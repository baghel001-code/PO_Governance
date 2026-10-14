using System.Globalization;
using System.Text.RegularExpressions;
using Application.Services.V1.Ingest.SheetJs;

namespace Application.Services.V1.Ingest
{
    /// <summary>
    /// Month / day-column helpers of index.html (lines ~1745-1790) that read the headers of the
    /// uploaded files: isMMYYYY, toYM, isDayCol, dayColNum, dateToMMYYYY, addMonths, daysInMonth, mmyyyy.
    /// </summary>
    public static class IngestDateHelpers
    {
        private static readonly Regex MmYyyy = new(@"^([0-9]{2})\.([0-9]{4})$", RegexOptions.CultureInvariant);
        private static readonly Regex DayCol = new(@"^D(?:AY)?[" + JsNum.WsClass + @"]*([0-9]{1,3})$", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
        private static readonly Regex Us = new(@"^([0-9]{1,2})\/([0-9]{1,2})\/([0-9]{4})$", RegexOptions.CultureInvariant);
        private static readonly Regex Iso = new(@"^([0-9]{4})-([0-9]{2})-([0-9]{2})", RegexOptions.CultureInvariant);

        /// <summary>isMMYYYY: header like "06.2026".</summary>
        public static bool IsMMYYYY(string k) => MmYyyy.IsMatch(JsNum.Trim(k));

        /// <summary>toYM: "06.2026" → "2026-06" (null otherwise).</summary>
        public static string? ToYM(string k)
        {
            var m = MmYyyy.Match(JsNum.Trim(k));
            return m.Success ? m.Groups[2].Value + "-" + m.Groups[1].Value : null;
        }

        /// <summary>isDayCol: "Day 12", "D12", "DAY12" (case-insensitive).</summary>
        public static bool IsDayCol(object? k) => DayCol.IsMatch(JsNum.Trim(ToStr(k)));

        /// <summary>dayColNum: the day index of a Day column, null otherwise.</summary>
        public static int? DayColNum(object? k)
        {
            var m = DayCol.Match(JsNum.Trim(ToStr(k)));
            return m.Success ? int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture) : null;
        }

        /// <summary>
        /// dateToMMYYYY: a DateTime, an Excel serial (&gt; 40000), "MM.YYYY", "6/1/2026" (M/D/YYYY) or
        /// "2026-06-01" → "MM.YYYY"; null otherwise.
        /// </summary>
        public static string? DateToMMYYYY(object? d)
        {
            // if(!d && d!==0) return null;
            if (d == null || (d is string s0 && s0.Length == 0) || (d is bool b && !b)) return null;
            if (d is DateTime dt) return dt.Month.ToString("00", CultureInfo.InvariantCulture) + "." + dt.Year.ToString(CultureInfo.InvariantCulture);
            if (d is double n)
            {
                if (n > 40000)
                {
                    var days = Math.Floor(n);
                    var date = new DateTime(1899, 12, 30).AddDays(days).AddMilliseconds(Math.Round((n - days) * 86400000));
                    return date.Month.ToString("00", CultureInfo.InvariantCulture) + "." + date.Year.ToString(CultureInfo.InvariantCulture);
                }
                return null;
            }
            if (d is string s)
            {
                if (IsMMYYYY(s)) return s;
                var m1 = Us.Match(s);
                if (m1.Success) return int.Parse(m1.Groups[1].Value, CultureInfo.InvariantCulture).ToString("00", CultureInfo.InvariantCulture) + "." + m1.Groups[3].Value;
                var m2 = Iso.Match(s);
                if (m2.Success) return m2.Groups[2].Value + "." + m2.Groups[1].Value;
            }
            return null;
        }

        /// <summary>addMonths(y, m, n).</summary>
        public static (int Y, int M) AddMonths(int y, int m, int n)
        {
            int mo = m + n, yr = y;
            while (mo > 12) { mo -= 12; yr++; }
            while (mo < 1) { mo += 12; yr--; }
            return (yr, mo);
        }

        /// <summary>daysInMonth(y, m) (m 1-12).</summary>
        public static int DaysInMonth(int y, int m) => DateTime.DaysInMonth(y, m);

        /// <summary>mmyyyy(y, m): "06.2026".</summary>
        public static string MmYyyyOf(int y, int m) => m.ToString("00", CultureInfo.InvariantCulture) + "." + y.ToString(CultureInfo.InvariantCulture);

        private static string ToStr(object? k) => k switch { null => "null", string s => s, double d => JsNum.ToStr(d), _ => k.ToString() ?? "" };
    }
}
