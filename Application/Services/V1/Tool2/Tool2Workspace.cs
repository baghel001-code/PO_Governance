using System.Text.RegularExpressions;
using Application.DTOs.Recommendation;
using Application.DTOs.Tool2;
using Application.Interfaces.V1.Recommendation;
using Application.Services.V1.Engines.Common;
using Application.Services.V1.Recommendation;
using Domain.Constants;
using Domain.Entities.Common;
using Domain.Entities.Engine;

namespace Application.Services.V1.Tool2
{
    /// <summary>
    /// A run's results plus the data / parameters they are shown with — the server-side equivalent of the
    /// browser's <c>batchResults</c> + <c>S.files</c> + <c>S.params</c> after a run or after re-opening a saved
    /// run. Holds the ports of the per-line text / recommendation helpers of index.html (lines 6180–7660):
    /// <c>buildDecisionTooltip</c>, <c>buildDetailedReason</c>, <c>_recommendedActionA</c>, <c>_rankedOptionsA</c>,
    /// <c>_mcKeyB</c>, <c>_rankedColoursB</c>, <c>_rankedOptionsAny</c>, <c>_bobsQ1Options*</c>,
    /// <c>_allSiblingsWithPDSTR</c>, <c>_q1PeerAvgPDSTR</c>, <c>_trueRepeatRecommendation</c>, the Category Head
    /// mapping helpers, <c>_bucketFor</c>, <c>_poRetentionInfo</c>, <c>_poStatusFlag</c>,
    /// <c>_multiConceptAttrRecommendation</c>, <c>_reduceCancelRecommendation</c>, <c>_reduceCancelOptions</c>.
    /// </summary>
    public sealed partial class Tool2Workspace
    {
        public Tool2Workspace(IReadOnlyList<Tool2LineResult> results, RecommendationContext ctx, IRecommendationEngine rec)
        {
            Results = results;
            Ctx = ctx;
            Rec = rec;
        }

        public IReadOnlyList<Tool2LineResult> Results { get; }
        public RecommendationContext Ctx { get; }
        public EngineData Data => Ctx.Data;
        public EngineParams P => Ctx.P;
        public IRecommendationEngine Rec { get; }
        /// <summary>_runACache / _runBCache of the export (one per workspace).</summary>
        public RecommendationRunCache RunCache { get; } = new();

        // ── parameter helpers ──────────────────────────────────────────────

        public double Buf => JsOps.Or(P.StrBuffer, 0);
        /// <summary><c>S.params.T2*(1+(S.params.strBuffer||0)/100)</c> (unrounded).</summary>
        public double EffT2 => P.T2 * (1 + Buf / 100);
        public string EffT2R => N(Js.Round(EffT2));
        public double BorderTol => JsOps.Or(P.BorderTolerance, 10);

        internal static string N(double d) => Js.NumberToString(d);
        /// <summary>JS string concatenation of a nullable number ("null" for null).</summary>
        internal static string N(double? d) => d.HasValue ? Js.NumberToString(d.Value) : "null";
        private static string Pct1(double v) => Js.ToFixed(v * 100, 1);

        // ── colours (decColor / decBg) ─────────────────────────────────────

        public static string DecColor(string d) => d switch
        {
            "Retain" => "#00873E", "Reduce Qty" => "#D97706", "Cancel" => "#DC2626", "Watch List" => "#D97706", "Cannot Validate" => "#6B7280", _ => "#6B7280"
        };

        public static string DecBg(string d) => d switch
        {
            "Retain" => "#DCFCE7", "Reduce Qty" => "#FFFBEB", "Cancel" => "#FEF2F2", "Watch List" => "#FFFBEB", "Cannot Validate" => "#F5F5F5", _ => "#F5F5F5"
        };

        public string StrText(double? artStr) => artStr != null ? N(Js.Round(artStr.Value)) + "d" : "—";

        // ── buildDecisionTooltip (line 6180) ───────────────────────────────

        public string Tooltip(Tool2LineResult r)
        {
            var pctTh = N(r.Percentile) + "th pct";
            if (r.Engine == "A")
            {
                var opt = string.IsNullOrEmpty(r.OptionType) ? "Article" : r.OptionType;
                if (r.Decision == Decisions.Cancel)
                    return r.Quartile != null ? opt + " ranks " + r.Quartile + " (" + pctTh + ") — bottom quartile. Confirmed slow seller. Do not raise PO."
                                              : Or(r.DecisionReason, "Confirmed slow seller. Do not raise PO.");
                if (r.Decision == Decisions.WatchList)
                    return r.Quartile != null ? opt + " ranks " + r.Quartile + " (" + pctTh + ") — lower-mid quartile. Escalate to Category Head."
                                              : Or(r.DecisionReason, "Escalate to Category Head before proceeding.");
                if (!string.IsNullOrEmpty(r.Quartile)) return opt + " ranks " + r.Quartile + " (" + pctTh + ") — healthy performer. PO supported.";
                return Or(r.DecisionReason, "Multi Concept MRP: insufficient BSR history — Auto Allow.");
            }
            var str = r.ArtStr != null ? N(Js.Round(r.ArtStr.Value)) + "d" : "N/A";
            var thr = EffT2R + "d (T=" + N(P.T2) + "d + " + N(Buf) + "% buffer)";
            if (r.Decision == Decisions.Retain) return "Retain: STR=" + str + " <= " + thr + ". PO within cover limit.";
            if (r.Decision == Decisions.Cancel) return "Cancel: STR=" + str + " > " + thr + ". Stock already exceeds threshold. Cancel PO.";
            if (r.Decision == Decisions.ReduceQty) return "Reduce Qty: STR=" + str + " > " + thr + ". Max retain=" + (r.RecQty > 0 ? Js.Fmt(r.RecQty) + " units" : "0") + ". Amend balance.";
            if (r.Decision == Decisions.WatchList) return "Watch List: Mix%=" + (r.MixPct != 0 ? Pct1(r.MixPct) + "%" : "N/A") + " or peer Q3. Escalate to Category Head.";
            return r.DecisionReason ?? "";
        }

