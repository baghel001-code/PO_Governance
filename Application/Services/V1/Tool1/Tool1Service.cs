using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Application.Interfaces.V1;
using Application.Interfaces.V1.Tool1;
using DecisionEngine.Common;
using DecisionEngine.Model;
using DecisionEngine.Results.Tool1;
using DecisionEngine.Tool1;
using Domain.Exceptions;
using Domain.Records;

namespace Application.Services.V1.Tool1
{
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

    /// <summary>
    /// Tool 1 use cases. Validate = formSubmit's decision against today's ledger (after
    /// <c>Shared.refreshRequests</c>); Submit = the decision again with the confirmed quantity plus
    /// <c>Shared.saveRequest</c> (shared.js line 444) with its 409 re-check / force flow. The decisions are made by
    /// <see cref="ITool1DecisionEngine"/> (DecisionEngine); this service loads the snapshot (<see cref="IDatasetCache"/>)
    /// and today's ledger (<see cref="IRequestLedger"/>), reads the clock and saves the request.
    /// </summary>
    public sealed class Tool1Service : ITool1Service
    {
        private readonly IDatasetCache _cache;
        private readonly IRequestLedger _ledger;
        private readonly IAppClock _clock;
        private readonly ITool1DecisionEngine _engine;
        private readonly Tool1Settings _settings;

        public Tool1Service(IDatasetCache cache, IRequestLedger ledger, IAppClock clock, ITool1DecisionEngine engine, Tool1Settings? settings = null)
        {
            _cache = cache; _ledger = ledger; _clock = clock; _engine = engine;
            _settings = settings ?? new Tool1Settings();
        }

        public async Task<Tool1FormOptionsDto> GetFormOptionsAsync(string? gm, CancellationToken ct = default)
        {
            var data = await _cache.GetCurrentAsync(ct);
            return _engine.FormOptions(data, gm, _clock.Today);
        }

        public async Task<Tool1ResultDto> ValidateAsync(Tool1RequestInputDto input, AppUserContext user, CancellationToken ct = default)
        {
            var data = await _cache.GetCurrentAsync(ct);
            var (gm, colour, month, qty) = EngineErrors.Run(() => _engine.CheckInput(data, input));
            var today = _clock.Today;
            var (reqs, seq) = await _ledger.GetDayAsync(today, ct);
            var o = Decide(data, reqs, today, Tool1Requests.NextId(reqs), gm, colour, month, qty, null);
            return ToResult(o, data.Params, today, seq, o.NeedsConfirmation ? "needsConfirmation" : "decided", null);
        }

        private Tool1Outcome Decide(EngineData data, IReadOnlyList<JsonObject> reqs, string today, int id,
                                    string gm, string colour, string month, double qty, Tool1Confirmation? confirmation) =>
            EngineErrors.Run(() => _engine.Decide(data, reqs, today, id, gm, colour, month, qty, confirmation, _settings.ReducedLabel));

        public async Task<Tool1ResultDto> SubmitAsync(Tool1SubmitInputDto input, AppUserContext user, CancellationToken ct = default)
        {
            var data = await _cache.GetCurrentAsync(ct);
            var (gm, colour, month, qty) = EngineErrors.Run(() => _engine.CheckInput(data, input));
            var today = _clock.Today;
            var (reqs, seq) = await _ledger.GetDayAsync(today, ct);

            Tool1Outcome o;
            var seenSeq = input.SeenSeq;
            if (input.ConfirmedQty == null && !input.Declined)
            {
                o = Decide(data, reqs, today, Tool1Requests.NextId(reqs), gm, colour, month, qty, null);
                if (o.NeedsConfirmation) return ToResult(o, data.Params, today, seq, "needsConfirmation", null);
            }
            else
            {
                // Decide against the ledger the user confirmed against (requests up to SeenSeq); anything
                // approved for this colour since then is caught by the save's 409 check, as in v1.1.4.
                var seen = reqs.Where(r => Tool1Requests.SeqOf(r) <= seenSeq).ToList();
                o = Decide(data, seen, today, Tool1Requests.NextId(seen), gm, colour, month, qty, new Tool1Confirmation(input.ConfirmedQty, input.Declined));
            }

            var toasts = new List<string>();
            var r = o.Result;
            r["requestedBy"] = user.Name;
            r["createdAt"] = _clock.Now.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);
            r["colourKey"] = Tool1Requests.ColourKey(r);
            JsonObject? saved = null;
            string? conflictBy = null;
            var headroomChanged = false;
            try
            {
                try
                {
                    saved = await _ledger.SaveAsync(Tool1Requests.Slim(r), seenSeq, input.Force, user, ct);
                }
                catch (ConflictException ex)
                {
                    conflictBy = PayloadString(ex.Payload, "byOthers");
                    var (fresh, freshSeq) = await _ledger.GetDayAsync(today, ct);
                    var want = LedgerHelpers.ApprovedQtyFor(r);
                    if (o.Engine == "B" && JsOps.Or(want ?? 0, 0) > 0)
                    {
                        var chk = _engine.RunEngineB(data, fresh, today, gm, colour, month, want!.Value, o.Info);
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
                    saved = await _ledger.SaveAsync(Tool1Requests.Slim(r), freshSeq, true, user, ct);
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
                    var who = ap["approverNames"] is JsonArray names ? string.Join(", ", names.Select(n => Js.ToJsString(Tool1Requests.NodeValue(n)))) : "";
                    toasts.Add("Watch List — sent to " + (who.Length > 0 ? who : "the admin (no category head mapped)") + " for approval");
                }
            }

            var res = ToResult(o, data.Params, today, saved != null ? (int)LedgerHelpers.ToNumber(LedgerHelpers.Get(saved, "seq")) : seenSeq,
                               saved != null ? "saved" : "notSaved", Tool1Requests.Slim(r));
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
                // the same set PO_REQUESTS_GET returns, which is what v1.1.4 kept in S.requests.
                var day = await platform.GetDayForUserAsync(today, user, ct);
                visible = day.Requests;
                seq = day.Seq;
            }
            else
            {
                var (reqs, s) = await _ledger.GetDayAsync(today, ct);
                visible = user.IsStaff ? reqs
                    : reqs.Where(r => Equals(LedgerHelpers.Get(r, "requestedByEmail"), user.Email) || Tool1Requests.IsApprover(r, user.Email)).ToList();
                seq = s;
            }
            var list = visible.Where(r => LedgerHelpers.IsToday(r, today)).ToList();
            if (onlyMine) list = list.Where(r => Equals(LedgerHelpers.Get(r, "requestedByEmail"), user.Email)).ToList();
            return new Tool1TodayDto { Rows = Tool1Requests.HistoryRows(list), Seq = seq, Count = visible.Count };
        }

        // ── helpers ──────────────────────────────────────────────────────────

        private Tool1ResultDto ToResult(Tool1Outcome o, EngineParams p, string today, int seq, string status, JsonObject? request)
        {
            var res = new Tool1ResultDto
            {
                Status = status, NeedsConfirmation = o.NeedsConfirmation, SeenSeq = seq, Engine = o.Engine, OptionType = o.OptionType,
                Decision = o.Decision, DecisionReason = o.DecisionReason, FinalApprovedQty = o.FinalApprovedQty,
                ApprovedQty = LedgerHelpers.ApprovedQtyFor(o.Result), Card = _engine.BuildCard(o, p, today),
                Request = request ?? Tool1Requests.Slim(o.Result)
            };
            if (o.NeedsConfirmation) { res.ProposedQty = o.Qty; res.MaxPermissibleQty = o.ConfirmMax; }
            return res;
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
    }
}
