using System.Text.Json.Nodes;
using DecisionEngine.Common;
using DecisionEngine.Results.Tool1;

namespace DecisionEngine.Tool1
{
    /// <summary>
    /// Pure helpers over the stored Tool 1 request objects (ports of shared.js <c>colourKey</c>,
    /// <c>slimRequest</c>, <c>approvalBadge</c> and index.html <c>renderCatHistory</c>).
    /// </summary>
    public static class Tool1Requests
    {
        /// <summary>shared.js REQ_DROP: request fields that hold whole BSR row sets, never stored.</summary>
        public static readonly string[] ReqDrop = { "peerPool", "cleanPool", "siblings", "qualSibs", "sibRanked", "repSibling" };

        /// <summary>The JS value of any JSON node (JsonElement- or CLR-backed).</summary>
        public static object? NodeValue(JsonNode? n) => LedgerHelpers.Get(new JsonObject { ["v"] = n?.DeepClone() }, "v");

        /// <summary>S.reqCount after refreshRequests (max id) + 1.</summary>
        public static int NextId(IEnumerable<JsonObject> reqs) =>
            (int)reqs.Select(r => JsOps.Or(LedgerHelpers.ToNumber(LedgerHelpers.Get(r, "id")), 0)).DefaultIfEmpty(0).Max() + 1;

        /// <summary>The request's ledger sequence number (0 when missing).</summary>
        public static double SeqOf(JsonObject r) => JsOps.Or(LedgerHelpers.ToNumber(LedgerHelpers.Get(r, "seq")), 0);

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

        /// <summary>Whether <paramref name="email"/> is one of the request's approvers.</summary>
        public static bool IsApprover(JsonObject r, string email) =>
            r["approval"] is JsonObject a && a["approvers"] is JsonArray ap && ap.Any(x => Equals(NodeValue(x), email));

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