        private static string Or(string? s, string d) => string.IsNullOrEmpty(s) ? d : s;

        // ── buildDetailedReason (line 6201) ────────────────────────────────

        private sealed class Segs
        {
            public readonly List<TextSegDto> L = new();
            public Segs T(string s) => Add(s, false);
            public Segs B(string s) => Add(s, true);
            private Segs Add(string s, bool bold)
            {
                if (s.Length == 0) return this;
                if (L.Count > 0 && L[^1].Bold == bold && L[^1].Color == null) L[^1].Text += s;
                else L.Add(new TextSegDto(s, bold));
                return this;
            }
        }

        public Tool2ReasonDto DetailedReason(Tool2LineResult r)
        {
            var d = new Tool2ReasonDto();
            var effR = EffT2R;
            var str = r.ArtStr != null ? N(Js.Round(r.ArtStr.Value)) + "d" : "N/A";
            var tB = " (T=" + N(P.T2) + "d + " + N(Buf) + "% buffer)";
            var cleanN = r.CleanPool is { Count: > 0 } cpl ? cpl.Count.ToString() : "?";
            string PdD() => r.Pdstr != null ? Pct1(r.Pdstr.Value) + "%/d" : "—";
            string AvgD() => r.PeerAvg != null ? Pct1(r.PeerAvg.Value) + "%/d" : "—";
            var bt = N(BorderTol);

            if (r.Engine == "B")
            {
                d.Kvs.Add(new KvDto("Engine", "B - Colour STR"));
                d.Kvs.Add(new KvDto("Op Stock", Js.Fmt(r.OpStock) + " units"));
                d.Kvs.Add(new KvDto("Colour Mix%", r.MixPct != 0 ? Pct1(r.MixPct) + "%" : "N/A"));
                d.Kvs.Add(new KvDto("STR", r.ArtStr != null ? N(Js.Round(r.ArtStr.Value)) + " days" : "N/A"));
                d.Kvs.Add(new KvDto("Threshold (T+Buffer)", effR + "d"));
                d.Kvs.Add(new KvDto("Window D", N(P.D) + " days"));
                if (r.RecQty > 0) d.Kvs.Add(new KvDto("Max Permissible Qty", Js.Fmt(r.RecQty) + " units"));
            }
            else if (r.Engine == "A")
            {
                d.Kvs.Add(new KvDto("Engine", "A - Multi-BSR"));
                d.Kvs.Add(new KvDto("BSR Source", Or(r.BsrLabel, "CY BSR")));
                d.Kvs.Add(new KvDto("Option Type", Or(r.OptionType, "—")));
                d.Kvs.Add(new KvDto("Quartile", Or(r.Quartile, "—")));
                d.Kvs.Add(new KvDto("Percentile", r.Percentile != null ? N(r.Percentile) + "th" : "N/A"));
                d.Kvs.Add(new KvDto("PDSTR %/day", r.Pdstr != null ? Pct1(r.Pdstr.Value) + "%" : "N/A"));
                d.Kvs.Add(new KvDto("Peer Avg %/day", r.PeerAvg != null ? Pct1(r.PeerAvg.Value) + "%" : "N/A"));
            }
            else d.Kvs.Add(new KvDto("Engine", "NEW - Not in UM/BSR"));

            var w = new Segs();
            var opt = r.OptionType ?? "null";
            var qPct = (r.Quartile ?? "null") + " (" + N(r.Percentile) + "th pct)";
            if (r.Engine == "A")
            {
                if (r.Decision == Decisions.Cancel)
                {
                    if (r.Quartile != null)
                        w.B(opt).T(" article ranks ").B(qPct).T(" in peer group of " + cleanN + " styles (MC+MRP+Flag, " + (r.BsrLabel ?? "null") + "). PDSTR = " +
                            (r.Pdstr != null ? Pct1(r.Pdstr.Value) + "%/d" : "see BSR") + ", peer avg = " + AvgD() + ", gap = " + (r.GapPct != null ? N(r.GapPct) + "%" : "—") +
                            ". Gap > " + bt + "% border zone threshold — block stands. ").B("Confirmed slow seller. Do not raise PO.");
                    else w.T(Or(r.DecisionReason, "Confirmed slow seller. Do not raise PO."));
                }
                else if (r.Decision == Decisions.WatchList)
                {
                    if (r.Quartile != null)
                    {
                        string wl;
                        if (r.InBorderZone && r.Quartile == "Q4") wl = "Q4 rank with gap " + N(r.GapPct) + "% ≤ " + bt + "% border zone — softened from Block.";
                        else if (r.Quartile == "Q3" && r.GapPct > 0) wl = "Q3 rank with gap " + N(r.GapPct) + "% > " + bt + "% — outside border zone.";
                        else if (r.Pdstr != null && r.PeerAvg != null && r.Pdstr >= r.PeerAvg) wl = r.Quartile + " rank but PDSTR " + Pct1(r.Pdstr.Value) + "%/d at/above peer avg — Category Head review.";
                        else wl = r.GapPct > 0 ? "Gap " + N(r.GapPct) + "% from peer avg" : "";
                        w.B(opt).T(" ranks ").B(qPct).T(" in peer group of " + cleanN + " styles (" + (r.BsrLabel ?? "null") + "). PDSTR = " + PdD() + ", peer avg = " + AvgD() + ". " +
                            (wl.Length > 0 ? wl + " " : "")).B("Category Head review required before proceeding.");
                    }
                    else w.T(Or(r.DecisionReason, "Escalate to Category Head before proceeding."));
                }
                else if (r.Decision == Decisions.Retain || r.Decision == "Auto Allow" || r.Decision == "Allow")
                {
                    if (!string.IsNullOrEmpty(r.Quartile))
                    {
                        var above = r.Pdstr != null && r.PeerAvg != null && r.Pdstr >= r.PeerAvg;
                        string al;
                        if (r.InBorderZone && r.Quartile == "Q3") al = "Q3 rank with gap " + N(r.GapPct) + "% ≤ " + bt + "% border zone — within tolerance, promoted from Watch List.";
                        else if (r.Quartile == "Q3" && above) al = "Q3 rank, PDSTR at/above peer avg — no escalation needed.";
                        else if (r.Quartile == "Q2" || r.Quartile == "Q1") al = r.Quartile + " rank — top half of peer group.";
                        else al = r.Quartile + " rank — healthy position relative to peers.";
                        w.B(opt).T(" ranks ").B(qPct).T(" in peer group of " + cleanN + " styles (" + (r.BsrLabel ?? "null") + "). PDSTR = " + PdD() + ", peer avg = " + AvgD() + ". " + al + " ")
                         .B("PO supported by BSR performance.");
                    }
                    else w.T(Or(r.DecisionReason, "Insufficient BSR history — Auto Allow."));
                }
                else w.T(Or(r.DecisionReason, "No reason available."));
            }
            else if (r.Engine == "B")
            {
                if (r.Decision == Decisions.Cancel)
                    w.T("Colour STR (").B(str).T(") exceeds ").B(effR + "d").T(tB + ". Existing stock + open POs already cover more than the threshold. Max Permissible Qty = 0. ").B("Cancel the full PO line.");
                else if (r.Decision == Decisions.ReduceQty)
                    w.T("Colour STR at full qty (").B(str).T(") exceeds ").B(effR + "d").T(tB + ". Partial quantity is viable. ").B("Max Permissible Qty = " + Js.Fmt(r.RecQty) + " units")
                     .T(" (= T×(1+Buffer%) × Daily Rate − existing stock at window start).");
                else if (r.Decision == Decisions.WatchList)
                    w.T("Colour Mix% (").B(r.MixPct != 0 ? Pct1(r.MixPct) + "%" : "N/A").T(") is below " + N(P.Mix) + "% threshold — broken/cut sizes likely. ").B("Escalate to Category Head before proceeding.");
                else
                    w.T("Colour STR (").B(str).T(") is within ").B(effR + "d").T(tB + ". Stock will be absorbed. ").B("PO healthy — no action needed.");
            }
            else w.T(Or(r.DecisionReason, "Not found in Universe Master or CY BSR — treated as new article."));
            d.Why = w.L;

            d.Caution = r.StrCaution is { Items.Count: > 0 } ? FillCautionTexts(r.StrCaution) : null;

            if (r.Engine == "B" && r.ArtStr != null)
            {
                var mixT = r.MixPct != 0 ? Pct1(r.MixPct) + "%" : "N/A";
                d.StrSteps = new List<List<TextSegDto>>
                {
                    new Segs().T("1. Opening Stock = ").B(Js.Fmt(r.OpStock)).T(" units (F1, no GRC double-count)").L,
                    new Segs().T("2. Rolling cover from Day 1 next month, " + N(P.D) + "-day window, F4 forecast x Colour Mix% (" + mixT + ")").L,
                    new Segs().T("3. Colour STR = Op Stock / Window Daily Rate x " + N(P.D) + " = ").B(str).L,
                    new Segs().T("4. Compare vs effective threshold=").B(effR + "d").T(tB + " -> ")
                        .B(r.ArtStr > EffT2 ? "BREACH -> " + r.Decision : "WITHIN LIMIT -> Retain").L
                };
            }

            if (r.Engine == "A" && !string.IsNullOrEmpty(r.Quartile))
            {
                var s = new Tool2ReasonStepsDto();
                var dc2 = r.Decision == Decisions.Cancel || r.Decision == "Block" ? "#DC2626" : r.Decision == Decisions.WatchList ? "#D97706" : "#00873E";
                s.DecisionColor = dc2;
                var above = r.Pdstr != null && r.PeerAvg != null && r.Pdstr >= r.PeerAvg;
                var pdstrPct2 = r.Pdstr != null ? Pct1(r.Pdstr.Value) + "%/d" : "N/A";
                var peerAvgPct2 = AvgD();
                var gapPct2 = r.GapPct != null ? (above ? "0.0% (at/above avg)" : N(r.GapPct) + "%") : "—";
                var peerN = r.PeerPool?.Count ?? 0;
                s.Step1 = new List<TileDto>
                {
                    new("BSR Options (MC×MRP)", N(r.TotalBsr is double tb && tb != 0 ? tb : 0), "#1A4FAF"),
                    new("WOF ≥" + N(P.Wof) + "w Pool", peerN.ToString(), "#0891B2"),
                    new("WOF This Article", r.Wof != null ? Js.ToFixed(r.Wof.Value, 0) + "w" : "—", "#0891B2"),
                    new("BSR Source", Or(r.BsrLabel, "—"), "#7C3AED"),
                    new("Option Type", Or(r.OptionType, "—"), "#7C3AED")
                };
                if (r.Pdstr != null)
                    s.Step2 = new List<TileDto>
                    {
                        new("This Article PDSTR", pdstrPct2, dc2),
                        new("Peer Avg PDSTR", peerAvgPct2, "#1A4FAF"),
                        new("Gap from Avg", gapPct2, above ? "#00873E" : dc2),
                        new("vs Mean", above ? "At/Above" : "Below", above ? "#00873E" : "#DC2626")
                    };
                if (r.Percentile != null)
                {
                    var n = r.CleanPool is { Count: > 0 } ? r.CleanPool.Count.ToString() : peerN > 0 ? peerN.ToString() : "?";
                    s.Step3Heading = "Step 3 — Quartile Ranking (" + N(r.Percentile) + "th pct — " + n + " styles)";
                    s.Step3MarkerPos = Math.Min(Math.Max(r.Percentile.Value, 2), 98);
                }
                if (r.Pdstr != null && r.PeerAvg != null && r.Pdstr < r.PeerAvg && (r.Quartile == "Q4" || r.Quartile == "Q3"))
                {
                    var bt2 = N(r.BorderTolUsed is double bu && bu != 0 ? bu : BorderTol);
                    var bz = r.InBorderZone;
                    s.Step4 = new Tool2BorderZoneDto
                    {
                        Threshold = "Threshold (Q4 & Q3) : gap ≤ " + bt2 + "%",
                        Gap = "This article gap      : " + gapPct2,
                        InZone = bz,
                        Result = bz ? "✓ IN BORDER ZONE" : "✗ OUTSIDE BORDER ZONE",
                        Message = bz
                            ? (r.Quartile == "Q3" ? "IN BORDER ZONE (gap ≤" + bt2 + "%) → Q3 softened: Watch List → Retain" : "IN BORDER ZONE (gap ≤" + bt2 + "%) → Q4 softened: Cancel → Watch List")
                            : "OUTSIDE BORDER ZONE (gap " + gapPct2 + " > " + bt2 + "%) → Hard decision stands",
                        Color = bz ? "#00873E" : "#DC2626",
                        Bg = bz ? "#DCFCE7" : "#FEF2F2"
                    };
                }
                if ((r.Decision == Decisions.Cancel || r.Decision == "Block") && r.Pdstr != null && r.Pdstr < 0.01 && r.Wof >= 4)
                    s.AbsBlockNote = "⚡ Absolute Block Override: PDSTR " + Pct1(r.Pdstr.Value) + "%/d < 1%/d threshold with WOF " +
                                     (r.Wof != null ? Js.ToFixed(r.Wof.Value, 0) : "?") + "w ≥ 4w. Cancel regardless of quartile.";
                d.Steps = s;
            }
            return d;
        }

