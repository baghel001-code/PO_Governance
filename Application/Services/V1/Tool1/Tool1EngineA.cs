using Application.DTOs.Tool1;
using Application.Services.V1.Engines.Common;
using Domain.Constants;
using Domain.Entities.Common;
using Domain.Entities.Engine;

namespace Application.Services.V1.Tool1
{
    /// <summary>
    /// Port of index.html <c>runEngineA</c> (line 4270) — Tool 1 Multi Concept MRP: peer pool from BSR
    /// (same MC / MRP / Flag, WOF ≥ gate, current seasons with fallback), PDSTR quartile, border zone,
    /// BOBs sibling logic and the absolute-block override (<c>absBlock</c>, line 4257). Uses the shared
    /// <see cref="BsrPeerRank"/> (getWOF / getPDSTR / quartile / pctRank) and <see cref="ResolveIndex"/>.
    /// </summary>
    public sealed class Tool1EngineA
    {
        /// <summary>Port of <c>getBSRRows(src, gm)</c>.</summary>
        public static List<SheetRow> GetBsrRows(IReadOnlyList<SheetRow>? src, string gm)
        {
            if (src == null) return new();
            var g = gm.Trim();
            return src.Where(r => r.Col("Generic Material", "generic material", "GENERIC MATERIAL").Trim() == g).ToList();
        }

        /// <summary>Port of <c>absBlock(decision, pdstr, wof, label)</c>: (overridden, decision, reason).</summary>
        public static (bool Overridden, string? Decision, string? Reason) AbsBlock(string decision, double pdstr, double wof, string label)
        {
            const double pdstrThreshold = 0.01, wofMin = 4;
            if ((decision == Decisions.Allow || decision == Decisions.AutoAllow) && wof >= wofMin && pdstr < pdstrThreshold)
                return (true, Decisions.Block,
                    "Absolute Block override: " + label + " has AVG PERDAY SELL THRU% = " + Js.ToFixed(pdstr * 100, 1) +
                    "% < 1% threshold with " + Js.NumberToString(wof) + "w on floor (≥ 4w gate). Confirmed very slow seller — do not raise PO regardless of peer ranking.");
            return (false, null, null);
        }

