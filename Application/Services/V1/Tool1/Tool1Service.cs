using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Application.DTOs.Tool1;
using Application.Interfaces.V1;
using Application.Interfaces.V1.Tool1;
using Application.Services.V1.Engines.Common;
using Domain.Constants;
using Domain.Entities.Common;
using Domain.Exceptions;
using Domain.Records;

namespace Application.Services.V1.Tool1
{
    /// <summary>
    /// Tool 1 use cases. Validate = formSubmit's decision against today's ledger (after
    /// <c>Shared.refreshRequests</c>); Submit = the decision again with the confirmed quantity plus
    /// <c>Shared.saveRequest</c> (shared.js line 444) with its 409 re-check / force flow.
    /// </summary>
    /// <summary>Tool 1 behaviour switches.</summary>
    public sealed class Tool1Settings
    {
        /// <summary>
        /// Apply v1.1.4 README "What changed" item 1 ("Recommend Reduced Qty" when Max Permissible is above 0 but
        /// the proposal breaches; kept after confirming a smaller quantity). The shipped index.html still labels
        /// these Hold / Auto Allow; false reproduces that exactly (used by the parity tests). Default false —
        /// the running v1.1.4 behaviour; set "Tool1:ReducedLabel": true in appsettings to switch it on.
        /// </summary>
        public bool ReducedLabel { get; set; }
    }

    public sealed class Tool1Service : ITool1Service
    {
        /// <summary>shared.js REQ_DROP: request fields that hold whole BSR row sets, never stored.</summary>
        public static readonly string[] ReqDrop = { "peerPool", "cleanPool", "siblings", "qualSibs", "sibRanked", "repSibling" };

        private readonly IDatasetCache _cache;
        private readonly IRequestLedger _ledger;
        private readonly IAppClock _clock;
        private readonly Tool1Flow _flow;
        private readonly Tool1EngineB _engineB;
        private readonly Tool1FormOptions _options;

        private readonly Tool1Settings _settings;

        public Tool1Service(IDatasetCache cache, IRequestLedger ledger, IAppClock clock, Tool1Flow flow, Tool1EngineB engineB, Tool1FormOptions options,
                            Tool1Settings? settings = null)
        {
            _cache = cache; _ledger = ledger; _clock = clock; _flow = flow; _engineB = engineB; _options = options;
            _settings = settings ?? new Tool1Settings();
        }

        private void Relabel(Tool1Outcome o)
        {
            if (_settings.ReducedLabel) Tool1Flow.ApplyReducedLabel(o);
        }

        public async Task<Tool1FormOptionsDto> GetFormOptionsAsync(string? gm, CancellationToken ct = default)
        {
            var data = await _cache.GetCurrentAsync(ct);
            return _options.Build(data, gm, _clock.Today);
        }

        public async Task<Tool1ResultDto> ValidateAsync(Tool1RequestInputDto input, AppUserContext user, CancellationToken ct = default)
        {
            var data = await _cache.GetCurrentAsync(ct);
            var (gm, colour, month, qty) = Tool1Flow.CheckInput(data, input);
            var today = _clock.Today;
            var (reqs, seq) = await _ledger.GetDayAsync(today, ct);
            var o = _flow.Decide(data, reqs, today, NextId(reqs), gm, colour, month, qty, null);
            Relabel(o);
            return ToResult(o, data.Params, today, seq, o.NeedsConfirmation ? "needsConfirmation" : "decided", null);
        }