        /// <summary>The texts of <c>_cautionBoxHtml</c> (also for cautions read back from a saved run).</summary>
        public static StrCautionDto FillCautionTexts(StrCautionDto sc)
        {
            foreach (var it in sc.Items)
            {
                it.Text = it.Label + " STR = " + N(it.Str) + "d for " + sc.DelMonth + ", above Tool 2 threshold of " + N(sc.EffT2) + "d.";
                it.Footer = "Window Start " + Js.Fmt(it.Cs.WinStockStart) + " ÷ Daily Rate " + N(it.Cs.Dr) + " = " + N(it.Str) + "d";
            }
            return sc;
        }

        // ── Multi Concept option helpers ───────────────────────────────────

        private static string ColOf(SheetRow r) => r.Col("Color", "Colour", "COLOR", "COLOUR", "colour", "color");
        private static string GmOf(SheetRow r) => r.Col("Generic Material", "generic material", "GENERIC MATERIAL");

        /// <summary>Port of <c>_recommendedActionA(r)</c>.</summary>
        public string RecommendedActionA(Tool2LineResult r)
        {
            if (r.Engine != "A") return "";
            var poolN = r.CleanPool?.Count ?? 0;
            var pdstrTxt = r.Pdstr != null ? Pct1(r.Pdstr.Value) + "%/day" : "—";
            var peerTxt = r.PeerAvg != null ? Pct1(r.PeerAvg.Value) + "%/day" : "—";
            var rankTxt = !string.IsNullOrEmpty(r.Quartile)
                ? "ranks " + r.Quartile + " (" + N(r.Percentile) + "th pct) of " + poolN + " peers (" + Or(r.BsrLabel, "BSR") + ")"
                : "insufficient peer data (" + (!string.IsNullOrEmpty(r.DecisionReason) && r.DecisionReason.Contains("WOF") ? "WOF gate not met" : "no peer pool") + ")";
            if (r.Decision == Decisions.Cancel)
                return "Cancel — " + rankTxt + ", PDSTR " + pdstrTxt + " vs peer avg " + peerTxt + ". Confirmed slow seller per Tool 1 peer-ranking logic — do not retain.";
            if (r.Decision == Decisions.WatchList)
                return "Watch List — " + rankTxt + ", PDSTR " + pdstrTxt + " vs peer avg " + peerTxt + ". Borderline per Tool 1 logic — Category Head review before retain/cut.";
            if (r.Decision == Decisions.Retain)
                return !string.IsNullOrEmpty(r.Quartile) ? "Retain — " + rankTxt + ", PDSTR " + pdstrTxt + ". Healthy seller per Tool 1 peer-ranking logic." : "Retain — " + rankTxt + ".";
            return r.Decision + " — " + rankTxt + ".";
        }

