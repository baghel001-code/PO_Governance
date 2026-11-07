using System.Globalization;
using System.Text.RegularExpressions;
using DecisionEngine.Mra;
using DecisionEngine.Results.Mra;

namespace Application.Services.V1.Sap
{
    /// <summary>
    /// v8: links each MRA to the tool's own SAP /decisions check it was raised from — SAP does not send the decision back
    /// (mra-events is unchanged). In this order:
    /// 1. the MRA number = the final document (MRN) SAP stored against the decision with POST /documents (exact link);
    /// 2. a check of one of the MRA's POs (same Generic Article + colour + delivery month);
    /// 3. the latest check of the same Generic Article (last 10 digits) + colour + delivery month (MM.yyyy) made on or
    ///    before the day the MRA was created (equal quantity preferred); a check made after the MRA is never used.
    /// </summary>
    public static class MraDecisionLinker
    {
        /// <summary>Saved decision items by article · colour · month and by the document number (MRA number) stored for them.</summary>
        public sealed class Lookup
        {
            public Dictionary<string, List<MraToolDecision>> ByKey { get; } = new(StringComparer.Ordinal);
            public Dictionary<string, List<MraToolDecision>> ByDocument { get; } = new(StringComparer.Ordinal);
        }

        public static string Key(string? ga, string? colour, string? mon) =>
            MraNorm.Ga(ga) + "|" + (colour ?? "").Trim().ToUpperInvariant() + "|" + (mon ?? "").Trim();

        private static string NoZeros(string? s) { var t = (s ?? "").Trim().TrimStart('0'); return t.Length == 0 ? "0" : t; }

        public static Lookup Index(IEnumerable<MraToolDecision> decisions)
        {
            var idx = new Lookup();
            foreach (var d in decisions)
            {
                var k = Key(d.Gm, d.Colour, d.DeliveryMonth);
                if (!idx.ByKey.TryGetValue(k, out var l)) idx.ByKey[k] = l = new List<MraToolDecision>();
                l.Add(d);
                if (!string.IsNullOrWhiteSpace(d.DocumentNo))
                {
                    var n = NoZeros(d.DocumentNo);
                    if (!idx.ByDocument.TryGetValue(n, out var dl)) idx.ByDocument[n] = dl = new List<MraToolDecision>();
                    dl.Add(d);
                }
            }
            return idx;
        }

        /// <summary>The decision for one MRA, or null.</summary>
        public static MraToolDecision? Link(MraState m, string mno, string mon, Lookup idx)
        {
            // 1. SAP stored this MRA number against a decision (POST /documents): that decision, its item of this
            //    article · colour · month when the PO had several, else the item of the same qty, else the largest
            if (!string.IsNullOrWhiteSpace(mno) && idx.ByDocument.TryGetValue(NoZeros(mno), out var docs) && docs.Count > 0)
            {
                var key = Key(m.Ga, m.Colour, mon);
                var latest = docs.OrderByDescending(c => c.DecidedAt).First().DecisionId;
                var items = docs.Where(c => c.DecisionId == latest).ToList();
                var item = items.FirstOrDefault(c => Key(c.Gm, c.Colour, c.DeliveryMonth) == key)
                        ?? items.FirstOrDefault(c => Key(c.Gm, c.Colour, "") == Key(m.Ga, m.Colour, ""))
                        ?? items.OrderByDescending(c => Math.Abs(c.OrderQty - m.Qty) < 0.5 ? 1 : 0).ThenByDescending(c => c.OrderQty).First();
                return With(item, "MRA number (/documents)");
            }

            if (!idx.ByKey.TryGetValue(Key(m.Ga, m.Colour, mon), out var cands) || cands.Count == 0) return null;
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
            ViewUrl = d.ViewUrl is { } u && Regex.IsMatch(u, @"^https?://") ? u : null, HasWorking = d.HasWorking, DocumentNo = d.DocumentNo, MatchedBy = by,
            Gm = d.Gm, Colour = d.Colour, DeliveryMonth = d.DeliveryMonth
        };
    }
}
