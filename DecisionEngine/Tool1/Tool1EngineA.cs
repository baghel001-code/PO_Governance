using DecisionEngine.Common;
using DecisionEngine.Model;
using DecisionEngine.Results.Tool1;

namespace DecisionEngine.Tool1
{
    /// <summary>
    /// Tool 1 Multi Concept MRP (articles not in the Universe Master) — originally a port of index.html <c>runEngineA</c>
    /// (line 4270), changed by the <b>multi concept rule, Step 1</b> (Nov 2026):
    /// <list type="bullet">
    /// <item>Peer pool from BSR: same <b>Brand</b>, MC, MRP and Flag, WOF ≥ gate, current seasons with fallback (Brand added
    /// in Step 1; the peer's brand is the BSR row's Brand Type, else the Article Master / Universe Master / F2 one).</item>
    /// <item>True Repeat ranked on AVG PERDAY SELL THRU% (PDSTR): <b>Q1 (top 25 %) → Allow, Q2 / Q3 / Q4 → Block</b>. No Watch
    /// List and no border zone any more.</item>
    /// <item>BOBs (new colour of an existing option): every other colour with WOF ≥ gate must rank Q1 to Allow, else Block.
    /// No other colours, or none with WOF ≥ gate → Allow as before.</item>
    /// <item>Unchanged: WOF below the gate → Allow; no peer pool → Allow; the absolute-block override (<see cref="AbsBlock"/>);
    /// new articles (engine NEW) → Auto Allow.</item>
    /// <item><b>Step 2</b> (Nov 2026): a Q1 is allowed only when its per-day sell-thru is at least the minimum of the option's
    /// Entry / Mid / Fashion type (<see cref="EmfMapping"/>: Entry 2.0 %, Mid 1.5 %, Fashion 1.5 %/day); below → Block. The
    /// option's Brand × MC × Flag × MRP not in the uploaded mapping → Hold "Planner has not maintained this MC-MRP in the
    /// master." BOBs: the minimum is checked on the oldest other colour (most weeks on floor). Only a ranking that would
    /// otherwise Allow is checked — Q2–Q4 stay Block, the WOF / no-peer-pool / new-article allows are unchanged.</item>
    /// </list>
    /// The JavaScript reference of the same rule, used by the parity harnesses, is <c>tests/parity/rules/multi-concept.js</c>.
    /// Tool 2 runs this engine through <see cref="Tool2.Tool2EngineA"/> (an adapter to Tool 2's line model).
    /// </summary>
    public sealed class Tool1EngineA
    {
        /// <summary>The only multi concept quartile that is allowed (Step 1).</summary>
        public const string AllowQuartile = "Q1";

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

        /// <summary>
        /// The brand of a BSR peer row: its own Brand Type column when filled, else the brand the GM resolves to
        /// (Article Master / Universe Master / F2). Empty when unknown.
        /// </summary>
        public static string PeerBrand(SheetRow r, IReadOnlyDictionary<string, GmInfo> resolve)
        {
            var own = r.Col("Brand Type", "BRAND TYPE", "brand type").Trim();
            if (own.Length > 0) return own;
            var gm = JsOps.StrOr(r, "Generic Material", "GENERIC MATERIAL").Trim();
            return resolve.TryGetValue(gm, out var i) ? (i.Brand ?? "").Trim() : "";
        }