        private sealed record Cand(string Label, double Pdstr, string? Q);

        /// <summary>Port of <c>_rankedOptionsA(r, n)</c>.</summary>
        public List<string> RankedOptionsA(Tool2LineResult r, int n)
        {
            if (r.Engine != "A") return new List<string>();
            List<Cand> candidates;
            var cp = r.CleanPool ?? new List<SheetRow>();
            if (r.OptionType == "BOBs")
            {
                List<(string colour, double pdstr, string? q)> src =
                    r.SibRanked is { Count: > 0 } ? r.SibRanked.Select(s => (ColOf(s.Row), s.Pdstr, (string?)s.Q)).ToList()
                    : r.QualSibs is { Count: > 0 } ? r.QualSibs.Select(row => (ColOf(row), BsrPeerRank.GetPdstr(row), (string?)null)).ToList()
                    : r.Siblings is { Count: > 0 } ? r.Siblings.Select(row => (ColOf(row), BsrPeerRank.GetPdstr(row), (string?)null)).ToList()
                    : new();
                candidates = src.Select(c => new Cand(c.colour.Length > 0 ? c.colour : "—", c.pdstr, !string.IsNullOrEmpty(c.q) ? c.q : BsrPeerRank.Quartile(c.pdstr, cp))).ToList();
            }
            else
            {
                var pool = cp.Where(row => !(GmOf(row).Trim() == r.Gm.Trim() && ColOf(row).Trim() == r.Colour.Trim()));
                candidates = pool.Select(row => new Cand(Or(GmOf(row), "—") + "/" + Or(ColOf(row), "—"), BsrPeerRank.GetPdstr(row), null)).ToList();
            }
            JsSort.StableSort(candidates, (a, b) => b.Pdstr - a.Pdstr);
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var uniq = new List<Cand>();
            foreach (var c in candidates) if (c.Label.Length > 0 && seen.Add(c.Label)) uniq.Add(c);
            return uniq.Take(n).Select(c => c.Label + " (" + (!string.IsNullOrEmpty(c.Q) ? c.Q + ", " : "") + Pct1(c.Pdstr) + "%/day)").ToList();
        }