        public Tool1EngineAResult Run(EngineData data, string gm, string colour, string optionType, Tool1InfoDto? info)
        {
            var p = data.Params;
            double wofGate = p.Wof, peerMin = p.Peer;
            var mcCd = info?.McCd ?? "";
            var mrp = info?.Mrp ?? "";
            var flag = (info?.Flag ?? "").Trim();
            var resolve = ResolveIndex.ResolveMap(data);

            List<SheetRow> BuildPool(IReadOnlyList<SheetRow>? src, HashSet<string>? seasonSet)
            {
                if (src == null) return new();
                var nMC = Tool1JsHelpers.StripDot0(mcCd);
                var nMRP = Tool1JsHelpers.StripDot0(mrp);
                var nFlag = flag.ToLowerInvariant();
                return src.Where(r =>
                {
                    var rMC = Tool1JsHelpers.StripDot0(r.Col("MC CD", "MC_CD", "mc cd"));
                    var rMRP = Tool1JsHelpers.StripDot0(r.Col("MRP", "mrp"));
                    var rArt = r.Col("Article Type", "article type", "BOGO/NON-BOGO", "Flag", "flag").Trim().ToLowerInvariant();
                    var wof = Js.Num(r.Col("WEEK\n ON\nFLOOR", "WEEK ON FLOOR", "WOF", "wof"));
                    var ok = rMC == nMC && rMRP == nMRP && (nFlag.Length == 0 || rArt == nFlag || rArt == "") && wof >= wofGate;
                    if (!ok) return false;
                    if (seasonSet == null) return true;
                    var rGm = JsOps.StrOr(r, "Generic Material", "GENERIC MATERIAL").Trim();
                    if (!resolve.TryGetValue(rGm, out var peer) || string.IsNullOrEmpty(peer.Season)) return false;
                    var s = System.Text.RegularExpressions.Regex.Replace(peer.Season.Trim().ToUpperInvariant().Replace("-", "").Replace("_", ""), @"\s+", "");
                    return seasonSet.Contains(s);
                }).ToList();
            }

            var curSet = new HashSet<string>(p.SeasonCurrent ?? new(), StringComparer.Ordinal);
            var fbSet = new HashSet<string>(p.SeasonFallback ?? new(), StringComparer.Ordinal);
            var bsrSrc = data.Bsr;
            var bsrLabel = "CY BSR (" + string.Join("+", p.SeasonCurrent ?? new()) + ")";
            var peerPool = BuildPool(bsrSrc, curSet);
            if (peerPool.Count < peerMin)
            {
                var fbPool = BuildPool(bsrSrc, fbSet);
                if (fbPool.Count >= peerMin)
                {
                    bsrLabel = "Fallback BSR (" + string.Join("+", p.SeasonFallback ?? new()) + ")";
                    peerPool = fbPool;
                }
                else if (fbPool.Count > 0)
                {
                    bsrLabel = "Fallback BSR (" + string.Join("+", p.SeasonFallback ?? new()) + ", limited pool)";
                    peerPool = fbPool;
                }
            }

            double totalBSR = bsrSrc == null ? 0 : bsrSrc.Count(r =>
                r.Col("MC CD", "MC_CD", "mc cd").Trim() == mcCd.Trim() &&
                Tool1JsHelpers.ReplaceFirstDot0(r.Col("MRP", "mrp").Trim()) == Tool1JsHelpers.ReplaceFirstDot0(mrp.Trim()));
            var cleanPool = peerPool;
            var peerAvg = cleanPool.Count > 0 ? cleanPool.Aggregate(0.0, (s, r) => s + BsrPeerRank.GetPdstr(r)) / cleanPool.Count : 0;

            Tool1EngineAResult Res(string decision, string reason, List<SheetRow> siblings, double wof, double? pdstr, string? q, double? pct) => new()
            {
                Decision = decision, DecisionReason = reason, OptionType = optionType, BsrLabel = bsrLabel, PeerPool = peerPool,
                CleanPool = cleanPool, TotalBSR = totalBSR, PeerAvg = peerAvg, Siblings = siblings, QualSibs = new(), SibRanked = new(),
                Wof = wof, Pdstr = pdstr, Quartile = q, Percentile = pct
            };

            var bt = JsOps.Or(p.BorderTolerance, 10);

            if (optionType == "BOBs")
            {
                var gmT = gm.Trim();
                var colT = colour.Trim();
                var allSibs = (data.Bsr ?? Array.Empty<SheetRow>()).Where(r =>
                    r.Col("Generic Material", "generic material", "GENERIC MATERIAL").Trim() == gmT &&
                    r.Col("Color", "Colour", "COLOR", "COLOUR", "colour", "color").Trim() != colT).ToList();
                var qualSibs = allSibs.Where(r => BsrPeerRank.GetWof(r) >= wofGate).ToList();
                if (qualSibs.Count > 0)
                {
                    var rankPool = cleanPool.Count > 0 ? cleanPool : peerPool;
                    var sibRanked = qualSibs.Select(r => new Tool1SiblingRankDto
                    {
                        Row = r, Q = BsrPeerRank.Quartile(BsrPeerRank.GetPdstr(r), rankPool), Pdstr = BsrPeerRank.GetPdstr(r)
                    }).ToList();
                    var hasLow = sibRanked.Any(s => s.Q == "Q4" || s.Q == "Q3");
                    var decision = hasLow ? Decisions.WatchList : Decisions.Allow;
                    var reason = hasLow
                        ? "BOBs: " + qualSibs.Count + " sibling colourway(s) with WOF ≥ " + Js.NumberToString(wofGate) + "w found. At least one ranks Q4/Q3 — new colourway addition is risky."
                        : "BOBs: All " + qualSibs.Count + " qualifying sibling colourway(s) rank Q2/Q1 — parent style is a healthy seller. New colour permitted.";
                    if (decision == Decisions.Allow)
                    {
                        var best = qualSibs.Aggregate(qualSibs[0], (b, r) => BsrPeerRank.GetWof(r) > BsrPeerRank.GetWof(b) ? r : b);
                        var ab = AbsBlock(Decisions.Allow, BsrPeerRank.GetPdstr(best), BsrPeerRank.GetWof(best), "best sibling colourway (" + gm + ")");
                        if (ab.Overridden) { decision = ab.Decision!; reason = ab.Reason!; }
                    }
                    var qOrder = new Dictionary<string, int> { ["Q4"] = 0, ["Q3"] = 1, ["Q2"] = 2, ["Q1"] = 3 };
                    var rep = hasLow
                        ? sibRanked.Aggregate(sibRanked[0], (w, s) => qOrder[s.Q] < qOrder[w.Q] || (qOrder[s.Q] == qOrder[w.Q] && s.Pdstr < w.Pdstr) ? s : w)
                        : sibRanked.Aggregate(sibRanked[0], (b, s) => qOrder[s.Q] > qOrder[b.Q] || (qOrder[s.Q] == qOrder[b.Q] && s.Pdstr > b.Pdstr) ? s : b);
                    return new Tool1EngineAResult
                    {
                        Decision = decision, DecisionReason = reason, OptionType = optionType, BsrLabel = bsrLabel, PeerPool = peerPool,
                        CleanPool = cleanPool, TotalBSR = totalBSR, PeerAvg = peerAvg, Siblings = allSibs, QualSibs = qualSibs, SibRanked = sibRanked,
                        Wof = BsrPeerRank.GetWof(rep.Row), Pdstr = rep.Pdstr, Quartile = rep.Q, Percentile = BsrPeerRank.PctRank(rep.Pdstr, rankPool),
                        RepSibling = rep.Row
                    };
                }
                if (allSibs.Count > 0)
                {
                    var bestSib = allSibs.Aggregate(allSibs[0], (b, r) => BsrPeerRank.GetWof(r) > BsrPeerRank.GetWof(b) ? r : b);
                    var parentWOF = BsrPeerRank.GetWof(bestSib);
                    if (parentWOF < wofGate)
                    {
                        var sp = BsrPeerRank.GetPdstr(bestSib);
                        var ab = AbsBlock(Decisions.Allow, sp, parentWOF, "best sibling of GM " + gm);
                        if (ab.Overridden) return Res(ab.Decision!, ab.Reason!, allSibs, parentWOF, sp, null, null);
                        return Res(Decisions.Allow, "BOBs: " + allSibs.Count + " sibling(s) found but none qualify on WOF ≥ " + Js.NumberToString(wofGate) +
                                   "w (best WOF=" + Js.NumberToString(parentWOF) + "w). Insufficient history — Auto Allow.", allSibs, parentWOF, null, null, null);
                    }
                    {
                        var sp2 = BsrPeerRank.GetPdstr(bestSib);
                        var ab2 = AbsBlock(Decisions.Allow, sp2, parentWOF, "best sibling of GM " + gm);
                        if (ab2.Overridden) return Res(ab2.Decision!, ab2.Reason!, allSibs, parentWOF, sp2, null, null);
                        if (cleanPool.Count == 0) return Res(Decisions.Allow, "BOBs fallthrough: no valid peer pool after IQR clean. Auto Allow.", allSibs, parentWOF, null, null, null);
                    }
                    var pdstr = BsrPeerRank.GetPdstr(bestSib);
                    var q = BsrPeerRank.Quartile(pdstr, cleanPool);
                    var pct = BsrPeerRank.PctRank(pdstr, cleanPool);
                    var gapPct = peerAvg > 0 ? (peerAvg - pdstr) / peerAvg * 100 : 0;
                    var gapPctRound = Js.Round(gapPct * 10) / 10;
                    var inBorderZone = gapPct > 0 && gapPct <= bt;
                    string pre = "BOBs fallthrough: parent ranks " + q + " (" + Js.NumberToString(pct) + "th pct, " + bsrLabel + ")";
                    string g = Js.NumberToString(gapPctRound), bts = Js.NumberToString(bt);
                    string dec, reason;
                    if (q == "Q2" || q == "Q1")
                    {
                        dec = Decisions.Allow;
                        reason = "BOBs fallthrough: parent style ranks " + q + " (" + Js.NumberToString(pct) + "th pct, " + bsrLabel + ") — healthy. New colour permitted.";
                    }
                    else if (q == "Q3")
                    {
                        if (pdstr >= peerAvg) { dec = Decisions.Allow; reason = pre + ". PDSTR " + Js.ToFixed(pdstr * 100, 1) + "%/d ≥ peer avg — at/above mean. Allow."; }
                        else if (inBorderZone) { dec = Decisions.Allow; reason = pre + ". PDSTR gap " + g + "% within border zone (≤" + bts + "%). Marginally below mean — Allow."; }
                        else { dec = Decisions.WatchList; reason = pre + ". PDSTR gap " + g + "% exceeds border zone (" + bts + "%). Category Head review."; }
                    }
                    else
                    {
                        if (pdstr >= peerAvg) { dec = Decisions.WatchList; reason = pre + ". PDSTR ≥ peer avg — Q4 but at/above mean. Category Head review."; }
                        else if (inBorderZone) { dec = Decisions.WatchList; reason = pre + ". PDSTR gap " + g + "% within border zone (≤" + bts + "%). Softened from Block to Watch List. Category Head review."; }
                        else { dec = Decisions.Block; reason = pre + ". PDSTR gap " + g + "% exceeds border zone (" + bts + "%). Confirmed slow. Do not raise."; }
                    }
                    if (dec == Decisions.Allow)
                    {
                        var ab = AbsBlock(Decisions.Allow, pdstr, parentWOF, "parent style (" + gm + ")");
                        if (ab.Overridden) { dec = ab.Decision!; reason = ab.Reason!; }
                    }
                    var res = Res(dec, reason, allSibs, parentWOF, pdstr, q, pct);
                    res.GapPct = gapPctRound; res.InBorderZone = inBorderZone; res.BorderTolUsed = bt;
                    return res;
                }
                return Res(Decisions.Allow, "BOBs: No sibling colourways found in CY BSR. Auto Allow (new colourway of untracked parent).", new(), 0, null, null, null);
            }

            // True Repeat: WOF gate on the article itself
            var bsrRows = GetBsrRows(data.Bsr, gm);
            var artRow = bsrRows.FirstOrDefault(r => r.Col("Color", "Colour", "COLOR", "COLOUR", "colour", "color").Trim() == colour.Trim());
            var wofA = artRow != null ? BsrPeerRank.GetWof(artRow) : 0;
            var label = "GM+Colour (" + gm + " " + colour + ")";
            if (wofA < wofGate)
            {
                var ap = artRow != null ? BsrPeerRank.GetPdstr(artRow) : 0;
                var ab = AbsBlock(Decisions.Allow, ap, wofA, label);
                if (ab.Overridden) return Res(ab.Decision!, ab.Reason!, new(), wofA, ap, null, null);
                return Res(Decisions.Allow, "WOF = " + Js.NumberToString(wofA) + "w < " + Js.NumberToString(wofGate) + "w gate. Insufficient selling history — Auto Allow.", new(), wofA, null, null, null);
            }
            if (cleanPool.Count == 0)
            {
                var ap = artRow != null ? BsrPeerRank.GetPdstr(artRow) : 0;
                var ab = AbsBlock(Decisions.Allow, ap, wofA, label);
                if (ab.Overridden) return Res(ab.Decision!, ab.Reason!, new(), wofA, ap, null, null);
                return Res(Decisions.Allow, "No peer pool available. Auto Allow.", new(), wofA, null, null, null);
            }

            var pd = artRow != null ? BsrPeerRank.GetPdstr(artRow) : 0;
            var qa = BsrPeerRank.Quartile(pd, cleanPool);
            var pcta = BsrPeerRank.PctRank(pd, cleanPool);
            var gap = peerAvg > 0 ? (peerAvg - pd) / peerAvg * 100 : 0;
            var gapR = Js.Round(gap * 10) / 10;
            var inBz = gap > 0 && gap <= bt;
            var head = "True Repeat: ranks " + qa + " (" + Js.NumberToString(pcta) + "th pct) in peer group of " + cleanPool.Count + " styles (" + bsrLabel + "). ";
            var pdTxt = Js.ToFixed(pd * 100, 1);
            string gTxt = Js.NumberToString(gapR), btTxt = Js.NumberToString(bt);
            string decA, reasonA;
            if (qa == "Q2" || qa == "Q1")
            {
                decA = Decisions.Allow;
                reasonA = head + "PDSTR = " + pdTxt + "%/day — healthy seller.";
            }
            else if (qa == "Q3")
            {
                if (pd >= peerAvg) { decA = Decisions.Allow; reasonA = head + "PDSTR " + pdTxt + "%/day ≥ peer avg " + Js.ToFixed(peerAvg * 100, 1) + "%/day — at or above mean. Allow."; }
                else if (inBz) { decA = Decisions.Allow; reasonA = head + "PDSTR " + pdTxt + "%/day is " + gTxt + "% below peer avg — within border zone (≤" + btTxt + "%). Marginally below mean — Allow."; }
                else { decA = Decisions.WatchList; reasonA = head + "PDSTR " + pdTxt + "%/day is " + gTxt + "% below peer avg — gap exceeds border zone (" + btTxt + "%). Category Head review required."; }
            }
            else
            {
                if (pd >= peerAvg) { decA = Decisions.WatchList; reasonA = head + "PDSTR " + pdTxt + "%/day ≥ peer avg — Q4 rank but at or above peer mean. Category Head review."; }
                else if (inBz) { decA = Decisions.WatchList; reasonA = head + "PDSTR " + pdTxt + "%/day is " + gTxt + "% below peer avg — within border zone (≤" + btTxt + "%). Softened from Block to Watch List. Category Head review."; }
                else { decA = Decisions.Block; reasonA = head + "PDSTR " + pdTxt + "%/day is " + gTxt + "% below peer avg — gap exceeds border zone (" + btTxt + "%). Confirmed slow seller. Do not raise PO."; }
            }
            if (decA == Decisions.Allow)
            {
                var ab = AbsBlock(decA, pd, wofA, label);
                if (ab.Overridden) { decA = ab.Decision!; reasonA = ab.Reason!; }
            }
            var ra = Res(decA, reasonA, new(), wofA, pd, qa, pcta);
            ra.GapPct = gapR; ra.InBorderZone = inBz; ra.BorderTolUsed = bt;
            return ra;
        }
    }
}
