using System.Text.RegularExpressions;
using DecisionEngine.Common;
using DecisionEngine.Model;

namespace DecisionEngine.Grc
{
    /// <summary>
    /// Small JS-semantics helpers private to the GRC back-test port (kept here, not in Domain, to avoid
    /// clashing with other ports).
    /// </summary>
    public static class GrcJs
    {
        private static readonly Regex IntPrefix = new(@"^[+-]?\d+", RegexOptions.Compiled);

        /// <summary>JS parseInt(s, 10): leading whitespace, optional sign, digits; NaN when none.</summary>
        public static double ParseInt(string? s)
        {
            if (s == null) return double.NaN;
            var m = IntPrefix.Match(s.TrimStart());
            if (!m.Success) return double.NaN;
            return double.TryParse(m.Value, System.Globalization.NumberStyles.AllowLeadingSign,
                System.Globalization.CultureInfo.InvariantCulture, out var d) ? d : double.NaN;
        }

        /// <summary>JS <c>r[k1]||r[k2]||…||''</c> on exact headers: the first truthy raw value, else "".</summary>
        public static object Or(SheetRow r, params string[] keys)
        {
            foreach (var k in keys)
            {
                var v = r[k];
                if (Js.Truthy(v)) return v!;
            }
            return "";
        }

        /// <summary>
        /// The period comparator every GRC list uses:
        /// <c>(parseInt(ay)*100+parseInt(an)) - (parseInt(by)*100+parseInt(bn))</c> on "NN.YYYY".
        /// </summary>
        public static double ComparePeriods(string a, string b) => PeriodKey(a) - PeriodKey(b);

        public static double PeriodKey(string p)
        {
            var parts = p.Split('.');
            var n = ParseInt(parts[0]);
            var y = parts.Length > 1 ? ParseInt(parts[1]) : double.NaN;
            return y * 100 + n;
        }

        /// <summary>Stable sort with a JS comparator (NaN treated as "equal", like Array.prototype.sort).</summary>
        public static List<T> StableSort<T>(IEnumerable<T> items, Func<T, T, double> cmp)
        {
            var arr = items.ToArray();
            if (arr.Length < 2) return arr.ToList();
            var tmp = new T[arr.Length];
            MergeSort(arr, tmp, 0, arr.Length, cmp);
            return arr.ToList();
        }

        private static void MergeSort<T>(T[] a, T[] tmp, int lo, int hi, Func<T, T, double> cmp)
        {
            if (hi - lo < 2) return;
            var mid = (lo + hi) / 2;
            MergeSort(a, tmp, lo, mid, cmp);
            MergeSort(a, tmp, mid, hi, cmp);
            int i = lo, j = mid, k = lo;
            while (i < mid && j < hi)
            {
                var c = cmp(a[j], a[i]);
                if (c < 0) tmp[k++] = a[j++]; // strictly less → right first; equal / NaN keeps left first
                else tmp[k++] = a[i++];
            }
            while (i < mid) tmp[k++] = a[i++];
            while (j < hi) tmp[k++] = a[j++];
            Array.Copy(tmp, lo, a, lo, hi - lo);
        }

        /// <summary>Distinct values in first-seen order (JS <c>[...new Set(arr)]</c>).</summary>
        public static List<string> DistinctInOrder(IEnumerable<string> items)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var list = new List<string>();
            foreach (var s in items) if (seen.Add(s)) list.Add(s);
            return list;
        }

        /// <summary>Chronologically sorted distinct periods (the sort used by every period list).</summary>
        public static List<string> SortedPeriods(IEnumerable<string> periods) =>
            StableSort(DistinctInOrder(periods), ComparePeriods);

        /// <summary>JS Array.prototype.slice(start[, end]).</summary>
        public static List<T> Slice<T>(IReadOnlyList<T> list, int start, int? end = null)
        {
            var len = list.Count;
            var s = start < 0 ? Math.Max(len + start, 0) : Math.Min(start, len);
            var e = end == null ? len : end < 0 ? Math.Max(len + end.Value, 0) : Math.Min(end.Value, len);
            var r = new List<T>();
            for (var i = s; i < e; i++) r.Add(list[i]);
            return r;
        }

        /// <summary>JS default Array.prototype.sort() for strings (UTF-16 code-unit order).</summary>
        public static List<string> SortOrdinal(IEnumerable<string> items) => StableSort(items, (a, b) => string.CompareOrdinal(a, b));

        /// <summary>JS <c>a.localeCompare(b)</c> (ICU root collation).</summary>
        public static int LocaleCompare(string a, string b) =>
            string.Compare(a, b, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.CompareOptions.None);

        /// <summary><c>x || fallback</c> for a numeric parameter (0 / NaN → fallback).</summary>
        public static double OrDefault(double v, double fallback) => v == 0 || double.IsNaN(v) ? fallback : v;

        /// <summary>JS <c>Math.round(v).toLocaleString('en-IN')</c>.</summary>
        public static string Fmt(double v) => Js.Fmt(v);

        /// <summary>"₹x.xx Cr" — <c>₹${(v/1e7).toFixed(2)} Cr</c>.</summary>
        public static string Cr(double v) => "₹" + Js.ToFixed(v / 1e7, 2) + " Cr";
    }
}