        /// <summary>Port of <c>_mcKeyB(r)</c>.</summary>
        public static string McKeyB(Tool2LineResult r)
        {
            var brand = Or(r.Info?.Brand, "NON-PL");
            var flag = Or(r.Info?.Flag, "—");
            return brand + "|" + (r.Info?.McCd ?? "") + "|" + (r.Info?.Mrp ?? "") + "|" + flag + "|" + r.Dm;
        }

        private Dictionary<string, List<(string Colour, double Str)>>? _colourStrByMc;

        /// <summary>Port of <c>_rankedColoursB(r, n)</c> (other colours of the same MC bucket × month, by STR).</summary>
        public List<string> RankedColoursB(Tool2LineResult r, int n)
        {
            if (r.Engine != "B") return new List<string>();
            _colourStrByMc ??= BuildColourStrByMc();
            var all = _colourStrByMc.TryGetValue(McKeyB(r), out var l) ? l : new List<(string Colour, double Str)>();
            var others = all.Where(x => x.Colour != r.Colour).ToList();
            List<string> Rank(double maxStr)
            {
                var f = others.Where(x => x.Str < maxStr).ToList();
                JsSort.StableSort(f, (a, b) => a.Str - b.Str);
                return f.Take(n).Select(x => x.Colour + " (" + Js.FmtD(x.Str) + ")").ToList();
            }
            var healthy = Rank(65);
            if (healthy.Count == 0) healthy = Rank(75 + 0.001);
            if (healthy.Count > 0) return healthy;
            if (others.Count > 0) return new List<string> { "No colour in this MC within 75d STR threshold" };
            return new List<string>();
        }

        private Dictionary<string, List<(string Colour, double Str)>> BuildColourStrByMc()
        {
            var m = new Dictionary<string, List<(string, double)>>(StringComparer.Ordinal);
            var seen = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
            foreach (var x in Results)
            {
                if (x.Engine != "B" || x.ArtStr == null) continue;
                var k = McKeyB(x);
                if (!m.TryGetValue(k, out var l)) { m[k] = l = new(); seen[k] = new HashSet<string>(StringComparer.Ordinal); }
                if (seen[k].Add(x.Colour)) l.Add((x.Colour, x.ArtStr.Value));
            }
            return m;
        }

