using System.Text.Json;
using System.Text.Json.Nodes;

namespace DecisionEngine.Common
{
    /// <summary>
    /// Same-day request ledger helpers. Requests are the v1.1.4 JSON objects returned by
    /// <c>IRequestLedger.GetDayAsync</c> (fields read here: engine, decision, qty, finalApprovedQty,
    /// reqDate, info{brand, mcCd, mrp, flag}, colour, month, gm).
    /// </summary>
    public static class LedgerHelpers
    {
        /// <summary>The JS value of a JSON node: string, double, bool or null (objects/arrays → raw JSON text).</summary>
        public static object? JsValue(JsonNode? n)
        {
            if (n == null) return null;
            if (n is JsonValue v)
            {
                var el = v.GetValue<JsonElement>();
                return el.ValueKind switch
                {
                    JsonValueKind.String => el.GetString(),
                    JsonValueKind.Number => el.GetDouble(),
                    JsonValueKind.True => true,
                    JsonValueKind.False => false,
                    _ => null
                };
            }
            return n.ToJsonString();
        }

        /// <summary>JS value of <c>obj[key]</c> (null for missing / null).</summary>
        public static object? Get(JsonObject? o, string key) =>
            o != null && o.TryGetPropertyValue(key, out var n) ? JsValueSafe(n) : null;

        private static object? JsValueSafe(JsonNode? n)
        {
            try { return JsValue(n); }
            catch (InvalidOperationException)
            {
                // JsonValue created from a CLR value (not a JsonElement)
                if (n is JsonValue v)
                {
                    if (v.TryGetValue<string>(out var s)) return s;
                    if (v.TryGetValue<double>(out var d)) return d;
                    if (v.TryGetValue<int>(out var i)) return (double)i;
                    if (v.TryGetValue<long>(out var l)) return (double)l;
                    if (v.TryGetValue<decimal>(out var m)) return (double)m;
                    if (v.TryGetValue<bool>(out var b)) return b;
                }
                return n?.ToJsonString();
            }
        }

        /// <summary>Whether the property exists and is not JSON null (JS <c>!==undefined&amp;&amp;!==null</c>).</summary>
        public static bool HasValue(JsonObject? o, string key) =>
            o != null && o.TryGetPropertyValue(key, out var n) && n != null;

        /// <summary>A JS value used in arithmetic: numbers as is, numeric strings via Number(), else 0.</summary>
        public static double ToNumber(object? v) => v switch
        {
            null => 0,
            double d => d,
            bool b => b ? 1 : 0,
            string s => Js.Number(s),
            _ => Js.Number(Js.ToJsString(v))
        };

        /// <summary>The normaliser the ledger merges use: <c>String(v||'').trim().replace(/\.0$/,'').toLowerCase()</c>.</summary>
        public static string Norm(object? v) => Js.N(v);

        /// <summary>
        /// Port of index.html <c>_approvedQtyFor(req)</c> (line 1882): the quantity that was actually
        /// approved for a Single Concept (engine B) request — the confirmed <c>finalApprovedQty</c> when
        /// present, else <c>qty</c> for an Auto Allow, else 0. Null for every other engine.
        /// </summary>
        public static double? ApprovedQtyFor(JsonObject? req)
        {
            if (req == null || Get(req, "engine") is not string eng || eng != "B") return null;
            if (HasValue(req, "finalApprovedQty")) return ToNumber(Get(req, "finalApprovedQty"));
            if (Get(req, "decision") is string d && d == "Auto Allow")
            {
                var q = Get(req, "qty");
                return Js.Truthy(q) ? ToNumber(q) : 0;
            }
            return 0;
        }

        /// <summary>
        /// <c>req.reqDate!==todayStr</c> filter of the ledger merges. Requests returned by
        /// <c>GetDayAsync(today)</c> always belong to today; a request with no reqDate field at all is
        /// therefore treated as today's (v1.1.4's server always stamps reqDate, so this never differs).
        /// </summary>
        public static bool IsToday(JsonObject req, string today)
        {
            if (!req.TryGetPropertyValue("reqDate", out var n) || n == null) return true;
            return Get(req, "reqDate") is string s && s == today;
        }

        /// <summary><c>req.info</c> as an object (null when missing / not an object).</summary>
        public static JsonObject? Info(JsonObject req) =>
            req.TryGetPropertyValue("info", out var n) ? n as JsonObject : null;

        /// <summary>
        /// The bucket test of the ledger merges: <c>norm(req.info.brand)===norm(brand) &amp;&amp; … mcCd, mrp, flag</c>.
        /// </summary>
        public static bool SameBucket(JsonObject info, object? mc, object? mrp, object? brand, object? flag) =>
            Norm(Get(info, "brand")) == Norm(brand) && Norm(Get(info, "mcCd")) == Norm(mc) &&
            Norm(Get(info, "mrp")) == Norm(mrp) && Norm(Get(info, "flag")) == Norm(flag);
    }
}
