using System.Globalization;
using System.Text.RegularExpressions;

namespace Application.Services.V1.Engines.Common
{
    /// <summary>
    /// Month arithmetic shared by every engine — ports of the index.html helpers (lines ~1756–1770, 1890)
    /// plus the small JS parsing quirks they rely on (<c>parseInt</c> of "MM.YYYY" parts).
    /// </summary>
    public static class MonthMath
    {
        private static readonly Regex MmYyyy = new("^[0-9]{2}\\.[0-9]{4}$", RegexOptions.Compiled);
        private static readonly Regex MmYyyyParts = new("^([0-9]{2})\\.([0-9]{4})$", RegexOptions.Compiled);
        private static readonly Regex IntPrefix = new("^[+-]?[0-9]+", RegexOptions.Compiled);

        /// <summary>Port of index.html <c>addMonths(y,m,n)</c> (line 1756).</summary>
        public static (int Y, int M) AddMonths(int y, int m, int n)
        {
            int mo = m + n, yr = y;
            while (mo > 12) { mo -= 12; yr++; }
            while (mo < 1) { mo += 12; yr--; }
            return (yr, mo);
        }

        /// <summary>Port of index.html <c>daysInMonth(y,m)</c> (line 1757) — <c>new Date(y,m,0).getDate()</c>.</summary>
        public static int DaysInMonth(int y, int m)
        {
            // new Date(y, m, 0) = last day of month m (1-based), with JS month overflow semantics.
            var (yy, mm) = AddMonths(y, m, 0);
            return DateTime.DaysInMonth(yy, mm);
        }

        /// <summary>Port of index.html <c>mmyyyy(y,m)</c> (line 1759): "07.2026".</summary>
        public static string Mmyyyy(int y, int m) => m.ToString(CultureInfo.InvariantCulture).PadLeft(2, '0') + "." + y.ToString(CultureInfo.InvariantCulture);

        /// <summary>Port of index.html <c>ymKey(y,m)</c> (line 1890): "2026-07".</summary>
        public static string YmKey(int y, int m) => y.ToString(CultureInfo.InvariantCulture) + "-" + m.ToString(CultureInfo.InvariantCulture).PadLeft(2, '0');

        /// <summary>Port of index.html <c>monLabel(y,m)</c> (line 1758): <c>toLocaleString('en-IN',{month:'short',year:'numeric'})</c> → "Oct 2026".</summary>
        public static string MonLabel(int y, int m)
        {
            var (yy, mm) = AddMonths(y, m, 0);
            return CultureInfo.InvariantCulture.DateTimeFormat.GetAbbreviatedMonthName(mm) + " " + yy.ToString(CultureInfo.InvariantCulture);
        }

        /// <summary>Port of index.html <c>isMMYYYY(k)</c> (line 1712): /^\d{2}\.\d{4}$/ on the trimmed key.</summary>
        public static bool IsMMYYYY(string k) => MmYyyy.IsMatch(k.Trim());

        /// <summary>The same test without trimming (<c>/^\d{2}\.\d{4}$/.test(h)</c> as used by <c>_buildMonths</c>).</summary>
        public static bool IsMMYYYYExact(string k) => MmYyyy.IsMatch(k);

        /// <summary>
        /// <c>m.match(/^(\d{2})\.(\d{4})$/)</c> → (month, year); null when it does not match.
        /// </summary>
        public static (int M, int Y)? ParseMmyyyy(string? k)
        {
            if (k == null) return null;
            var mt = MmYyyyParts.Match(k);
            if (!mt.Success) return null;
            return (int.Parse(mt.Groups[1].Value, CultureInfo.InvariantCulture), int.Parse(mt.Groups[2].Value, CultureInfo.InvariantCulture));
        }

        /// <summary>JS <c>parseInt(s)</c> (radix 10): leading integer, NaN when there is none.</summary>
        public static double ParseInt(string? s)
        {
            if (s == null) return double.NaN;
            var t = s.TrimStart(' ', '\t', '\n', '\r', '\v', '\f', ' ', '﻿');
            var mt = IntPrefix.Match(t);
            if (!mt.Success) return double.NaN;
            return double.Parse(mt.Value, CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// <c>var p=m.split('.'); return parseInt(p[1])*12+parseInt(p[0]);</c> — the month ordinal used by
        /// <c>_cumPO</c>, <c>_f2ByCol</c>, <c>_buildMonths</c>' sort and the month-bar sort. NaN for junk.
        /// </summary>
        public static double MonthOrdinal(string m)
        {
            var p = m.Split('.');
            var yr = p.Length > 1 ? ParseInt(p[1]) : double.NaN;
            return yr * 12 + ParseInt(p[0]);
        }

        /// <summary>Parses "yyyy-MM-dd" (the server's today) into (y, m, d).</summary>
        public static (int Y, int M, int D) ParseIsoDate(string today)
        {
            var d = DateTime.ParseExact(today[..10], "yyyy-MM-dd", CultureInfo.InvariantCulture);
            return (d.Year, d.Month, d.Day);
        }

        /// <summary>
        /// JS comparator <c>(a,b)=>ordinal(a)-ordinal(b)</c> as a stable sort (Array.prototype.sort is stable).
        /// A NaN comparator result counts as 0 (keeps order), as in V8.
        /// </summary>
        public static List<string> SortByOrdinal(IEnumerable<string> months)
        {
            var list = months.ToList();
            JsSort.StableSort(list, (a, b) => MonthOrdinal(a) - MonthOrdinal(b));
            return list;
        }
    }

    /// <summary>
    /// Stable sort with a JS-style numeric comparator (<c>Array.prototype.sort(fn)</c>): negative → a first,
    /// positive → b first, 0 or NaN → keep the original relative order.
    /// </summary>
    public static class JsSort
    {
        public static void StableSort<T>(List<T> list, Func<T, T, double> cmp)
        {
            // Merge sort (stable), comparator semantics as V8: only "> 0" moves b before a.
            if (list.Count < 2) return;
            var arr = list.ToArray();
            var tmp = new T[arr.Length];
            MergeSort(arr, tmp, 0, arr.Length, cmp);
            list.Clear();
            list.AddRange(arr);
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
                // take the right element only when cmp(left, right) > 0 (NaN / 0 keep order)
                if (cmp(a[i], a[j]) > 0) tmp[k++] = a[j++];
                else tmp[k++] = a[i++];
            }
            while (i < mid) tmp[k++] = a[i++];
            while (j < hi) tmp[k++] = a[j++];
            Array.Copy(tmp, lo, a, lo, hi - lo);
        }
    }
}