        public Tool1EngineAResult Run(EngineData data, string gm, string colour, string optionType, Tool1InfoDto? info)
        {
            var p = data.Params;
            double wofGate = p.Wof, peerMin = p.Peer;
            var mcCd = info?.McCd ?? "";
            var mrp = info?.Mrp ?? "";
            var brand = (info?.Brand ?? "").Trim();
            var flag = (info?.Flag ?? "").Trim();
            var resolve = ResolveIndex.ResolveMap(data);

            List<SheetRow> BuildPool(IReadOnlyList<SheetRow>? src, HashSet<string>? seasonSet)
            {
                if (src == null) return new();
                var nMC = Tool1JsHelpers.StripDot0(mcCd);
                var nMRP = Tool1JsHelpers.StripDot0(mrp);
                var nFlag = flag.ToLowerInvariant();
                var nBrand = brand.ToLowerInvariant();
                return src.Where(r =>
                {
                    var rMC = Tool1JsHelpers.StripDot0(r.Col("MC CD", "MC_CD", "mc cd"));
                    var rMRP = Tool1JsHelpers.StripDot0(r.Col("MRP", "mrp"));
                    var rArt = r.Col("Article Type", "article type", "BOGO/NON-BOGO", "Flag", "flag").Trim().ToLowerInvariant();
                    var wof = Js.Num(r.Col("WEEK\n ON\nFLOOR", "WEEK ON FLOOR", "WOF", "wof"));
                    var ok = rMC == nMC && rMRP == nMRP && (nFlag.Length == 0 || rArt == nFlag || rArt == "") && wof >= wofGate;
                    if (!ok) return false;
                    // Step 1: same brand (a peer whose brand is not known is kept, like a blank flag)
                    if (nBrand.Length > 0)
                    {
                        var rBrand = PeerBrand(r, resolve).ToLowerInvariant();
                        if (rBrand.Length > 0 && rBrand != nBrand) return false;
                    }
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
                    var best = qualSibs.Aggregate(qualSibs[0], (b, r) => BsrPeerRank.GetWof(r) > BsrPeerRank.GetWof(b) ? r : b);
                    var rankPool = cleanPool.Count > 0 ? cleanPool : peerPool;
                    if (rankPool.Count == 0)
                    {
                        // nothing to rank the other colours against: allowed, like a True Repeat without a peer pool
                        var bp = BsrPeerRank.GetPdstr(best);
                        var bw = BsrPeerRank.GetWof(best);
                        var ab0 = AbsBlock(Decisions.Allow, bp, bw, "best sibling colourway (" + gm + ")");
                        var r0 = ab0.Overridden
                            ? Res(ab0.Decision!, ab0.Reason!, allSibs, bw, bp, null, null)
                            : Res(Decisions.Allow, "BOBs: " + qualSibs.Count + " sibling colourway(s) with WOF ≥ " + Js.NumberToString(wofGate) +
                                  "w found, but no peer pool to rank them against. Auto Allow.", allSibs, bw, null, null, null);
                        r0.QualSibs = qualSibs;
                        return r0;
                    }
                    var sibRanked = qualSibs.Select(r => new Tool1SiblingRankDto
                    {
                        Row = r, Q = BsrPeerRank.Quartile(BsrPeerRank.GetPdstr(r), rankPool), Pdstr = BsrPeerRank.GetPdstr(r)
                    }).ToList();
                    var low = sibRanked.Where(s => s.Q != AllowQuartile).ToList();
                    var hasLow = low.Count > 0;
                    var decision = hasLow ? Decisions.Block : Decisions.Allow;
                    var reason = hasLow
                        ? "BOBs: " + low.Count + " of " + qualSibs.Count + " sibling colourway(s) with WOF ≥ " + Js.NumberToString(wofGate) + "w rank below Q1 (" +
                          string.Join(", ", low.Select(s => s.Row.Col("Color", "Colour", "COLOR", "COLOUR", "colour", "color").Trim() + " " + s.Q)) +
                          "). Every other colour must rank Q1 (top 25%) — new colour blocked."
                        : "BOBs: All " + qualSibs.Count + " sibling colourway(s) with WOF ≥ " + Js.NumberToString(wofGate) +
                          "w rank Q1 (top 25%) — parent style is a top seller. New colour permitted.";
                    string? emfTag = null, emfCheck = null;
                    double? emfMin = null, emfPd = null;
                    if (decision == Decisions.Allow)
                    {
                        var ab = AbsBlock(Decisions.Allow, BsrPeerRank.GetPdstr(best), BsrPeerRank.GetWof(best), "best sibling colourway (" + gm + ")");
                        if (ab.Overridden) { decision = ab.Decision!; reason = ab.Reason!; }
                        else
                        {
                            // Step 2: the minimum of the option's type, checked on the oldest other colour (most weeks on floor)
                            var oldest = best;
                            var oc = oldest.Col("Color", "Colour", "COLOR", "COLOUR", "colour", "color").Trim();
                            var ow = BsrPeerRank.GetWof(oldest);
                            emfPd = BsrPeerRank.GetPdstr(oldest);
                            emfTag = EmfMapping.Lookup(data, brand, mcCd, flag, mrp);
                            var allQ1 = "BOBs: All " + qualSibs.Count + " sibling colourway(s) with WOF ≥ " + Js.NumberToString(wofGate) + "w rank Q1 (top 25%)";
                            if (emfTag == null)
                            {
                                emfCheck = EmfMapping.CheckNotMapped;
                                decision = Decisions.Hold;
                                reason = EmfMapping.NotMaintainedMessage + " (" + EmfMapping.Describe(brand, mcCd, flag, mrp) +
                                         " is not in the Entry / Mid / Fashion mapping. " + allQ1 + ".)";
                            }
                            else
                            {
                                emfMin = EmfMapping.MinPdstr(emfTag);
                                var minTxt = "the " + emfTag + " minimum " + Js.ToFixed(emfMin!.Value * 100, 1) + "%/day";
                                var oldTxt = "the oldest colour " + oc + " (" + Js.NumberToString(ow) + "w on floor) sells " + Js.ToFixed(emfPd.Value * 100, 1) + "%/day";
                                if (EmfMapping.MeetsMinimum(emfPd.Value, emfMin.Value))
                                {
                                    emfCheck = EmfMapping.CheckOk;
                                    reason = allQ1 + " and " + oldTxt + " — at least " + minTxt + ". New colour permitted.";
                                }
                                else
                                {
                                    emfCheck = EmfMapping.CheckBelow;
                                    decision = Decisions.Block;
                                    reason = allQ1 + ", but " + oldTxt + " — below " + minTxt + ". New colour blocked.";
                                }
                            }
                        }
                    }
                    // the sibling that drove the decision: Block → the worst-ranked colour, Allow → the best-ranked one
                    var qOrder = new Dictionary<string, int> { ["Q4"] = 0, ["Q3"] = 1, ["Q2"] = 2, ["Q1"] = 3 };
                    var rep = hasLow
                        ? sibRanked.Aggregate(sibRanked[0], (w, s) => qOrder[s.Q] < qOrder[w.Q] || (qOrder[s.Q] == qOrder[w.Q] && s.Pdstr < w.Pdstr) ? s : w)
                        : sibRanked.Aggregate(sibRanked[0], (b, s) => qOrder[s.Q] > qOrder[b.Q] || (qOrder[s.Q] == qOrder[b.Q] && s.Pdstr > b.Pdstr) ? s : b);
                    return new Tool1EngineAResult
                    {
                        Decision = decision, DecisionReason = reason, OptionType = optionType, BsrLabel = bsrLabel, PeerPool = peerPool,
                        CleanPool = cleanPool, TotalBSR = totalBSR, PeerAvg = peerAvg, Siblings = allSibs, QualSibs = qualSibs, SibRanked = sibRanked,
                        Wof = BsrPeerRank.GetWof(rep.Row), Pdstr = rep.Pdstr, Quartile = rep.Q, Percentile = BsrPeerRank.PctRank(rep.Pdstr, rankPool),
                        RepSibling = rep.Row, EmfTag = emfTag, EmfMin = emfMin, EmfCheck = emfCheck, EmfPdstr = emfPd
                    };
                }
                if (allSibs.Count > 0)
                {
                    // other colours exist but none has WOF ≥ gate: allowed as before (unless the absolute block applies)
                    var bestSib = allSibs.Aggregate(allSibs[0], (b, r) => BsrPeerRank.GetWof(r) > BsrPeerRank.GetWof(b) ? r : b);
                    var parentWOF = BsrPeerRank.GetWof(bestSib);
                    var sp = BsrPeerRank.GetPdstr(bestSib);
                    var ab = AbsBlock(Decisions.Allow, sp, parentWOF, "best sibling of GM " + gm);
                    if (ab.Overridden) return Res(ab.Decision!, ab.Reason!, allSibs, parentWOF, sp, null, null);
                    return Res(Decisions.Allow, "BOBs: " + allSibs.Count + " sibling(s) found but none qualify on WOF ≥ " + Js.NumberToString(wofGate) +
                               "w (best WOF=" + Js.NumberToString(parentWOF) + "w). Insufficient history — Auto Allow.", allSibs, parentWOF, null, null, null);
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
            var head = "True Repeat: ranks " + qa + " (" + Js.NumberToString(pcta) + "th pct) in peer group of " + cleanPool.Count + " styles (" + bsrLabel + "). ";
            var pdTxt = Js.ToFixed(pd * 100, 1);
            string decA, reasonA;
            string? tagA = null, checkA = null;
            double? minA = null;
            if (qa == AllowQuartile)
            {
                decA = Decisions.Allow;
                reasonA = head + "PDSTR = " + pdTxt + "%/day — top 25% (Q1). Allow.";
                var ab = AbsBlock(decA, pd, wofA, label);
                if (ab.Overridden) { decA = ab.Decision!; reasonA = ab.Reason!; }
                else
                {
                    // Step 2: Q1 and at least the minimum of the option's Entry / Mid / Fashion type
                    tagA = EmfMapping.Lookup(data, brand, mcCd, flag, mrp);
                    if (tagA == null)
                    {
                        checkA = EmfMapping.CheckNotMapped;
                        decA = Decisions.Hold;
                        reasonA = EmfMapping.NotMaintainedMessage + " (" + EmfMapping.Describe(brand, mcCd, flag, mrp) +
                                  " is not in the Entry / Mid / Fashion mapping. " + head + "PDSTR = " + pdTxt + "%/day.)";
                    }
                    else
                    {
                        minA = EmfMapping.MinPdstr(tagA);
                        var minTxt = "the " + tagA + " minimum " + Js.ToFixed(minA!.Value * 100, 1) + "%/day";
                        if (EmfMapping.MeetsMinimum(pd, minA.Value))
                        {
                            checkA = EmfMapping.CheckOk;
                            reasonA = head + "PDSTR = " + pdTxt + "%/day — top 25% (Q1) and at least " + minTxt + ". Allow.";
                        }
                        else
                        {
                            checkA = EmfMapping.CheckBelow;
                            decA = Decisions.Block;
                            reasonA = head + "PDSTR = " + pdTxt + "%/day — Q1 but below " + minTxt + " (first 3 weeks). Block. Do not raise PO.";
                        }
                    }
                }
            }
            else
            {
                decA = Decisions.Block;
                reasonA = head + "PDSTR = " + pdTxt + "%/day (peer avg " + Js.ToFixed(peerAvg * 100, 1) +
                          "%/day). Only Q1 (top 25%) is allowed for multi concept — Block. Do not raise PO.";
            }
            var ra = Res(decA, reasonA, new(), wofA, pd, qa, pcta);
            ra.GapPct = Js.Round(gap * 10) / 10;   // shown as "Gap from Avg" (information only — no border zone any more)
            ra.EmfTag = tagA; ra.EmfMin = minA; ra.EmfCheck = checkA; ra.EmfPdstr = checkA != null ? pd : null;
            return ra;
        }
    }
}
