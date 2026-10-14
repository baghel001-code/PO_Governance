using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Application.Services.V1.Engines.Common;
using Domain.Entities.Common;

namespace Application.Services.V1.Analytics.Common
{
    /// <summary>
    /// JS semantics the analytics screens rely on when they read request / Tool 2 result objects
    /// (plain JSON objects in v1.1.4): <c>undefined</c> vs <c>null</c>, <c>a||b</c>, string concatenation,
    /// <c>Number(v)</c>, <c>toFixed</c> with the sign of small negatives and <c>JSON.stringify</c>.
    /// </summary>
    public static class AnalyticsJs
    {
        /// <summary>True when <c>o[key]</c> is not <c>undefined</c> (the property exists, even if JSON null).</summary>
        public static bool Defined(JsonObject? o, string key) => o != null && o.TryGetPropertyValue(key, out _);

        /// <summary>JS value of <c>o[key]</c>: string, double, bool, null (also for undefined) or raw JSON text for objects.</summary>
        public static object? Get(JsonObject? o, string key) => LedgerHelpers.Get(o, key);

        /// <summary><c>o.key || fallback</c>.</summary>
        public static object? Or(JsonObject? o, string key, object? fallback)
        {
            if (o == null || !o.TryGetPropertyValue(key, out var n) || n == null) return fallback;
            if (n is JsonObject or JsonArray) return n;
            var v = LedgerHelpers.Get(o, key);
            return Js.Truthy(v) ? v : fallback;
        }

        /// <summary><c>String(o.key || fallback)</c>.</summary>
        public static string OrStr(JsonObject? o, string key, string fallback) => Str(Or(o, key, fallback));

        /// <summary>JS <c>String(v)</c> / template literal of a JS value (null → "null").</summary>
        public static string Str(object? v) => v switch
        {
            null => "null",
            JsonObject => "[object Object]",
            JsonArray a => string.Join(",", a.Select(x => x == null ? "" : Str(x is JsonValue ? LedgerHelpers.JsValue(x) : x))),
            _ => Js.ToJsString(v)
        };

        /// <summary><c>''+o.key</c> — "undefined" when the property is missing, "null" when JSON null.</summary>
        public static string Concat(JsonObject? o, string key)
        {
            if (o == null || !o.TryGetPropertyValue(key, out var n)) return "undefined";
            if (n == null) return "null";
            if (n is JsonObject or JsonArray) return Str(n);
            return Str(LedgerHelpers.Get(o, key));
        }

        /// <summary><c>(r.info &amp;&amp; r.info.key) || fallback</c> as a string.</summary>
        public static string InfoStr(JsonObject r, string key, string fallback) => OrStr(LedgerHelpers.Info(r), key, fallback);

        /// <summary>JS <c>Number(o.key)</c> (undefined → NaN, null → 0, bool → 1/0, numeric strings parsed).</summary>
        public static double Number(JsonObject? o, string key)
        {
            if (o == null || !o.TryGetPropertyValue(key, out var n)) return double.NaN;
            if (n == null) return 0;
            if (n is JsonObject) return double.NaN;
            return Number(LedgerHelpers.Get(o, key));
        }

        public static double Number(object? v) => v switch
        {
            null => 0,
            double d => d,
            bool b => b ? 1 : 0,
            string s => Js.Number(s),
            _ => Js.Number(Js.ToJsString(v))
        };

        /// <summary>A numeric field used with <c>+</c>: undefined → NaN, null → 0.</summary>
        public static double Arith(JsonObject? o, string key) => Number(o, key);

        /// <summary><c>(o.key || 0)</c> as a number.</summary>
        public static double OrZero(JsonObject? o, string key)
        {
            var v = Or(o, key, null);
            return v == null ? 0 : Number(v);
        }

        /// <summary><c>o.key != null</c> (neither undefined nor null).</summary>
        public static bool NotNull(JsonObject? o, string key) => LedgerHelpers.HasValue(o, key);

        /// <summary>JS <c>x.toFixed(digits)</c>, keeping the "-" of small negatives ((-0.04).toFixed(1) = "-0.0").</summary>
        public static string ToFixed(double x, int digits)
        {
            var s = Js.ToFixed(x, digits);
            if (x < 0 && !s.StartsWith('-') && double.IsFinite(x)) s = "-" + s;
            return s;
        }

        /// <summary><c>Math.round(x)</c> as the integer string JS would print.</summary>
        public static string RoundStr(double x) => Js.NumberToString(Js.Round(x));

        /// <summary>JS default <c>Array.prototype.sort()</c> on distinct strings (UTF-16 code unit order).</summary>
        public static List<string> SortStrings(IEnumerable<string> values)
        {
            var l = values.ToList();
            l.Sort(string.CompareOrdinal);
            return l;
        }

