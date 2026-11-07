using System.Globalization;
using System.Text.RegularExpressions;
using DecisionEngine.Mra;
using DecisionEngine.Results.Mra;

namespace Application.Services.V1.Sap
{
    /// <summary>
    /// v8: links each MRA to the tool's own SAP /decisions check it was raised from — SAP does not send the decision back
    /// (mra-events is unchanged). Saved decision items are matched on Generic Article (last 10 digits) + colour + delivery
    /// month (MM.yyyy). Among them: a check of one of the MRA's POs first; else the latest check made on or before the day
    /// the MRA was created (equal quantity preferred); a check made after the MRA is never used.
    /// </summary>
    public static class MraDecisionLinker
    {
        public static string Key(string? ga, string? colour, string? mon) =>
            MraNorm.Ga(ga) + "|" + (colour ?? "").Trim().ToUpperInvariant() + "|" + (mon ?? "").Trim();

        private static string NoZeros(string? s) { var t = (s ?? "").Trim().TrimStart('0'); return t.Length == 0 ? "0" : t; }

        public static Dictionary<string, List<MraToolDecision>> Index(IEnumerable<MraToolDecision> decisions)
        {
            var idx = new Dictionary<string, List<MraToolDecision>>(StringComparer.Ordinal);
            foreach (var d in decisions)
            {
                var k = Key(d.Gm, d.Colour, d.DeliveryMonth);
                if (!idx.TryGetValue(k, out var l)) idx[k] = l = new List<MraToolDecision>();
                l.Add(d);
            }
            return idx;
        }

        /// <summary>The decision for one MRA, or null.</summary>
        public static MraToolDecision? Link(MraState m, string mon, Dictionary<string, List<MraToolDecision>> idx)
        {
            if (!idx.TryGetValue(Key(m.Ga, m.Colour, mon), out var cands) || cands.Count == 0) return null;
            var pos = new HashSet<string>(StringComparer.Ordinal);
            foreach (var p in (m.PoNo ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)) pos.Add(NoZeros(p));
            foreach (var p in m.Pos) if (!string.IsNullOrEmpty(p.Po)) pos.Add(NoZeros(p.Po));
            var byPo = cands.Where(c => !string.IsNullOrEmpty(c.PoNumber) && pos.Contains(NoZeros(c.PoNumber)))
                            .OrderByDescending(c => c.DecidedAt).FirstOrDefault();
            if (byPo != null) return With(byPo, "PO number");

            // the MRA is raised after the check: on or before the day it was created (file), else before its first live change
            DateTime? until = DateTime.TryParseExact(m.CreatedOn ?? "", "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var c0)
                ? c0.Date.AddDays(1)
                : m.History.Count > 0 ? m.History[0].At.LocalDateTime : null;
            var ok = cands.Where(c => until == null || c.DecidedAt.LocalDateTime < until.Value).ToList();
            if (ok.Count == 0) return null;
            var best = ok.OrderByDescending(c => Math.Abs(c.OrderQty - m.Qty) < 0.5 ? 1 : 0).ThenByDescending(c => c.DecidedAt).First();
            return With(best, "article · colour · month");
        }

        private static MraToolDecision With(MraToolDecision d, string by) => new()
        {
            ItemId = d.ItemId, DecisionId = d.DecisionId, RequestId = d.RequestId, PoNumber = d.PoNumber, DecidedAt = d.DecidedAt, Mode = d.Mode,
            Decision = d.Decision, Verdict = d.Verdict, Reason = d.Reason, MaxPermissible = d.MaxPermissible, OrderQty = d.OrderQty,
            ViewUrl = d.ViewUrl is { } u && Regex.IsMatch(u, @"^https?://") ? u : null, HasWorking = d.HasWorking, MatchedBy = by,
            Gm = d.Gm, Colour = d.Colour, DeliveryMonth = d.DeliveryMonth
        };
    }
}