        public async Task<Tool1ResultDto> SubmitAsync(Tool1SubmitInputDto input, AppUserContext user, CancellationToken ct = default)
        {
            var data = await _cache.GetCurrentAsync(ct);
            var (gm, colour, month, qty) = Tool1Flow.CheckInput(data, input);
            var today = _clock.Today;
            var (reqs, seq) = await _ledger.GetDayAsync(today, ct);

            Tool1Outcome o;
            var seenSeq = input.SeenSeq;
            if (input.ConfirmedQty == null && !input.Declined)
            {
                o = _flow.Decide(data, reqs, today, NextId(reqs), gm, colour, month, qty, null);
                Relabel(o);
                if (o.NeedsConfirmation) return ToResult(o, data.Params, today, seq, "needsConfirmation", null);
            }
            else
            {
                // Decide against the ledger the user confirmed against (requests up to SeenSeq); anything
                // approved for this colour since then is caught by the save's 409 check, as in v1.1.4.
                var seen = reqs.Where(r => SeqOf(r) <= seenSeq).ToList();
                o = _flow.Decide(data, seen, today, NextId(seen), gm, colour, month, qty, new Tool1Confirmation(input.ConfirmedQty, input.Declined));
                Relabel(o);
            }

            var toasts = new List<string>();
            var r = o.Result;
            r["requestedBy"] = user.Name;
            r["createdAt"] = _clock.Now.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);
            r["colourKey"] = ColourKey(r);
            JsonObject? saved = null;
            string? conflictBy = null;
            var headroomChanged = false;
            try
            {
                try
                {
                    saved = await _ledger.SaveAsync(Slim(r), seenSeq, input.Force, user, ct);
                }
                catch (ConflictException ex)
                {
                    conflictBy = PayloadString(ex.Payload, "byOthers");
                    var (fresh, freshSeq) = await _ledger.GetDayAsync(today, ct);
                    var want = LedgerHelpers.ApprovedQtyFor(r);
                    if (o.Engine == "B" && JsOps.Or(want ?? 0, 0) > 0)
                    {
                        var chk = _engineB.Run(data, fresh, today, gm, colour, month, want!.Value, o.Info);
                        var chkMax = JsOps.Or(chk.MaxPermQty ?? 0, 0);
                        if (chkMax < want)
                        {
                            var capped = chkMax;
                            headroomChanged = true;
                            o.FinalApprovedQty = capped;
                            o.Decision = capped > 0 ? Decisions.AutoAllow : Decisions.Hold;
                            o.DecisionReason = "Headroom changed while you were confirming — " + (string.IsNullOrEmpty(conflictBy) ? "another user" : conflictBy) +
                                               " approved a request for this colour moments earlier. Approved qty capped from " + Js.Fmt(want.Value) + " to " +
                                               Js.Fmt(capped) + " units. " + o.DecisionReason;
                            r["finalApprovedQty"] = capped;
                            r["decision"] = o.Decision;
                            r["decisionReason"] = o.DecisionReason;
                            toasts.Add("⚠ Another request for this colour was approved first — your approved qty was capped to " + Js.Fmt(capped));
                        }
                    }
                    seenSeq = freshSeq;
                    saved = await _ledger.SaveAsync(Slim(r), freshSeq, true, user, ct);
                }
            }
            catch (AppException ex) when (ex is not ConflictException)
            {
                if (ex.Status == 403) toasts.Add("⚠ " + ex.Message);
                toasts.Add("⚠ Request decided but NOT saved to server (HTTP " + ex.Status + ")");
            }

            if (saved != null)
            {
                foreach (var k in new[] { "id", "reqId", "seq", "approval", "requestedBy", "requestedByEmail", "savedAt", "reqDate" })
                    if (saved.TryGetPropertyValue(k, out var v)) r[k] = v?.DeepClone();
                if (LedgerHelpers.Get(saved, "id") is double idv) o.Id = (int)idv;
                if (saved["approval"] is JsonObject ap)
                {
                    var who = ap["approverNames"] is JsonArray names ? string.Join(", ", names.Select(n => Js.ToJsString(NodeValue(n)))) : "";
                    toasts.Add("Watch List — sent to " + (who.Length > 0 ? who : "the admin (no category head mapped)") + " for approval");
                }
            }

            var res = ToResult(o, data.Params, today, saved != null ? (int)LedgerHelpers.ToNumber(LedgerHelpers.Get(saved, "seq")) : seenSeq,
                               saved != null ? "saved" : "notSaved", Slim(r));
            res.Toasts.InsertRange(0, toasts);
            res.HeadroomChanged = headroomChanged;
            res.ConflictByOthers = conflictBy;
            if (saved != null)
            {
                res.Id = o.Id;
                res.ReqId = LedgerHelpers.Get(saved, "reqId") as string;
                res.Seq = (int)LedgerHelpers.ToNumber(LedgerHelpers.Get(saved, "seq"));
                res.Approval = saved["approval"]?.DeepClone() as JsonObject;
            }
            return res;
        }

