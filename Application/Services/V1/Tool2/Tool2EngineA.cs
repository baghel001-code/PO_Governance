using System.Text.RegularExpressions;
using Application.Services.V1.Engines.Common;
using Domain.Entities.Common;
using Domain.Entities.Engine;

namespace Application.Services.V1.Tool2
{
    /// <summary>Raw result of runEngineA (before Tool 2 maps the decision).</summary>
    public sealed class EngineAResult
    {
        public string Decision { get; set; } = "";
        public string DecisionReason { get; set; } = "";
        public string? OptionType { get; set; }
        public string BsrLabel { get; set; } = "";
        public List<SheetRow> PeerPool { get; set; } = new();
        public List<SheetRow> CleanPool { get; set; } = new();
        public double TotalBsr { get; set; }
        public double PeerAvg { get; set; }
        public List<SheetRow> Siblings { get; set; } = new();
        public List<SheetRow> QualSibs { get; set; } = new();
        public List<SibRank> SibRanked { get; set; } = new();
        public double? Wof { get; set; }
        public double? Pdstr { get; set; }
        public string? Quartile { get; set; }
        public double? Percentile { get; set; }
        public double? GapPct { get; set; }
        public bool? InBorderZone { get; set; }
        public double? BorderTolUsed { get; set; }
        public SheetRow? RepSibling { get; set; }
    }

    /// <summary>
    /// Port of index.html <c>runEngineA</c> (line 4270) and <c>absBlock</c> (line 4257) — the
    /// Multi Concept MRP peer-ranking decision (True Repeat / BOBs) that Tool 2 maps to Retain /
    /// Cancel / Watch List. Tool 1's engine is the same JS function; this port lives with Tool 2
    /// until Tool 1's port is merged (both must stay identical to the JS).
    /// </summary>
    public static class Tool2EngineA
    {
        private static readonly Regex SeasonStrip = new("[-_]", RegexOptions.Compiled);
        private static readonly Regex Ws = new(@"\s+", RegexOptions.Compiled);

        private sealed record AbsBlockResult(bool Overridden, string Decision = "", string DecisionReason = "");

        /// <summary>Port of <c>absBlock(decision, pdstr, wof, label)</c>.</summary>
        private static AbsBlockResult AbsBlock(string decision, double pdstr, double wof, string label)
        {
            const double PDSTR_THRESHOLD = 0.01;
            const double WOF_MIN = 4;
            if ((decision == "Allow" || decision == "Auto Allow") && wof >= WOF_MIN && pdstr < PDSTR_THRESHOLD)
                return new AbsBlockResult(true, "Block",
                    "Absolute Block override: " + label + " has AVG PERDAY SELL THRU% = " + Js.ToFixed(pdstr * 100, 1) +
                    "% < 1% threshold with " + Js.NumberToString(wof) + "w on floor (≥ 4w gate). Confirmed very slow seller — do not raise PO regardless of peer ranking.");
            return new AbsBlockResult(false);
        }

        private static string ColourOf(SheetRow r) => r.Col("Color", "Colour", "COLOR", "COLOUR", "colour", "color");
        private static string GmOf(SheetRow r) => r.Col("Generic Material", "generic material", "GENERIC MATERIAL");