        /// <summary>Port of <c>_rankedOptionsAny(r, n)</c>.</summary>
        public List<string> RankedOptionsAny(Tool2LineResult r, int n) =>
            r.Engine == "A" ? RankedOptionsA(r, n) : r.Engine == "B" ? RankedColoursB(r, Math.Min(n, 3)) : new List<string>();

        /// <summary>Port of <c>_bobsQ1OptionsArray(r, n)</c>.</summary>
        public List<string> BobsQ1OptionsArray(Tool2LineResult r, int n)
        {
            if (r.OptionType != "BOBs") return new List<string>();
            var cp = r.CleanPool ?? new List<SheetRow>();
            List<Cand> src =
                r.SibRanked is { Count: > 0 } ? r.SibRanked.Select(s => new Cand(Or(ColOf(s.Row), "—"), s.Pdstr, !string.IsNullOrEmpty(s.Q) ? s.Q : BsrPeerRank.Quartile(s.Pdstr, cp))).ToList()
                : r.QualSibs is { Count: > 0 } ? r.QualSibs.Select(row => new Cand(Or(ColOf(row), "—"), BsrPeerRank.GetPdstr(row), BsrPeerRank.Quartile(BsrPeerRank.GetPdstr(row), cp))).ToList()
                : r.Siblings is { Count: > 0 } ? r.Siblings.Select(row => new Cand(Or(ColOf(row), "—"), BsrPeerRank.GetPdstr(row), BsrPeerRank.Quartile(BsrPeerRank.GetPdstr(row), cp))).ToList()
                : new();
            var q1 = src.Where(c => c.Q == "Q1").ToList();
            JsSort.StableSort(q1, (a, b) => b.Pdstr - a.Pdstr);
            q1 = q1.Take(n).ToList();
            if (q1.Count == 0) return new List<string> { "No Q1-ranked sibling colourway available for this generic material." };
            return q1.Select(c => c.Label + " (Q1, " + Pct1(c.Pdstr) + "%/day)").ToList();
        }

        /// <summary>Port of <c>_allSiblingsWithPDSTR(r)</c>.</summary>
        public string AllSiblingsWithPdstr(Tool2LineResult r)
        {
            if (r.OptionType != "BOBs") return "";
            var list = r.QualSibs is { Count: > 0 } ? r.QualSibs : r.Siblings is { Count: > 0 } ? r.Siblings : new List<SheetRow>();
            if (list.Count == 0) return "No sibling colourway data available.";
            var cp = r.CleanPool ?? new List<SheetRow>();
            var items = list.Select(row => { var pd = BsrPeerRank.GetPdstr(row); return new Cand(Or(ColOf(row), "—"), pd, BsrPeerRank.Quartile(pd, cp)); }).ToList();
            JsSort.StableSort(items, (a, b) => b.Pdstr - a.Pdstr);
            return string.Join("; ", items.Select(c => c.Label + " (" + Or(c.Q, "—") + ", " + Pct1(c.Pdstr) + "%/day)"));
        }

        /// <summary>Port of <c>_q1PeerAvgPDSTR(r)</c>: a number (1 dp, in %) or "—".</summary>
        public object Q1PeerAvgPdstr(Tool2LineResult r)
        {
            var pool = r.CleanPool ?? new List<SheetRow>();
            if (pool.Count == 0) return "—";
            var q1 = pool.Select(BsrPeerRank.GetPdstr).Where(p => BsrPeerRank.Quartile(p, pool) == "Q1").ToList();
            if (q1.Count == 0) return "—";
            double s = 0; foreach (var v in q1) s += v;
            return Js.ParseFloat(Pct1(s / q1.Count));
        }

        /// <summary>Port of <c>_trueRepeatRecommendation(r)</c>.</summary>
        public string TrueRepeatRecommendation(Tool2LineResult r)
        {
            var b = RecommendedActionA(r);
            var pool = r.CleanPool ?? new List<SheetRow>();
            if (pool.Count == 0) return b;
            var filtered = pool.Where(row => !(GmOf(row).Trim() == r.Gm.Trim() && ColOf(row).Trim() == r.Colour.Trim())).ToList();
            if (filtered.Count == 0) return b;
            var best = filtered.Aggregate(filtered[0], (bb, row) => BsrPeerRank.GetPdstr(row) > BsrPeerRank.GetPdstr(bb) ? row : bb);
            return b + " Best-performing peer in this MC: GM " + Or(GmOf(best), "—") + " / colour " + Or(ColOf(best), "—") + " (" + Pct1(BsrPeerRank.GetPdstr(best)) + "%/day) — reference benchmark.";
        }

        // ── Category Head mapping ──────────────────────────────────────────

        private Dictionary<string, string>? _catHeadMap;

        private static string CatHeadEmailOfRow(SheetRow r) =>
            Js.ToJsString(JsOps.RawOr(r, "", "Category_Email_ID", "Category Email ID", "Category_Email", "Level 3")).Trim();

        private static string CatHeadKeyOfRow(SheetRow r) =>
            Js.N(JsOps.RawOr(r, "", "Brand Type", "Brand")) + "|" + Js.N(JsOps.RawOr(r, "", "MC CD", "MC_CD", "MC Cd", "MC")) + "|" + Js.N(JsOps.RawOr(r, "", "MRP"));