        public async Task<Tool1TodayDto> GetTodayAsync(AppUserContext user, bool onlyMine, CancellationToken ct = default)
        {
            var today = _clock.Today;
            List<JsonObject> visible;
            int seq;
            if (_ledger is IRequestService platform)
            {
                // server.py visible_to (own requests, requests they approve, their SCR Brand × MC × MRP) —
                // the same set GET api/v1/requests returns, which is what v1.1.4 kept in S.requests.
                var day = await platform.GetDayForUserAsync(today, user, ct);
                visible = day.Requests;
                seq = day.Seq;
            }
            else
            {
                var (reqs, s) = await _ledger.GetDayAsync(today, ct);
                visible = user.IsStaff ? reqs
                    : reqs.Where(r => Equals(LedgerHelpers.Get(r, "requestedByEmail"), user.Email) || IsApprover(r, user.Email)).ToList();
                seq = s;
            }
            var list = visible.Where(r => LedgerHelpers.IsToday(r, today)).ToList();
            if (onlyMine) list = list.Where(r => Equals(LedgerHelpers.Get(r, "requestedByEmail"), user.Email)).ToList();
            return new Tool1TodayDto { Rows = HistoryRows(list), Seq = seq, Count = visible.Count };
        }

        private static bool IsApprover(JsonObject r, string email) =>
            r["approval"] is JsonObject a && a["approvers"] is JsonArray ap && ap.Any(x => Equals(NodeValue(x), email));

        // ── helpers ──────────────────────────────────────────────────────────

        /// <summary>The JS value of any JSON node (JsonElement- or CLR-backed).</summary>
        private static object? NodeValue(JsonNode? n) => LedgerHelpers.Get(new JsonObject { ["v"] = n?.DeepClone() }, "v");

        private static Tool1ResultDto ToResult(Tool1Outcome o, Domain.Entities.Engine.EngineParams p, string today, int seq, string status, JsonObject? request)
        {
            var res = new Tool1ResultDto
            {
                Status = status, NeedsConfirmation = o.NeedsConfirmation, SeenSeq = seq, Engine = o.Engine, OptionType = o.OptionType,
                Decision = o.Decision, DecisionReason = o.DecisionReason, FinalApprovedQty = o.FinalApprovedQty,
                ApprovedQty = LedgerHelpers.ApprovedQtyFor(o.Result), Card = Tool1CardBuilder.Build(o, p, today),
                Request = request ?? Slim(o.Result)
            };
            if (o.NeedsConfirmation) { res.ProposedQty = o.Qty; res.MaxPermissibleQty = o.ConfirmMax; }
            return res;
        }

        /// <summary>S.reqCount after refreshRequests (max id) + 1.</summary>
        public static int NextId(IEnumerable<JsonObject> reqs) =>
            (int)reqs.Select(r => JsOps.Or(LedgerHelpers.ToNumber(LedgerHelpers.Get(r, "id")), 0)).DefaultIfEmpty(0).Max() + 1;

        private static double SeqOf(JsonObject r) => JsOps.Or(LedgerHelpers.ToNumber(LedgerHelpers.Get(r, "seq")), 0);

        /// <summary>shared.js <c>colourKey(r)</c>: brand|mcCd|mrp|flag|colour, normalised.</summary>
        public static string ColourKey(JsonObject r)
        {
            var i = LedgerHelpers.Info(r) ?? new JsonObject();
            return string.Join("|", Js.N(LedgerHelpers.Get(i, "brand")), Js.N(LedgerHelpers.Get(i, "mcCd")), Js.N(LedgerHelpers.Get(i, "mrp")),
                               Js.N(LedgerHelpers.Get(i, "flag")), Js.N(LedgerHelpers.Get(r, "colour")));
        }

        /// <summary>shared.js <c>slimRequest(r)</c>: drops REQ_DROP fields and any object larger than 60,000 JSON characters.</summary>
        public static JsonObject Slim(JsonObject r)
        {
            var o = new JsonObject();
            foreach (var kv in r)
            {
                if (ReqDrop.Contains(kv.Key)) continue;
                if (kv.Value is JsonObject or JsonArray && kv.Value.ToJsonString(Tool1Json.Options).Length > 60000) continue;
                o[kv.Key] = kv.Value?.DeepClone();
            }
            // re-parse so every value is a plain JSON value (numbers readable as double by any consumer)
            return JsonNode.Parse(o.ToJsonString(Tool1Json.Options))!.AsObject();
        }