        /// <summary>Port of <c>runEngineA(gm, colour, optionType, info)</c>.</summary>
        public static EngineAResult Run(EngineData data, string gm, string colour, string? optionType, GmInfo? info)
        {
            var p = data.Params;
            var wofGate = p.Wof; var peerMin = p.Peer;
            string mcCd = info?.McCd ?? "", mrp = info?.Mrp ?? "", flag = (info?.Flag ?? "").Trim();
            var resolve = ResolveIndex.ResolveMap(data);
            var bsr = data.Bsr;

            List<SheetRow> BuildPool(HashSet<string>? seasonSet)
            {
                if (bsr == null) return new List<SheetRow>();
                var nMC = Regex.Replace(mcCd.Trim(), @"\.0$", "");
                var nMRP = Regex.Replace(mrp.Trim(), @"\.0$", "");
                var nFlag = flag.ToLowerInvariant();
                var outL = new List<SheetRow>();
                foreach (var r in bsr)
                {
                    var rMC = Regex.Replace(r.Col("MC CD", "MC_CD", "mc cd").Trim(), @"\.0$", "");
                    var rMRP = Regex.Replace(r.Col("MRP", "mrp").Trim(), @"\.0$", "");
                    var rArt = r.Col("Article Type", "article type", "BOGO/NON-BOGO", "Flag", "flag").Trim().ToLowerInvariant();
                    var wof = Js.Num(r.Col("WEEK\n ON\nFLOOR", "WEEK ON FLOOR", "WOF", "wof"));
                    var ok = rMC == nMC && rMRP == nMRP && (nFlag.Length == 0 || rArt == nFlag || rArt.Length == 0) && wof >= wofGate;
                    if (!ok) continue;
                    if (seasonSet == null) { outL.Add(r); continue; }
                    var rGm = JsOps.StrOr(r, "Generic Material", "GENERIC MATERIAL").Trim();
                    if (!resolve.TryGetValue(rGm, out var peerInfo) || string.IsNullOrEmpty(peerInfo.Season)) continue;
                    var s = Ws.Replace(SeasonStrip.Replace(peerInfo.Season.Trim().ToUpperInvariant(), ""), "");
                    if (seasonSet.Contains(s)) outL.Add(r);
                }
                return outL;
            }

            var cur = p.SeasonCurrent ?? new List<string>();
            var fb = p.SeasonFallback ?? new List<string>();
            var curSet = new HashSet<string>(cur, StringComparer.Ordinal);
            var fbSet = new HashSet<string>(fb, StringComparer.Ordinal);
            var bsrLabel = "CY BSR (" + string.Join("+", cur) + ")";
            var peerPool = BuildPool(curSet);
            if (peerPool.Count < peerMin)
            {
                var fbPool = BuildPool(fbSet);
                if (fbPool.Count >= peerMin) { bsrLabel = "Fallback BSR (" + string.Join("+", fb) + ")"; peerPool = fbPool; }
                else if (fbPool.Count > 0) { bsrLabel = "Fallback BSR (" + string.Join("+", fb) + ", limited pool)"; peerPool = fbPool; }
            }

            double totalBsr = 0;
            if (bsr != null)
            {
                var mcT = mcCd.Trim(); var mrpT = Tool2Js.ReplaceFirstDot0(mrp.Trim());
                totalBsr = bsr.Count(r => r.Col("MC CD", "MC_CD", "mc cd").Trim() == mcT && Tool2Js.ReplaceFirstDot0(r.Col("MRP", "mrp").Trim()) == mrpT);
            }
            var cleanPool = peerPool;
            double peerAvg = 0;
            if (cleanPool.Count > 0) { double s = 0; foreach (var r in cleanPool) s += BsrPeerRank.GetPdstr(r); peerAvg = s / cleanPool.Count; }

            EngineAResult Res(string dec, string reason) => new()
            {
                Decision = dec, DecisionReason = reason, OptionType = optionType, BsrLabel = bsrLabel, PeerPool = peerPool, CleanPool = cleanPool,
                TotalBsr = totalBsr, PeerAvg = peerAvg
            };

            var bt = JsOps.Or(p.BorderTolerance, 10);
            var btS = Js.NumberToString(bt);

            if (optionType == "BOBs")
            {
                var gmT = gm.Trim(); var colT = colour.Trim();
                var allSibs = (bsr ?? Array.Empty<SheetRow>()).Where(r => GmOf(r).Trim() == gmT && ColourOf(r).Trim() != colT).ToList();
                var qualSibs = allSibs.Where(r => BsrPeerRank.GetWof(r) >= wofGate).ToList();
                if (qualSibs.Count > 0)
                {
                    var rankPool = cleanPool.Count > 0 ? cleanPool : peerPool;
                    var sibRanked = qualSibs.Select(r => new SibRank { Row = r, Q = BsrPeerRank.Quartile(BsrPeerRank.GetPdstr(r), rankPool), Pdstr = BsrPeerRank.GetPdstr(r) }).ToList();
                    var hasLow = sibRanked.Any(s => s.Q == "Q4" || s.Q == "Q3");
                    var decision = hasLow ? "Watch List" : "Allow";
                    var decisionReason = hasLow
                        ? "BOBs: " + qualSibs.Count + " sibling colourway(s) with WOF ≥ " + Js.NumberToString(wofGate) + "w found. At least one ranks Q4/Q3 — new colourway addition is risky."
                        : "BOBs: All " + qualSibs.Count + " qualifying sibling colourway(s) rank Q2/Q1 — parent style is a healthy seller. New colour permitted.";
                    if (decision == "Allow")
                    {
                        var best = qualSibs.Aggregate(qualSibs[0], (b, r) => BsrPeerRank.GetWof(r) > BsrPeerRank.GetWof(b) ? r : b);
                        var ab = AbsBlock("Allow", BsrPeerRank.GetPdstr(best), BsrPeerRank.GetWof(best), "best sibling colourway (" + gm + ")");
                        if (ab.Overridden) { decision = ab.Decision; decisionReason = ab.DecisionReason; }
                    }
                    var qOrder = new Dictionary<string, int> { ["Q4"] = 0, ["Q3"] = 1, ["Q2"] = 2, ["Q1"] = 3 };
                    int QO(string q) => qOrder.TryGetValue(q, out var v) ? v : -1;
                    var repSib = hasLow
                        ? sibRanked.Aggregate(sibRanked[0], (w, s) => (QO(s.Q) < QO(w.Q) || (QO(s.Q) == QO(w.Q) && s.Pdstr < w.Pdstr)) ? s : w)
                        : sibRanked.Aggregate(sibRanked[0], (b, s) => (QO(s.Q) > QO(b.Q) || (QO(s.Q) == QO(b.Q) && s.Pdstr > b.Pdstr)) ? s : b);
                    var repPool = cleanPool.Count > 0 ? cleanPool : peerPool;
                    var res = Res(decision, decisionReason);
                    res.Siblings = allSibs; res.QualSibs = qualSibs; res.SibRanked = sibRanked;
                    res.Wof = BsrPeerRank.GetWof(repSib.Row); res.Pdstr = repSib.Pdstr; res.Quartile = repSib.Q;
                    res.Percentile = BsrPeerRank.PctRank(repSib.Pdstr, repPool); res.RepSibling = repSib.Row;
                    return res;
                }
                if (allSibs.Count > 0)
                {
                    var bestSib = allSibs.Aggregate(allSibs[0], (b, r) => BsrPeerRank.GetWof(r) > BsrPeerRank.GetWof(b) ? r : b);
                    var parentWof = BsrPeerRank.GetWof(bestSib);
                    EngineAResult Sib(string dec, string reason, double? pd, string? q = null, double? pct = null)
                    {
                        var r0 = Res(dec, reason); r0.Siblings = allSibs; r0.Wof = parentWof; r0.Pdstr = pd; r0.Quartile = q; r0.Percentile = pct; return r0;
                    }
                    if (parentWof < wofGate)
                    {
                        var sp = BsrPeerRank.GetPdstr(bestSib);
                        var ab = AbsBlock("Allow", sp, parentWof, "best sibling of GM " + gm);
                        if (ab.Overridden) return Sib(ab.Decision, ab.DecisionReason, sp);
                        return Sib("Allow", "BOBs: " + allSibs.Count + " sibling(s) found but none qualify on WOF ≥ " + Js.NumberToString(wofGate) + "w (best WOF=" +
                                            Js.NumberToString(parentWof) + "w). Insufficient history — Auto Allow.", null);
                    }
                    {
                        var sp2 = BsrPeerRank.GetPdstr(bestSib);
                        var ab2 = AbsBlock("Allow", sp2, parentWof, "best sibling of GM " + gm);
                        if (ab2.Overridden) return Sib(ab2.Decision, ab2.DecisionReason, sp2);
                        if (cleanPool.Count == 0) return Sib("Allow", "BOBs fallthrough: no valid peer pool after IQR clean. Auto Allow.", null);
                    }
                    var pdstr = BsrPeerRank.GetPdstr(bestSib);
                    var q = BsrPeerRank.Quartile(pdstr, cleanPool);
                    var pct = BsrPeerRank.PctRank(pdstr, cleanPool);
                    var gapPct = peerAvg > 0 ? (peerAvg - pdstr) / peerAvg * 100 : 0;
                    var gapR = Js.Round(gapPct * 10) / 10;
                    var inBorder = gapPct > 0 && gapPct <= bt;
                    var pctS = Js.NumberToString(pct);
                    string dec, reason;
                    if (q == "Q2" || q == "Q1") { dec = "Allow"; reason = "BOBs fallthrough: parent style ranks " + q + " (" + pctS + "th pct, " + bsrLabel + ") — healthy. New colour permitted."; }
                    else if (q == "Q3")
                    {
                        if (pdstr >= peerAvg) { dec = "Allow"; reason = "BOBs fallthrough: parent ranks " + q + " (" + pctS + "th pct, " + bsrLabel + "). PDSTR " + Js.ToFixed(pdstr * 100, 1) + "%/d ≥ peer avg — at/above mean. Allow."; }
                        else if (inBorder) { dec = "Allow"; reason = "BOBs fallthrough: parent ranks " + q + " (" + pctS + "th pct, " + bsrLabel + "). PDSTR gap " + Js.NumberToString(gapR) + "% within border zone (≤" + btS + "%). Marginally below mean — Allow."; }
                        else { dec = "Watch List"; reason = "BOBs fallthrough: parent ranks " + q + " (" + pctS + "th pct, " + bsrLabel + "). PDSTR gap " + Js.NumberToString(gapR) + "% exceeds border zone (" + btS + "%). Category Head review."; }
                    }
                    else
                    {
                        if (pdstr >= peerAvg) { dec = "Watch List"; reason = "BOBs fallthrough: parent ranks " + q + " (" + pctS + "th pct, " + bsrLabel + "). PDSTR ≥ peer avg — Q4 but at/above mean. Category Head review."; }
                        else if (inBorder) { dec = "Watch List"; reason = "BOBs fallthrough: parent ranks " + q + " (" + pctS + "th pct, " + bsrLabel + "). PDSTR gap " + Js.NumberToString(gapR) + "% within border zone (≤" + btS + "%). Softened from Block to Watch List. Category Head review."; }
                        else { dec = "Block"; reason = "BOBs fallthrough: parent ranks " + q + " (" + pctS + "th pct, " + bsrLabel + "). PDSTR gap " + Js.NumberToString(gapR) + "% exceeds border zone (" + btS + "%). Confirmed slow. Do not raise."; }
                    }
                    if (dec == "Allow")
                    {
                        var ab = AbsBlock("Allow", pdstr, parentWof, "parent style (" + gm + ")");
                        if (ab.Overridden) { dec = ab.Decision; reason = ab.DecisionReason; }
                    }
                    var rr = Sib(dec, reason, pdstr, q, pct);
                    rr.GapPct = gapR; rr.InBorderZone = inBorder; rr.BorderTolUsed = bt;
                    return rr;
                }
                var none = Res("Allow", "BOBs: No sibling colourways found in CY BSR. Auto Allow (new colourway of untracked parent).");
                none.Wof = 0;
                return none;
            }

            // True Repeat
            var gmS = gm.Trim();
            var bsrRows = bsr == null ? new List<SheetRow>() : bsr.Where(r => GmOf(r).Trim() == gmS).ToList();
            var artRow = bsrRows.FirstOrDefault(r => ColourOf(r).Trim() == colour.Trim());
            var wofA = artRow != null ? BsrPeerRank.GetWof(artRow) : 0;
            var label = "GM+Colour (" + gm + " " + colour + ")";
            EngineAResult Tr(string dec, string reason, double? pd)
            {
                var r0 = Res(dec, reason); r0.Wof = wofA; r0.Pdstr = pd; return r0;
            }
            if (wofA < wofGate)
            {
                var ap = artRow != null ? BsrPeerRank.GetPdstr(artRow) : 0;
                var ab = AbsBlock("Allow", ap, wofA, label);
                if (ab.Overridden) return Tr(ab.Decision, ab.DecisionReason, ap);
                return Tr("Allow", "WOF = " + Js.NumberToString(wofA) + "w < " + Js.NumberToString(wofGate) + "w gate. Insufficient selling history — Auto Allow.", null);
            }
            if (cleanPool.Count == 0)
            {
                var ap = artRow != null ? BsrPeerRank.GetPdstr(artRow) : 0;
                var ab = AbsBlock("Allow", ap, wofA, label);
                if (ab.Overridden) return Tr(ab.Decision, ab.DecisionReason, ap);
                return Tr("Allow", "No peer pool available. Auto Allow.", null);
            }
            {
                var pdstr = artRow != null ? BsrPeerRank.GetPdstr(artRow) : 0;
                var q = BsrPeerRank.Quartile(pdstr, cleanPool);
                var pct = BsrPeerRank.PctRank(pdstr, cleanPool);
                var gapPct = peerAvg > 0 ? (peerAvg - pdstr) / peerAvg * 100 : 0;
                var gapR = Js.Round(gapPct * 10) / 10;
                var inBorder = gapPct > 0 && gapPct <= bt;
                var head = "True Repeat: ranks " + q + " (" + Js.NumberToString(pct) + "th pct) in peer group of " + cleanPool.Count + " styles (" + bsrLabel + "). ";
                var pdS = Js.ToFixed(pdstr * 100, 1); var avgS = Js.ToFixed(peerAvg * 100, 1); var gapS = Js.NumberToString(gapR);
                string dec, reason;
                if (q == "Q2" || q == "Q1") { dec = "Allow"; reason = head + "PDSTR = " + pdS + "%/day — healthy seller."; }
                else if (q == "Q3")
                {
                    if (pdstr >= peerAvg) { dec = "Allow"; reason = head + "PDSTR " + pdS + "%/day ≥ peer avg " + avgS + "%/day — at or above mean. Allow."; }
                    else if (inBorder) { dec = "Allow"; reason = head + "PDSTR " + pdS + "%/day is " + gapS + "% below peer avg — within border zone (≤" + btS + "%). Marginally below mean — Allow."; }
                    else { dec = "Watch List"; reason = head + "PDSTR " + pdS + "%/day is " + gapS + "% below peer avg — gap exceeds border zone (" + btS + "%). Category Head review required."; }
                }
                else
                {
                    if (pdstr >= peerAvg) { dec = "Watch List"; reason = head + "PDSTR " + pdS + "%/day ≥ peer avg — Q4 rank but at or above peer mean. Category Head review."; }
                    else if (inBorder) { dec = "Watch List"; reason = head + "PDSTR " + pdS + "%/day is " + gapS + "% below peer avg — within border zone (≤" + btS + "%). Softened from Block to Watch List. Category Head review."; }
                    else { dec = "Block"; reason = head + "PDSTR " + pdS + "%/day is " + gapS + "% below peer avg — gap exceeds border zone (" + btS + "%). Confirmed slow seller. Do not raise PO."; }
                }
                if (dec == "Allow")
                {
                    var ab = AbsBlock(dec, pdstr, wofA, label);
                    if (ab.Overridden) { dec = ab.Decision; reason = ab.DecisionReason; }
                }
                var rr = Tr(dec, reason, pdstr);
                rr.Quartile = q; rr.Percentile = pct; rr.GapPct = gapR; rr.InBorderZone = inBorder; rr.BorderTolUsed = bt;
                return rr;
            }
        }
    }
}