        /// <summary>Port of <c>_buildCatHeadMap()</c>: "brand|mc|mrp" → email, first mapping wins.</summary>
        public Dictionary<string, string> CatHeadMap()
        {
            if (_catHeadMap != null) return _catHeadMap;
            var map = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var r in Data.CatHeadMap ?? Array.Empty<SheetRow>())
            {
                var email = CatHeadEmailOfRow(r);
                if (email.Length == 0) continue;
                map.TryAdd(CatHeadKeyOfRow(r), email);
            }
            return _catHeadMap = map;
        }

        /// <summary>Port of <c>_catHeadEmailFor(map, brand, mcCd, mrp)</c>.</summary>
        public string? CatHeadEmailFor(object? brand, object? mcCd, object? mrp)
        {
            var m = CatHeadMap();
            if (m.Count == 0) return null;
            return m.TryGetValue(Js.N(brand) + "|" + Js.N(mcCd) + "|" + Js.N(mrp), out var e) ? e : null;
        }

        private static readonly Regex NameSplit = new("[._0-9]+", RegexOptions.Compiled);

        /// <summary>Port of <c>_catHeadDisplayName(email)</c>: "sukhdev.singh@x" → "Sukhdev Singh"; none → "Unassigned".</summary>
        public static string CatHeadDisplayName(string? email)
        {
            if (string.IsNullOrEmpty(email)) return "Unassigned";
            var local = email.Split('@')[0];
            var parts = NameSplit.Split(local).Where(p => p.Length > 0).ToList();
            if (parts.Count == 0) return email;
            return string.Join(" ", parts.Select(w => w[..1].ToUpperInvariant() + w[1..].ToLowerInvariant()));
        }

        /// <summary>The <c>catHeadFor(r)</c> of exportAssXLSX.</summary>
        public string CatHeadFor(Tool2LineResult r) => CatHeadDisplayName(CatHeadEmailFor(r.BrandOrNonPl, r.Info?.McCd, r.Mrp));

        /// <summary>Unassigned last, others by localeCompare.</summary>
        public static int CatHeadSort(string a, string b)
        {
            if (a == "Unassigned" && b != "Unassigned") return 1;
            if (b == "Unassigned" && a != "Unassigned") return -1;
            return Tool2Js.LocaleCompare(a, b);
        }

