using Domain.Entities.Common;
using Domain.Entities.Engine;

namespace Application.Services.V1.Engines.Common
{
    /// <summary>Result of <c>_prevMonthCarry</c>: {qty, cutoff, prevKey, currKey}.</summary>
    public sealed record CarryOver(double Qty, double Cutoff, string? PrevKey, string CurrKey);

    /// <summary>
    /// Previous-month pending-PO carry-over (Single Concept MRP only) — ports of index.html
    /// <c>_prevMonthCarry</c> (line 1784), <c>_prevMonthCarryNote</c> (line 1795) and
    /// <c>_ePOWithCarry</c> (line 1803). Non-mutating: the carry is applied only when a month is read.
    /// </summary>
    public static class PrevMonthCarry
    {
        /// <summary>
        /// Port of <c>_prevMonthCarry(existingPOMap, currY, currM, todayDate)</c>. Cut-off is
        /// <c>S.params.prevMonthPendingCutoffDay</c> (0 is respected — the JS uses <c>!=null</c>, not <c>||</c>).
        /// </summary>
        public static CarryOver Compute(Func<string, double>? existingPO, int currY, int currM, int todayDate, EngineParams p)
        {
            var cutoff = p.PrevMonthPendingCutoffDay;
            var currKey = MonthMath.Mmyyyy(currY, currM);
            if (todayDate > cutoff) return new CarryOver(0, cutoff, null, currKey);
            var (py, pm) = MonthMath.AddMonths(currY, currM, -1);
            var prevKey = MonthMath.Mmyyyy(py, pm);
            var qty = existingPO == null ? 0 : JsOps.Or(existingPO(prevKey), 0);
            return new CarryOver(qty, cutoff, prevKey, currKey);
        }

        /// <summary>Overload for a month → qty map (null = JS null / undefined map).</summary>
        public static CarryOver Compute(JsObjectMap<double>? existingPO, int currY, int currM, int todayDate, EngineParams p) =>
            Compute(existingPO == null ? null : k => existingPO.Get(k), currY, currM, todayDate, p);

        /// <summary>Port of <c>_prevMonthCarryNote(carry)</c> — '' when nothing was carried.</summary>
        public static string Note(CarryOver? carry)
        {
            if (carry == null || !(carry.Qty > 0)) return "";
            return " [Includes " + Js.Fmt(carry.Qty) + " units of previous month's (" + carry.PrevKey +
                   ") pending PO, carried over — Day-of-month cut-off = " + Js.NumberToString(carry.Cutoff) + ".]";
        }

        /// <summary>Port of <c>_ePOWithCarry(existingPOMap, mkey, carry)</c>.</summary>
        public static double EPOWithCarry(JsObjectMap<double>? existingPO, string mkey, CarryOver? carry)
        {
            var b = existingPO == null ? 0 : JsOps.Or(existingPO.Get(mkey), 0);
            return carry != null && mkey == carry.CurrKey ? b + carry.Qty : b;
        }
    }
}
