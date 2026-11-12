using System.Text.RegularExpressions;
using DecisionEngine.Mra;
using DecisionEngine.Results.Mra;

namespace Application.Services.V1.Sap
{
    /// <summary>
    /// v8: links each MRA to the tool's own SAP /decisions check — SAP does not send the decision back (mra-events is
    /// unchanged). The only link is the MRA number: SAP stores it as document_no against the request_id of the checked
    /// PO with POST /documents. No document with this MRA number → no decision ("Not found"). When the checked PO had
    /// several items, the item of the MRA's Generic Article + colour + delivery month is used (else same article and
    /// colour, else same qty, else the largest).
    /// </summary>
    public static class MraDecisionLinker
    {
        public const string ByMraNumber = "MRA number (/documents)";

        public static string Key(string? ga, string? colour, string? mon) =>
            MraNorm.Ga(ga) + "|" + (colour ?? "").Trim().ToUpperInvariant() + "|" + (mon ?? "").Trim();

        private static string NoZeros(string? s) { var t = (s ?? "").Trim().TrimStart('0'); return t.Length == 0 ? "0" : t; }

        /// <summary>Saved decision items by the document number (MRA number) SAP stored for them.</summary>
        public static Dictionary<string, List<MraToolDecision>> Index(IEnumerable<MraToolDecision> decisions)
        {
            var idx = new Dictionary<string, List<MraToolDecision>>(StringComparer.Ordinal);
            foreach (var d in decisions)
            {
                if (string.IsNullOrWhiteSpace(d.DocumentNo)) continue;
                var n = NoZeros(d.DocumentNo);
                if (!idx.TryGetValue(n, out var l)) idx[n] = l = new List<MraToolDecision>();
                l.Add(d);
            }
            return idx;
        }

        /// <summary>The decision of MRA <paramref name="mno"/>, or null when SAP stored no document with this number.</summary>
        public static MraToolDecision? Link(MraState m, string mno, string mon, Dictionary<string, List<MraToolDecision>> idx)
        {
            if (string.IsNullOrWhiteSpace(mno) || !idx.TryGetValue(NoZeros(mno), out var docs) || docs.Count == 0) return null;
            var latest = docs.OrderByDescending(c => c.DecidedAt).First().DecisionId;
            return ForDecision(docs.Where(c => c.DecisionId == latest).ToList(), m, mon);
        }

        /// <summary>
        /// The item of one decision to show for an MRA: the item of the MRA's article · colour · month when the PO had several,
        /// else same article and colour, else same qty, else the largest (also without an MRA: the largest). Null when none.
        /// </summary>
        public static MraToolDecision? ForDecision(IReadOnlyList<MraToolDecision> items, MraState? m, string? mon)
        {
            if (items.Count == 0) return null;
            MraToolDecision? item = null;
            if (m != null)
            {
                var key = Key(m.Ga, m.Colour, mon);
                item = items.FirstOrDefault(c => Key(c.Gm, c.Colour, c.DeliveryMonth) == key)
                    ?? items.FirstOrDefault(c => Key(c.Gm, c.Colour, "") == Key(m.Ga, m.Colour, ""))
                    ?? items.FirstOrDefault(c => Math.Abs(c.OrderQty - m.Qty) < 0.5);
            }
            item ??= items.OrderByDescending(c => c.OrderQty).First();
            return With(item, string.IsNullOrEmpty(item.DocumentNo) ? "verdict link (MRA number not sent to /documents yet)" : ByMraNumber);
        }

        private static MraToolDecision With(MraToolDecision d, string by) => new()
        {
            ItemId = d.ItemId, DecisionId = d.DecisionId, RequestId = d.RequestId, PoNumber = d.PoNumber, DecidedAt = d.DecidedAt, Mode = d.Mode,
            Decision = d.Decision, Verdict = d.Verdict, Reason = d.Reason, MaxPermissible = d.MaxPermissible, OrderQty = d.OrderQty,
            // the PO verdict page (view_url of new decisions is the MRA page itself)
            ViewUrl = MraVerdictLinks.PoVerdictUrl(d.ViewUrl), HasWorking = d.HasWorking, DocumentNo = d.DocumentNo, MatchedBy = by,
            Gm = d.Gm, Colour = d.Colour, DeliveryMonth = d.DeliveryMonth, OverallVerdict = d.OverallVerdict, PoItem = d.PoItem, ViewExpiresAt = d.ViewExpiresAt
        };
    }
}
