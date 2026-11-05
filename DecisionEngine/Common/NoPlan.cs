namespace DecisionEngine.Common
{
    /// <summary>
    /// v1.1.6 D1 "No Plan Available" (single concept) — ports of the v1.1.6 page helpers <c>_windowMonths</c>,
    /// <c>_noPlanReason</c> and <c>_paNoPlan</c> (legacy/v1.1.6/patches/20_noplan_nosale.py). There is no plan when
    /// the F4 plan of the Brand × MC × MRP × Flag adds up to 0 (or less) over the requirement window: D days from the
    /// 1st of the month after delivery. New PO request → Hold (checked first); PO Assessment → "No Plan – Review".
    /// </summary>
    public static class NoPlan
    {
        /// <summary>
        /// Port of <c>_windowMonths(month, days)</c>: the "MM.YYYY" keys of the requirement window — the month after
        /// <paramref name="month"/> onwards until <paramref name="days"/> (whole days, <c>parseInt</c>) are covered,
        /// at most 24 months. Empty for an invalid month.
        /// </summary>
        public static List<string> WindowMonths(string? month, double days)
        {
            var outList = new List<string>();
            var p = MonthMath.ParseMmyyyy(month);
            if (p == null || p.Value.M < 1 || p.Value.M > 12) return outList;
            int y = p.Value.Y, m = p.Value.M + 1;
            if (m > 12) { m = 1; y++; }
            var left = double.IsFinite(days) ? Math.Truncate(days) : 0;
            while (left > 0 && outList.Count < 24)
            {
                outList.Add(MonthMath.Mmyyyy(y, m));
                left -= MonthMath.DaysInMonth(y, m);
                m++;
                if (m > 12) { m = 1; y++; }
            }
            return outList;
        }

        /// <summary>Port of <c>_noPlanReason(info, ks)</c>.</summary>
        public static string Reason(string? brand, string? mcCd, string? mrp, string? flag, IReadOnlyList<string> ks)
        {
            static string Lab(string k) => MonthMath.MonLabel((int)MonthMath.ParseInt(k[3..]), (int)MonthMath.ParseInt(k[..2]));
            var span = ks.Count > 0 ? Lab(ks[0]) + (ks.Count > 1 ? " – " + Lab(ks[^1]) : "") : "";
            return "No Plan Available: there is no F4 plan for " + (brand ?? "") + " · MC " + (mcCd ?? "") + " · ₹" + (mrp ?? "") + " · " + (flag ?? "") +
                   " in the requirement window (" + span + "). Nothing else was checked. Ask the planning team to add the plan.";
        }

        /// <summary>
        /// Port of <c>_paNoPlan(forecastByMon, dm, noShift)</c>: the window months when there is no plan, else null.
        /// Without <paramref name="noShift"/> (PO Assessment) a PO two or more months late is checked from last month.
        /// "Now" is the server date <paramref name="today"/> ("yyyy-MM-dd"); <paramref name="d"/> = <c>parseFloat(S.params.D)||75</c>.
        /// </summary>
        public static List<string>? Check(Func<string, double> forecast, string? dm, bool noShift, string today, double d)
        {
            var p = MonthMath.ParseMmyyyy(dm);
            var (ny, nm, _) = MonthMath.ParseIsoDate(today);
            if (!noShift && p != null && p.Value.Y * 12 + p.Value.M < ny * 12 + nm - 1)
            {
                int py = ny, pm = nm - 1;
                if (pm < 1) { pm = 12; py--; }
                dm = MonthMath.Mmyyyy(py, pm);
            }
            var ks = WindowMonths(dm, JsOps.Or(d, 75));
            if (ks.Count == 0) return null;
            double tot = 0;
            foreach (var k in ks) { var v = forecast(k); tot += double.IsNaN(v) ? 0 : v; }
            return tot > 0 ? null : ks;
        }
    }
}
