using DecisionEngine.Common;

namespace DecisionEngine.Grc
{
    /// <summary>
    /// Week / month period ("WW.YYYY" ISO week or legacy "MM.YYYY" month) → calendar date math used by the
    /// WOF back-calculation. Dates are UTC epoch milliseconds (double, NaN for an invalid date) exactly
    /// like the JS Date values they replace.
    /// </summary>
    public static class GrcPeriodMath
    {
        public const double DayMs = 24 * 60 * 60 * 1000d;
        public const double WeekMs = 7 * DayMs;

        /// <summary>JS Date.UTC(year, monthIndex, day) (month / day overflow roll over; years 0-99 → 1900+).</summary>
        public static double DateUtc(double year, double monthIndex, double day)
        {
            if (!double.IsFinite(year) || !double.IsFinite(monthIndex) || !double.IsFinite(day)) return double.NaN;
            var y = Math.Truncate(year);
            if (y >= 0 && y <= 99) y += 1900;
            var m = Math.Truncate(monthIndex);
            y += Math.Floor(m / 12);
            m = ((m % 12) + 12) % 12;
            return (DaysFromCivil((long)y, (int)m + 1, 1) + Math.Truncate(day) - 1) * DayMs;
        }

        // Howard Hinnant's days_from_civil (proleptic Gregorian, 1970-01-01 = 0).
        private static long DaysFromCivil(long y, int m, int d)
        {
            y -= m <= 2 ? 1 : 0;
            var era = (y >= 0 ? y : y - 399) / 400;
            var yoe = y - era * 400;
            var doy = (153 * (m + (m > 2 ? -3 : 9)) + 2) / 5 + d - 1;
            var doe = yoe * 365 + yoe / 4 - yoe / 100 + doy;
            return era * 146097 + doe - 719468;
        }

        private static (long Y, int M, int D) CivilFromDays(long z)
        {
            z += 719468;
            var era = (z >= 0 ? z : z - 146096) / 146097;
            var doe = z - era * 146097;
            var yoe = (doe - doe / 1460 + doe / 36524 - doe / 146096) / 365;
            var y = yoe + era * 400;
            var doy = doe - (365 * yoe + yoe / 4 - yoe / 100);
            var mp = (5 * doy + 2) / 153;
            var d = (int)(doy - (153 * mp + 2) / 5 + 1);
            var m = (int)(mp < 10 ? mp + 3 : mp - 9);
            return (m <= 2 ? y + 1 : y, m, d);
        }

        /// <summary>getUTCDay(): 0 = Sunday … 6 = Saturday.</summary>
        public static int UtcDay(double ms) => (int)((((long)Math.Floor(ms / DayMs) + 4) % 7 + 7) % 7);

        /// <summary>
        /// Port of index.html <c>_isoWeekToDate(week, year)</c> (line 2754): Monday of ISO week
        /// <paramref name="week"/> (week 1 = the week containing 4 January).
        /// </summary>
        public static double IsoWeekToDate(double week, double year)
        {
            var jan4 = DateUtc(year, 0, 4);
            if (double.IsNaN(jan4) || !double.IsFinite(week)) return double.NaN;
            var jan4Day = UtcDay(jan4);
            if (jan4Day == 0) jan4Day = 7; // Mon=1..Sun=7
            var week1Monday = jan4 + (1 - jan4Day) * DayMs;
            return week1Monday + Math.Truncate((week - 1) * 7) * DayMs;
        }

        /// <summary>Splits "NN.YYYY" with JS <c>split('.').map(Number)</c>.</summary>
        public static (double N, double Y) SplitPeriod(string period)
        {
            var parts = period.Split('.');
            var n = Js.Number(parts[0]);
            var y = parts.Length > 1 ? Js.Number(parts[1]) : double.NaN;
            return (n, y);
        }

        /// <summary>Calendar date a period stands for: week → its Monday, month → the 15th (mid-month).</summary>
        public static double PeriodDate(string period, string periodType)
        {
            var (n, y) = SplitPeriod(period);
            return periodType == "week" ? IsoWeekToDate(n, y) : DateUtc(y, n - 1, 15);
        }

        /// <summary>
        /// Port of <c>_grcAsOfDate()</c> (line 2777): the "Assess as of" reference date — the saved period
        /// (WW.YYYY / MM.YYYY per the GRC file's period type), or the current date when none is set.
        /// </summary>
        public static double AsOfDate(string? asOfPeriod, string? grcPeriodType, double nowMs)
        {
            if (string.IsNullOrEmpty(asOfPeriod)) return nowMs;
            var periodType = string.IsNullOrEmpty(grcPeriodType) ? "month" : grcPeriodType;
            return PeriodDate(asOfPeriod, periodType);
        }

        /// <summary>Port of <c>_weeksElapsedSincePeriod(period, periodType)</c> (line 2784).</summary>
        public static double WeeksElapsedSincePeriod(string period, string periodType, double asOfMs) =>
            (asOfMs - PeriodDate(period, periodType)) / WeekMs;

        /// <summary>
        /// Port of <c>_weekPeriodToMonthLabel(period)</c> (line 2801): the calendar month ("MM.YYYY") owning
        /// the majority of the Monday–Sunday week's 7 days.
        /// </summary>
        public static string WeekPeriodToMonthLabel(string period)
        {
            var (wk, yr) = SplitPeriod(period);
            var monday = IsoWeekToDate(wk, yr);
            if (double.IsNaN(monday)) return "NaN.NaN";
            var keys = new List<string>();
            var counts = new Dictionary<string, int>(StringComparer.Ordinal);
            var day0 = (long)Math.Floor(monday / DayMs);
            for (var i = 0; i < 7; i++)
            {
                var (y, m, _) = CivilFromDays(day0 + i);
                var key = m.ToString("00") + "." + y.ToString(System.Globalization.CultureInfo.InvariantCulture);
                if (!counts.ContainsKey(key)) { counts[key] = 0; keys.Add(key); }
                counts[key]++;
            }
            return GrcJs.StableSort(keys, (a, b) => counts[b] - counts[a])[0];
        }

        /// <summary>UTC "yyyy-MM-dd" of a ms value (JS <c>toISOString().slice(0,10)</c>).</summary>
        public static string IsoDate(double ms)
        {
            var (y, m, d) = CivilFromDays((long)Math.Floor(ms / DayMs));
            return y.ToString("0000") + "-" + m.ToString("00") + "-" + d.ToString("00");
        }
    }
}