        private static string? PayloadString(object? payload, string key)
        {
            if (payload == null) return null;
            try
            {
                var n = payload as JsonNode ?? JsonSerializer.SerializeToNode(payload, payload.GetType());
                return n is JsonObject o && o[key] is JsonValue v && v.TryGetValue<string>(out var s) ? s : null;
            }
            catch (NotSupportedException) { return null; }
        }

        /// <summary>Port of <c>renderCatHistory</c> rows (newest first) with shared.js <c>approvalBadge</c>.</summary>
        public static List<Tool1HistoryRowDto> HistoryRows(IEnumerable<JsonObject> todayReqs)
        {
            var rows = new List<Tool1HistoryRowDto>();
            foreach (var r in todayReqs.Reverse())
            {
                object? G(string k) => LedgerHelpers.Get(r, k);
                var info = LedgerHelpers.Info(r);
                var engine = Js.ToJsString(G("engine"));
                var approved = LedgerHelpers.ApprovedQtyFor(r);
                var decision = Js.ToJsString(G("decision"));
                var mcDesc = info != null ? LedgerHelpers.Get(info, "mcDesc") : null;
                var mrp = info != null ? LedgerHelpers.Get(info, "mrp") : null;
                rows.Add(new Tool1HistoryRowDto
                {
                    RequestId = Js.ToJsString(Js.Truthy(G("reqId")) ? G("reqId") : G("id")),
                    Gm = Tpl(r, "gm"), Colour = Tpl(r, "colour"),
                    McDesc = Js.Truthy(mcDesc) ? Js.ToJsString(mcDesc) : "—",
                    Mrp = "₹" + (Js.Truthy(mrp) ? Js.ToJsString(mrp) : "—"),
                    Month = Tpl(r, "month"), Qty = Js.Fmt(LedgerHelpers.ToNumber(G("qty"))),
                    EngineTag = "Eng " + engine, EngineClass = engine == "B" ? "et-b" : engine == "A" ? "et-a" : "et-new",
                    OptionType = Tpl(r, "optionType"), Decision = decision, DecisionClass = Tool1CardBuilder.PillClass(decision),
                    ApprovalBadge = Badge(r["approval"] as JsonObject),
                    ApprovedQty = approved != null ? Js.Fmt(approved.Value) : "—",
                    By = (Js.Truthy(G("requestedBy")) ? Js.ToJsString(G("requestedBy")) : "—").Replace("<", "").Replace(">", "").Replace("&", ""),
                    RequestedByEmail = G("requestedByEmail") as string
                });
            }
            return rows;
        }

        /// <summary>A field interpolated into a JS template string: missing → "undefined", null → "null".</summary>
        private static string Tpl(JsonObject r, string key) =>
            !r.TryGetPropertyValue(key, out var n) ? "undefined" : n == null ? "null" : Js.ToJsString(LedgerHelpers.Get(r, key));

        /// <summary>shared.js <c>Shared.approvalBadge(r)</c>.</summary>
        public static Tool1BadgeDto? Badge(JsonObject? a)
        {
            if (a == null) return null;
            string S(string k) => Js.ToJsString(LedgerHelpers.Get(a, k));
            var by = Js.Truthy(LedgerHelpers.Get(a, "byName")) ? S("byName") : Js.Truthy(LedgerHelpers.Get(a, "by")) ? S("by") : "?";
            var names = a["approverNames"] is JsonArray n ? string.Join(", ", n.Select(x => Js.ToJsString(NodeValue(x)))) : "";
            var (c, bg, text) = S("status") switch
            {
                "pending" => ("#B45309", "#FFF7ED", "⏳ Awaiting " + (names.Length > 0 ? names : "admin")),
                "approved" => ("#00873E", "#DCFCE7", "✓ Approved by " + by + (LedgerHelpers.HasValue(a, "qty") ? " · " + Js.Fmt(LedgerHelpers.ToNumber(LedgerHelpers.Get(a, "qty"))) : "")),
                "rejected" => ("#DC2626", "#FEF2F2", "✕ Rejected by " + by),
                "cleared" => ("#6B7280", "#F3F4F6", "⊘ Cleared by admin – no longer waiting"),
                _ => ("#6B7280", "#F3F4F6", S("status"))
            };
            return new Tool1BadgeDto { Text = text, Colour = c, Background = bg, Title = Js.Truthy(LedgerHelpers.Get(a, "comment")) ? S("comment") : "" };
        }
    }
}