        /// <summary>Port of <c>_allCatHeadNamesForExport()</c>.</summary>
        public List<string> AllCatHeadNames()
        {
            var names = new List<string>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var r in Results) { var n = CatHeadFor(r); if (seen.Add(n)) names.Add(n); }
            JsSort.StableSort(names, (a, b) => CatHeadSort(a, b));
            return names;
        }

        /// <summary>Port of <c>_bucketFor(r)</c>.</summary>
        public static Tool2BucketDto? BucketFor(Tool2LineResult r)
        {
            if (r.Decision == Decisions.WatchList)
            {
                if (r.Engine == "A" && r.OptionType == "BOBs") return new Tool2BucketDto { Type = "WATCHLIST", Sub = "BoBs", Code = "WL-BoBs" };
                if (r.Engine == "A" && r.OptionType == "True Repeat") return new Tool2BucketDto { Type = "WATCHLIST", Sub = "True Repeat", Code = "WL-TR" };
                if (r.Engine == "B") return new Tool2BucketDto { Type = "WATCHLIST", Sub = "Single Concept", Code = "WL-SC" };
                return null;
            }
            if (r.Decision == Decisions.ReduceQty || r.Decision == Decisions.Cancel)
                return r.Engine == "B" ? new Tool2BucketDto { Type = "REDUCE/CANCEL", Sub = "Single Concept", Code = "RC-SC" }
                                       : new Tool2BucketDto { Type = "REDUCE/CANCEL", Sub = "Multi Concept", Code = "RC-MC" };
            return null;
        }

        // ── retention / Reduce-Cancel sheet helpers ───────────────────────

        /// <summary>Port of <c>_poRetentionInfo(r)</c>: (retainPct, acceptable) or null.</summary>
        public static (double RetainPct, bool Acceptable)? PoRetentionInfo(Tool2LineResult r)
        {
            if (r.Decision != Decisions.ReduceQty || r.RelQty == 0 || double.IsNaN(r.RelQty)) return null;
            var pct = r.RecQty / r.RelQty;
            return (pct, pct >= 0.90);
        }

        /// <summary>Port of <c>_poStatusFlag(r)</c>.</summary>
        public static string PoStatusFlag(Tool2LineResult r)
        {
            if (r.Decision == Decisions.Cancel) return "🚫 Cancel";
            var ret = PoRetentionInfo(r);
            if (ret != null)
                return ret.Value.Acceptable
                    ? "✅ Acceptable (" + N(Js.Round(ret.Value.RetainPct * 100)) + "% retained)"
                    : "⚠️ Action Needed (" + N(Js.Round(ret.Value.RetainPct * 100)) + "% retained)";
            return "—";
        }

        /// <summary>Port of <c>_multiConceptAttrRecommendation(r, n)</c>.</summary>
        public List<string> MultiConceptAttrRecommendation(Tool2LineResult r, int n)
        {
            if (r.Engine != "A") return new List<string>();
            var mc = r.Info?.McCd ?? ""; var mrp = r.Info?.Mrp ?? "";
            var brand = Or(r.Info?.Brand, "NON-PL"); var flag = r.Info?.Flag ?? "";
            if (mc.Length == 0) return new List<string>();
            var months = new List<string> { r.Dm };
            var selfCol = Js.N(r.Colour);
            List<RecARecDto> Dedupe(IEnumerable<RecARecDto> l)
            {
                var seen = new HashSet<string>(StringComparer.Ordinal);
                return l.Where(x => seen.Add(Js.N(x.Col))).ToList();
            }
            var res = Rec.RunACached(Ctx, mc, mrp, brand, flag, months, RunCache);
            if (res != null && string.IsNullOrEmpty(res.Err) && res.DimMode != "colour-only" && res.Recs is { Count: > 0 })
            {
                var q4 = Dedupe(res.Recs.Where(x => x.Q == "Q1" && Js.N(x.Col) != selfCol));
                var q3 = Dedupe(res.Recs.Where(x => x.Q == "Q2" && Js.N(x.Col) != selfCol));
                var picked = q4.Concat(q3).Take(n).ToList();
                if (picked.Count > 0)
                    return picked.Select(x => (x.Gm.Length > 0 ? x.Gm.ToUpperInvariant() + " / " : "") + x.Col.ToUpperInvariant() + " (" + N(x.Pdstr) + "%/day)").ToList();
            }
            var fallback = res != null && res.DimMode == "colour-only" ? res.Data : Rec.RunBCached(Ctx, mc, mrp, brand, flag, months, RunCache);
            if (fallback?.Rows == null) return new List<string>();
            var ranked = fallback.Rows.Where(x => Js.N(x.Col) != selfCol && !x.WatchList && x.MData.Count > 0 && x.MData[0].Str != null).ToList();
            JsSort.StableSort(ranked, (a, b) => a.MData[0].Str!.Value - b.MData[0].Str!.Value);
            return ranked.Take(n).Select(x =>
            {
                var cands = Rec.GetGMsForColour(Ctx, mc, mrp, brand, flag, x.Col);
                var gm = cands.Count > 0 ? cands[0] : null;
                var pd = x.Stk > 0 ? Js.ToFixed(x.S45 / 45 / x.Stk * 100, 1) : "0.0";
                return (!string.IsNullOrEmpty(gm) ? gm.ToUpperInvariant() + " / " : "") + x.Col.ToUpperInvariant() + " (" + pd + "%/day)";
            }).ToList();
        }

        /// <summary>Port of <c>_reduceCancelOptions(r, n)</c>.</summary>
        public List<string> ReduceCancelOptions(Tool2LineResult r, int n)
        {
            var ret = PoRetentionInfo(r);
            if (ret is { Acceptable: true }) return new List<string>();
            if (r.Engine == "B") return RankedColoursB(r, n);
            if (r.Engine == "A") return MultiConceptAttrRecommendation(r, n);
            return new List<string>();
        }

        /// <summary>Port of <c>_reduceCancelRecommendation(r, opts)</c>.</summary>
        public string ReduceCancelRecommendation(Tool2LineResult r, List<string>? opts)
        {
            var ret = PoRetentionInfo(r);
            if (ret is { Acceptable: true })
                return "✅ Acceptable as-is — only " + N(Js.Round((1 - ret.Value.RetainPct) * 100)) + "% cut (" + N(Js.Round(ret.Value.RetainPct * 100)) +
                       "% of released qty retained). No action needed; this PO can be left alone.";
            if (r.Engine == "B") return "Switch to a lower-STR colour in this MC — see Option columns.";
            if (r.Engine == "A")
            {
                if (opts is { Count: > 0 }) return "Switch to a BSR-verified Q1/Q2 alternative in this MC×MRP — see Option columns.";
                return TrueRepeatRecommendation(r);
            }
            return "—";
        }

        /// <summary>Port of <c>_formatColourOptions(arr, prefix)</c>.</summary>
        public static string FormatColourOptions(List<string>? arr, string? prefix)
        {
            if (arr == null || arr.Count == 0) return "Only colour in this MC — no comparison available.";
            if (arr.Count == 1 && arr[0].StartsWith("No colour", StringComparison.Ordinal)) return arr[0];
            return (string.IsNullOrEmpty(prefix) ? "Best colours in this MC by STR: " : prefix) + string.Join("; ", arr);
        }

        /// <summary>Port of <c>_aiDriverBucketT2(reason)</c> (index.html line 8923).</summary>
        public static string DriverBucket(string? reason)
        {
            var t = reason ?? "";
            bool M(string p) => Regex.IsMatch(t, p, RegexOptions.IgnoreCase);
            if (M("Absolute Block")) return "Absolute Block — confirmed dead stock";
            if (M("Colour Mix")) return "Colour Mix% below threshold";
            if (M("Max Permissible Qty = 0")) return "No headroom — stock already covers threshold";
            if (M("STR exceeds|exceeds T=")) return "Stock cover breach — STR exceeds threshold";
            if (M("BOBs fallthrough")) return "BOBs — PDSTR gap vs peer siblings";
            if (M("True Repeat")) return "True Repeat — PDSTR gap vs peer group";
            if (M("WOF.*gate|Insufficient selling history")) return "Insufficient selling history (WOF gate)";
            if (M("no peer pool|No peer pool")) return "No peer pool available";
            if (M("Not found in Universe Master")) return "Data/master mismatch";
            return "Within safe stock cover";
        }
    }
}
