using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Application.Services.V1.Engines.Common;
using Domain.Entities.Common;

namespace Application.Services.V1.Tool1
{
    /// <summary>
    /// JSON options for Tool 1 request objects: camelCase names come from the DTO attributes, non-finite
    /// numbers are written as null (JSON.stringify semantics) and non-ASCII text is not escaped.
    /// </summary>
    public static class Tool1Json
    {
        public static readonly JsonSerializerOptions Options = Create();

        private static JsonSerializerOptions Create()
        {
            var o = new JsonSerializerOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
            o.Converters.Add(new JsDoubleConverter());
            return o;
        }

        /// <summary>Serialises a DTO into a JsonObject (null → null).</summary>
        public static JsonObject? ToObject(object? o) =>
            o == null ? null : JsonSerializer.SerializeToNode(o, o.GetType(), Options) as JsonObject;

        public static JsonNode? ToNode(object? o) =>
            o == null ? null : JsonSerializer.SerializeToNode(o, o.GetType(), Options);

        /// <summary>A double written like JSON.stringify writes a JS number (NaN / ±Infinity → null).</summary>
        public sealed class JsDoubleConverter : JsonConverter<double>
        {
            public override double Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
                reader.TokenType == JsonTokenType.Null ? double.NaN : reader.GetDouble();

            public override void Write(Utf8JsonWriter writer, double value, JsonSerializerOptions options)
            {
                if (double.IsFinite(value)) writer.WriteNumberValue(value);
                else writer.WriteNullValue();
            }
        }
    }

    /// <summary>Small JS idioms used by the Tool 1 engines (index.html first script).</summary>
    public static class Tool1JsHelpers
    {
        private static readonly Regex DayCol = new(@"^D(?:AY)?\s*[0-9]{1,3}$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex Slash = new(@"^([0-9]{1,2})/([0-9]{1,2})/([0-9]{4})$", RegexOptions.Compiled);
        private static readonly Regex Iso = new(@"^([0-9]{4})-([0-9]{2})-([0-9]{2})", RegexOptions.Compiled);

        /// <summary><c>Object.keys(row)</c>: array-index keys ascending first, then insertion order.</summary>
        public static List<string> JsKeys(SheetRow r)
        {
            var keys = r.Keys.ToList();
            var idx = keys.Where(JsObjectMap<int>.IsArrayIndex).OrderBy(k => ulong.Parse(k, CultureInfo.InvariantCulture)).ToList();
            idx.AddRange(keys.Where(k => !JsObjectMap<int>.IsArrayIndex(k)));
            return idx;
        }

        /// <summary>Port of index.html <c>isDayCol</c>: "Day N" / "DN" / "D N".</summary>
        public static bool IsDayCol(string k) => DayCol.IsMatch(k.Trim());

        /// <summary>Port of <c>toYM</c>: "MM.YYYY" → "YYYY-MM" (trimmed), null otherwise.</summary>
        public static string? ToYM(string k)
        {
            var p = MonthMath.ParseMmyyyy(k.Trim());
            if (p == null) return null;
            var t = k.Trim();
            return t.Substring(3, 4) + "-" + t.Substring(0, 2);
        }

        /// <summary>
        /// Port of index.html <c>dateToMMYYYY(d)</c> for the cell value types a stored row holds
        /// (string, double, bool, null). Excel serials (&gt; 40000) are days since 1899-12-30.
        /// </summary>
        public static string? DateToMMYYYY(object? d)
        {
            if (d == null) return null;
            if (d is double n)
            {
                if (n == 0 || double.IsNaN(n)) return null;
                if (n > 40000)
                {
                    DateTime dt;
                    try { dt = new DateTime(1899, 12, 30).AddDays(n); } catch (ArgumentOutOfRangeException) { return null; }
                    return dt.Month.ToString(CultureInfo.InvariantCulture).PadLeft(2, '0') + "." + dt.Year.ToString(CultureInfo.InvariantCulture);
                }
                return null;
            }
            if (d is string s)
            {
                if (s.Length == 0) return null;
                if (MonthMath.IsMMYYYY(s)) return s;
                var m1 = Slash.Match(s);
                if (m1.Success)
                    return MonthMath.ParseInt(m1.Groups[1].Value).ToString(CultureInfo.InvariantCulture).PadLeft(2, '0') + "." + m1.Groups[3].Value;
                var m2 = Iso.Match(s);
                if (m2.Success) return m2.Groups[2].Value + "." + m2.Groups[1].Value;
            }
            return null;
        }

        /// <summary><c>String(v).trim().replace(/\.0$/,'')</c>.</summary>
        public static string StripDot0(string? v)
        {
            var s = (v ?? "").Trim();
            return s.EndsWith(".0", StringComparison.Ordinal) ? s[..^2] : s;
        }

        /// <summary>JS <c>str.replace('.0','')</c> — first occurrence anywhere.</summary>
        public static string ReplaceFirstDot0(string s)
        {
            var i = s.IndexOf(".0", StringComparison.Ordinal);
            return i < 0 ? s : s.Remove(i, 2);
        }

        /// <summary><c>arr.slice(-n)</c> with JS integer conversion of n.</summary>
        public static List<T> SliceNeg<T>(IReadOnlyList<T> list, double n)
        {
            var start = double.IsNaN(n) ? 0 : -Math.Truncate(n);
            int len = list.Count, s;
            if (double.IsNegativeInfinity(start)) s = 0;
            else if (start < 0) s = (int)Math.Max(len + start, 0);
            else s = (int)Math.Min(start, len);
            return list.Skip(s).ToList();
        }

        /// <summary>JSON.stringify of a string array (keys are plain text).</summary>
        public static string JsonArray(IEnumerable<string> keys) =>
            JsonSerializer.Serialize(keys.ToList(), Tool1Json.Options);
    }
}