        /// <summary>Distinct values in first-seen order (<c>[...new Set(arr)]</c>).</summary>
        public static List<string> Distinct(IEnumerable<string> values)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var l = new List<string>();
            foreach (var v in values) if (seen.Add(v)) l.Add(v);
            return l;
        }

        /// <summary>Lower-cases like JS <c>toLowerCase()</c>.</summary>
        public static string Lower(string s) => s.ToLowerInvariant();

        // ── JSON.stringify ───────────────────────────────────────────────────

        /// <summary>
        /// JS <c>JSON.stringify(value)</c> for a JsonNode tree: no whitespace, numbers as JS prints them,
        /// non-ASCII kept as is, only quotes / backslashes / control characters escaped.
        /// </summary>
        public static string Stringify(JsonNode? node)
        {
            var sb = new StringBuilder();
            Write(sb, node);
            return sb.ToString();
        }

        private static void Write(StringBuilder sb, JsonNode? n)
        {
            switch (n)
            {
                case null: sb.Append("null"); break;
                case JsonObject o:
                    sb.Append('{');
                    var first = true;
                    foreach (var kv in OrderedProps(o))
                    {
                        if (!first) sb.Append(',');
                        first = false;
                        Quote(sb, kv.Key); sb.Append(':'); Write(sb, kv.Value);
                    }
                    sb.Append('}');
                    break;
                case JsonArray a:
                    sb.Append('[');
                    for (var i = 0; i < a.Count; i++) { if (i > 0) sb.Append(','); Write(sb, a[i]); }
                    sb.Append(']');
                    break;
                case JsonValue v:
                    var jv = v.TryGetValue<JsonElement>(out var el) ? FromElement(el) : FromClr(v);
                    switch (jv)
                    {
                        case null: sb.Append("null"); break;
                        case string s: Quote(sb, s); break;
                        case bool b: sb.Append(b ? "true" : "false"); break;
                        case double d: sb.Append(double.IsFinite(d) ? Js.NumberToString(d) : "null"); break;
                    }
                    break;
            }
        }

        /// <summary>Object properties in JS order (integer-like keys first, ascending).</summary>
        private static IEnumerable<KeyValuePair<string, JsonNode?>> OrderedProps(JsonObject o)
        {
            var list = o.ToList();
            var idx = list.Where(k => JsObjectMap<int>.IsArrayIndex(k.Key)).OrderBy(k => ulong.Parse(k.Key, CultureInfo.InvariantCulture));
            return idx.Concat(list.Where(k => !JsObjectMap<int>.IsArrayIndex(k.Key)));
        }

        private static object? FromElement(JsonElement el) => el.ValueKind switch
        {
            JsonValueKind.String => el.GetString(),
            JsonValueKind.Number => el.GetDouble(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => null
        };

        private static object? FromClr(JsonValue v)
        {
            if (v.TryGetValue<string>(out var s)) return s;
            if (v.TryGetValue<double>(out var d)) return d;
            if (v.TryGetValue<int>(out var i)) return (double)i;
            if (v.TryGetValue<long>(out var l)) return (double)l;
            if (v.TryGetValue<decimal>(out var m)) return (double)m;
            if (v.TryGetValue<bool>(out var b)) return b;
            return null;
        }

        private static void Quote(StringBuilder sb, string s)
        {
            sb.Append('"');
            for (var i = 0; i < s.Length; i++)
            {
                var c = s[i];
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\b': sb.Append("\\b"); break;
                    case '\f': sb.Append("\\f"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                        else if (char.IsSurrogate(c))
                        {
                            if (char.IsHighSurrogate(c) && i + 1 < s.Length && char.IsLowSurrogate(s[i + 1])) { sb.Append(c).Append(s[i + 1]); i++; }
                            else sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                        }
                        else sb.Append(c);
                        break;
                }
            }
            sb.Append('"');
        }

        /// <summary>A JSON value node for a JS value (string / double / bool / null) — the JSON.stringify input.</summary>
        public static JsonNode? Node(object? v) => v switch
        {
            null => null,
            string s => JsonValue.Create(s),
            double d => JsonValue.Create(d),
            int i => JsonValue.Create((double)i),
            bool b => JsonValue.Create(b),
            JsonNode n => n.DeepClone(),
            _ => JsonValue.Create(Js.ToJsString(v))
        };

        /// <summary>Copies <c>o[key]</c> into <paramref name="target"/> as <paramref name="name"/> unless undefined (JSON.stringify drops undefined).</summary>
        public static void CopyIfDefined(JsonObject target, string name, JsonObject? o, string key)
        {
            if (o == null || !o.TryGetPropertyValue(key, out var n)) return;
            target[name] = n?.DeepClone();
        }
    }
}
