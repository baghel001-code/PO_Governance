using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Domain.Entities.Store
{
    /// <summary>
    /// JSON helpers with Python semantics, used by the storage / platform port:
    /// <c>json.dumps</c> output (separators ", " and ": "), Python truthiness and <c>str()</c> of values,
    /// tolerant field access on loosely typed records.
    /// </summary>
    public static class PyJson
    {
        public static readonly JsonSerializerOptions Options = new()
        {
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowReadingFromString,
        };

        /// <summary>
        /// Python <c>json.dumps(obj, ensure_ascii=…)</c>. The store writes with ensure_ascii=False
        /// (storage.py _dumps); audit details use the default ensure_ascii=True.
        /// </summary>
        public static string Dumps(JsonNode? node, bool ensureAscii = false)
        {
            var sb = new StringBuilder();
            Write(sb, node, ensureAscii);
            return sb.ToString();
        }

        public static string Dumps<T>(T value, bool ensureAscii = false) => Dumps(ToNode(value), ensureAscii);

        private static void Write(StringBuilder sb, JsonNode? node, bool ascii)
        {
            switch (node)
            {
                case null: sb.Append("null"); break;
                case JsonObject o:
                    {
                        sb.Append('{');
                        var first = true;
                        foreach (var kv in o)
                        {
                            if (!first) sb.Append(", ");
                            first = false;
                            WriteString(sb, kv.Key, ascii);
                            sb.Append(": ");
                            Write(sb, kv.Value, ascii);
                        }
                        sb.Append('}');
                        break;
                    }
                case JsonArray a:
                    {
                        sb.Append('[');
                        for (var i = 0; i < a.Count; i++)
                        {
                            if (i > 0) sb.Append(", ");
                            Write(sb, a[i], ascii);
                        }
                        sb.Append(']');
                        break;
                    }
                case JsonValue v:
                    switch (v.GetValueKind())
                    {
                        case JsonValueKind.String: WriteString(sb, AsString(v) ?? "", ascii); break;
                        case JsonValueKind.True: sb.Append("true"); break;
                        case JsonValueKind.False: sb.Append("false"); break;
                        case JsonValueKind.Null: sb.Append("null"); break;
                        default: sb.Append(v.ToJsonString()); break;
                    }
                    break;
            }
        }

        private static void WriteString(StringBuilder sb, string s, bool ascii)
        {
            sb.Append('"');
            foreach (var c in s)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    case '\b': sb.Append("\\b"); break;
                    case '\f': sb.Append("\\f"); break;
                    default:
                        if (c < 0x20 || (ascii && c > 0x7e))
                            sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                        else sb.Append(c);
                        break;
                }
            }
            sb.Append('"');
        }

        public static JsonNode? Parse(string? text) =>
            string.IsNullOrEmpty(text) ? null : JsonNode.Parse(text, documentOptions: new JsonDocumentOptions { AllowTrailingCommas = true });

        public static JsonNode? ToNode<T>(T value) => JsonSerializer.SerializeToNode(value, Options);

        public static T? FromNode<T>(JsonNode? node) => node == null ? default : node.Deserialize<T>(Options);

        public static JsonObject ToObject<T>(T value) => (JsonObject)ToNode(value)!;

        /// <summary>Python truthiness of a JSON value.</summary>
        public static bool Truthy(JsonNode? n) => n switch
        {
            null => false,
            JsonObject o => o.Count > 0,
            JsonArray a => a.Count > 0,
            JsonValue v => v.GetValueKind() switch
            {
                JsonValueKind.True => true,
                JsonValueKind.False or JsonValueKind.Null => false,
                JsonValueKind.String => (AsString(v) ?? "").Length > 0,
                JsonValueKind.Number => AsDouble(v) != 0,
                _ => true
            },
            _ => true
        };

        /// <summary>The string of a string JSON value (null for other kinds).</summary>
        public static string? AsString(JsonNode? n)
        {
            if (n is not JsonValue v || v.GetValueKind() != JsonValueKind.String) return null;
            return v.TryGetValue<string>(out var s) ? s : JsonSerializer.Deserialize<string>(v.ToJsonString());
        }

        public static double AsDouble(JsonNode? n)
        {
            if (n is not JsonValue v) return 0;
            if (v.TryGetValue<double>(out var d)) return d;
            if (v.TryGetValue<long>(out var l)) return l;
            if (v.TryGetValue<int>(out var i)) return i;
            if (v.TryGetValue<decimal>(out var m)) return (double)m;
            if (v.GetValueKind() == JsonValueKind.Number)
                return double.Parse(v.ToJsonString(), CultureInfo.InvariantCulture);
            if (v.GetValueKind() == JsonValueKind.String &&
                double.TryParse(AsString(v), NumberStyles.Float, CultureInfo.InvariantCulture, out var p)) return p;
            if (v.GetValueKind() == JsonValueKind.True) return 1;
            return 0;
        }

        public static bool IsNumber(JsonNode? n) => n is JsonValue v && v.GetValueKind() == JsonValueKind.Number;
        public static bool IsString(JsonNode? n) => n is JsonValue v && v.GetValueKind() == JsonValueKind.String;
        public static bool IsBool(JsonNode? n) => n is JsonValue v && v.GetValueKind() is JsonValueKind.True or JsonValueKind.False;

        /// <summary>Python <c>str(v)</c> of a JSON value (None → "None", True → "True", numbers as written).</summary>
        public static string Str(JsonNode? n) => n switch
        {
            null => "None",
            JsonValue v => v.GetValueKind() switch
            {
                JsonValueKind.String => AsString(v) ?? "",
                JsonValueKind.True => "True",
                JsonValueKind.False => "False",
                JsonValueKind.Null => "None",
                _ => v.ToJsonString()
            },
            _ => Dumps(n, true)
        };

        /// <summary>Python <c>str(v or "")</c>.</summary>
        public static string StrOrEmpty(JsonNode? n) => Truthy(n) ? Str(n) : "";

        /// <summary>Python <c>str()</c> of a cell value of a data-file row (string, double, bool or null).</summary>
        public static string Str(object? v) => v switch
        {
            null => "None",
            string s => s,
            bool b => b ? "True" : "False",
            double d => FormatDouble(d),
            float f => FormatDouble(f),
            int i => i.ToString(CultureInfo.InvariantCulture),
            long l => l.ToString(CultureInfo.InvariantCulture),
            decimal m => m.ToString(CultureInfo.InvariantCulture),
            JsonNode jn => Str(jn),
            _ => Convert.ToString(v, CultureInfo.InvariantCulture) ?? ""
        };

        /// <summary>Python truthiness of a row cell value.</summary>
        public static bool Truthy(object? v) => v switch
        {
            null => false,
            string s => s.Length > 0,
            bool b => b,
            double d => d != 0,
            float f => f != 0,
            int i => i != 0,
            long l => l != 0,
            decimal m => m != 0,
            JsonNode jn => Truthy(jn),
            _ => true
        };

        /// <summary>Python <c>str(v or "")</c> of a row cell.</summary>
        public static string StrOrEmpty(object? v) => Truthy(v) ? Str(v) : "";

        // JSON numbers in the stored rows are ints when integral (json.loads → int → "1299").
        private static string FormatDouble(double d)
        {
            if (double.IsNaN(d)) return "nan";
            if (double.IsInfinity(d)) return d > 0 ? "inf" : "-inf";
            if (d == Math.Floor(d) && Math.Abs(d) < 1e16) return ((long)d).ToString(CultureInfo.InvariantCulture);
            return d.ToString("R", CultureInfo.InvariantCulture);
        }

        // ── tolerant field access (dict.get) ─────────────────────────────

        public static JsonNode? Get(this JsonObject? o, string key) =>
            o != null && o.TryGetPropertyValue(key, out var v) ? v : null;

        /// <summary>String value of o[key]; numbers / bools as their text; null when missing / null.</summary>
        public static string? GetStr(this JsonObject? o, string key)
        {
            var n = o.Get(key);
            if (n == null) return null;
            if (n is JsonValue v)
                return v.GetValueKind() switch
                {
                    JsonValueKind.String => AsString(v),
                    JsonValueKind.Null => null,
                    JsonValueKind.True => "True",
                    JsonValueKind.False => "False",
                    _ => v.ToJsonString()
                };
            return n.ToJsonString();
        }

        /// <summary>Python <c>d.get(key) or ""</c> as string.</summary>
        public static string GetStrOrEmpty(this JsonObject? o, string key)
        {
            var n = o.Get(key);
            return Truthy(n) ? (o.GetStr(key) ?? "") : "";
        }

        /// <summary>Integer value of o[key] (numbers truncated, numeric strings parsed); <paramref name="dflt"/> otherwise.</summary>
        public static long GetLong(this JsonObject? o, string key, long dflt = 0)
        {
            var n = o.Get(key);
            if (n == null) return dflt;
            if (IsNumber(n)) return (long)AsDouble(n);
            if (IsString(n) && long.TryParse((AsString(n) ?? "").Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var l)) return l;
            if (IsBool(n)) return AsDouble(n) != 0 ? 1 : 0;
            return dflt;
        }

        public static bool GetTruthy(this JsonObject? o, string key) => Truthy(o.Get(key));

        public static JsonObject? GetObj(this JsonObject? o, string key) => o.Get(key) as JsonObject;

        public static List<string> GetStrList(this JsonObject? o, string key)
        {
            var list = new List<string>();
            if (o.Get(key) is JsonArray a)
                foreach (var x in a)
                {
                    var s = x is JsonValue ? Str(x) : null;
                    if (s != null) list.Add(s);
                }
            return list;
        }

        /// <summary>Deep copy of a node.</summary>
        public static T Clone<T>(this T node) where T : JsonNode => (T)node.DeepClone();
    }
}
