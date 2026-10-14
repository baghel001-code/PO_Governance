using System.Globalization;
using System.Text.RegularExpressions;
using Application.Services.V1.Engines.Common;
using Domain.Entities.Common;

namespace Application.Services.V1.Tool2
{
    /// <summary>
    /// Small index.html helpers Tool 2 needs that are not in the shared engine helpers:
    /// <c>isDayCol</c>, <c>toYM</c>, <c>dateToMMYYYY</c>, JS object key order of a row, and
    /// <c>Array.prototype.slice(-n)</c>.
    /// </summary>
    public static class Tool2Js
    {
        private static readonly Regex DayCol = new(@"^D(?:AY)?\s*[0-9]{1,3}$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex SlashDate = new(@"^([0-9]{1,2})/([0-9]{1,2})/([0-9]{4})$", RegexOptions.Compiled);
        private static readonly Regex IsoDate = new(@"^([0-9]{4})-([0-9]{2})-([0-9]{2})", RegexOptions.Compiled);

        /// <summary>index.html <c>isDayCol(k)</c> (line 1751).</summary>
        public static bool IsDayCol(string k) => DayCol.IsMatch(k.Trim());

        /// <summary>index.html <c>toYM(k)</c> (line 1747): "MM.YYYY" → "YYYY-MM".</summary>
        public static string? ToYM(string k)
        {
            var p = MonthMath.ParseMmyyyy(k.Trim());
            if (p == null) return null;
            var t = k.Trim();
            return t[3..] + "-" + t[..2];
        }

        /// <summary>index.html <c>dateToMMYYYY(d)</c> (line 1757) for a header name or a raw cell value.</summary>
        public static string? DateToMMYYYY(object? d)
        {
            if (d == null) return null;
            if (d is string s0 && s0.Length == 0) return null;
            if (d is bool b0 && !b0) return null;
            if (d is double dn && double.IsNaN(dn)) return null;
            if (d is DateTime dt) return dt.Month.ToString("00", CultureInfo.InvariantCulture) + "." + dt.Year.ToString(CultureInfo.InvariantCulture);
            if (d is double n)
            {
                if (n > 40000)
                {
                    var x = new DateTime(1899, 12, 30).AddDays(n);
                    return x.Month.ToString("00", CultureInfo.InvariantCulture) + "." + x.Year.ToString(CultureInfo.InvariantCulture);
                }
                return null;
            }
            if (d is string s)
            {
                if (MonthMath.IsMMYYYY(s)) return s;
                var m1 = SlashDate.Match(s);
                if (m1.Success) return ((int)MonthMath.ParseInt(m1.Groups[1].Value)).ToString(CultureInfo.InvariantCulture).PadLeft(2, '0') + "." + m1.Groups[3].Value;
                var m2 = IsoDate.Match(s);
                if (m2.Success) return m2.Groups[2].Value + "." + m2.Groups[1].Value;
            }
            return null;
        }

        /// <summary>Object.keys(row) order: array-index keys ascending, then the others in insertion order.</summary>
        public static IEnumerable<string> JsKeys(SheetRow r)
        {
            var keys = r.Keys.ToList();
            if (!keys.Any(JsObjectMap<int>.IsArrayIndex)) return keys;
            var idx = keys.Where(JsObjectMap<int>.IsArrayIndex).OrderBy(k => ulong.Parse(k, CultureInfo.InvariantCulture)).ToList();
            idx.AddRange(keys.Where(k => !JsObjectMap<int>.IsArrayIndex(k)));
            return idx;
        }

        /// <summary><c>arr.slice(-n)</c> with JS ToIntegerOrInfinity semantics (n = 0 → the whole array).</summary>
        public static List<T> SliceLast<T>(List<T> arr, double n)
        {
            var rs = double.IsNaN(n) ? 0 : Math.Truncate(-n);
            int start = rs < 0 ? (int)Math.Max(arr.Count + rs, 0) : (int)Math.Min(rs, arr.Count);
            return arr.Skip(start).ToList();
        }

        /// <summary>JS <c>String.prototype.replace('.0','')</c> (first occurrence only).</summary>
        public static string ReplaceFirstDot0(string s)
        {
            var i = s.IndexOf(".0", StringComparison.Ordinal);
            return i < 0 ? s : s[..i] + s[(i + 2)..];
        }

        /// <summary>JS <c>String(a).localeCompare(b)</c> approximation used for sorting names / months (ICU root collation).</summary>
        public static int LocaleCompare(string a, string b) => string.Compare(a, b, CultureInfo.InvariantCulture, CompareOptions.None);

        /// <summary>JS default sort of strings (UTF-16 code units).</summary>
        public static int Ordinal(string a, string b) => string.CompareOrdinal(a, b);

        /// <summary>Stable JS sort with a comparator.</summary>
        public static List<T> Sorted<T>(IEnumerable<T> src, Func<T, T, double> cmp)
        {
            var l = src.ToList();
            JsSort.StableSort(l, cmp);
            return l;
        }
    }
}
