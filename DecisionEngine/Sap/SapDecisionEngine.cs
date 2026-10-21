using System.Text.Json.Nodes;
using DecisionEngine.Common;
using DecisionEngine.Model;
using DecisionEngine.Tool1;

namespace DecisionEngine.Sap
{
    /// <summary>
    /// The SAP /decisions verdict for one PO (spec v1.4 §6): every item is decided by the Tool 1 engines exactly as a
    /// buyer's New Request (<see cref="ITool1DecisionEngine.Decide"/> — routing → Single Concept / Multi Concept / NEW) on the
    /// current data snapshot and today's Tool 1 ledger (read only: SAP decisions are never written into it), then mapped
    /// by <see cref="SapVerdictMap"/>. Pure and synchronous like every engine: "today" is a parameter.
    /// </summary>
    public interface ISapDecisionEngine
    {
        SapDecisionResult Decide(EngineData data, IReadOnlyList<JsonObject> todayLedger, string today,
                                 IReadOnlyList<SapItemInput> items, SapDecisionOptions options);
    }

    /// <summary>
    /// Default <see cref="ISapDecisionEngine"/>. Items of one PO with the same GM + colour + delivery month (the size variants
    /// of one generic article in IS-Retail) are decided <b>together</b> on their summed quantity, because the tool decides
    /// per article-colour; a REDUCE max_qty is then split over those items in proportion to their quantities (largest
    /// remainder, the item max_qty values add up to Max Permissible).
    /// </summary>
    public sealed class SapDecisionEngine : ISapDecisionEngine
    {
        public const int ReasonTextMax = 132;
        public const int SummaryTextMax = 255;

        private readonly ITool1DecisionEngine _tool1;

        public SapDecisionEngine(ITool1DecisionEngine tool1)
        {
            _tool1 = tool1;
        }

        public SapDecisionResult Decide(EngineData data, IReadOnlyList<JsonObject> todayLedger, string today,
                                        IReadOnlyList<SapItemInput> items, SapDecisionOptions options)
        {
            var res = new SapDecisionResult();
            res.MissingFiles = Tool1Router.MissingRequired(data);
            if (res.MissingFiles.Count > 0) return res;

            // 1. normalise every item
            var decisions = new List<SapItemDecision>();
            foreach (var it in items)
            {
                var d = new SapItemDecision
                {
                    PoItem = it.PoItem, Article = it.Article, GenericArticle = it.GenericArticle, ColourSent = it.Colour,
                    OrderQty = Math.Truncate(it.OrderQty)
                };
                var (gm, how) = SapInputNormalizer.ResolveGm(data, it.GenericArticle, it.Article);
                d.Gm = gm; d.GmMatch = how;
                var (colour, cHow) = SapInputNormalizer.ResolveColour(data, gm, it.Colour);
                d.Colour = colour; d.ColourMatch = cHow;
                d.Month = SapInputNormalizer.DeliveryMonth(it.DeliveryDate, today);
                if (gm == null)
                    d.Problem = "Article " + Display(it.GenericArticle ?? it.Article) + " is unknown to the tool (not in Article / Universe Master, F2 or BSR)";
                else if (colour == null)
                    d.Problem = "No colour sent for article " + Display(it.Article);
                else if (d.Month == null)
                    d.Problem = "No valid delivery_date for item " + it.PoItem;
                else if (d.OrderQty <= 0)
                    d.Problem = "order_qty must be > 0";
                decisions.Add(d);
            }

            // 2. decide each GM + colour + month group once, on the summed quantity
            foreach (var g in decisions.Where(d => d.Problem == null).GroupBy(d => (d.Gm!, d.Colour!, d.Month!)))
            {
                var group = g.ToList();
                var qty = group.Sum(x => x.OrderQty);
                Tool1Outcome o;
                try
                {
                    o = _tool1.Decide(data, todayLedger, today, 0, g.Key.Item1, g.Key.Item2, g.Key.Item3, qty, null);
                }
                catch (DecisionEngineException ex)
                {
                    foreach (var d in group) d.Problem = "The tool cannot validate this item: " + ex.Message;
                    continue;
                }
                var path = SapVerdictMap.Classify(o);
                if (path == SapOutcomePath.CannotValidate)
                {
                    foreach (var d in group)
                    {
                        FillEngine(d, o);
                        d.Problem = "The tool cannot validate this item: " + o.DecisionReason;
                    }
                    continue;
                }
                var rule = SapVerdictMap.Rule(path);
                var verdict = SapVerdictMap.VerdictFor(rule, options.BlockVerdict);
                var metrics = Metrics(o);
                var shares = verdict == SapVerdicts.Reduce ? SplitMax(metrics.HeadroomQty ?? 0, group.Select(x => x.OrderQty).ToList()) : null;
                for (var i = 0; i < group.Count; i++)
                {
                    var d = group[i];
                    FillEngine(d, o);
                    d.Decided = true;
                    d.GroupQty = qty;
                    d.Path = path;
                    d.Verdict = verdict;
                    d.VerdictCode = SapVerdicts.Code(verdict);
                    d.ReasonCode = rule.ReasonCode;
                    d.RuleId = rule.RuleId;
                    d.Metrics = metrics;
                    d.MaxQty = shares?[i];
                    d.ReasonText = Clip(ReasonText(path, o, data.Params, metrics, qty, group.Count), ReasonTextMax);
                }
            }

            // 3. items that cannot be decided
            var undecided = decisions.Where(d => !d.Decided).ToList();
            if (undecided.Count > 0)
            {
                var policyWatch = string.Equals((options.UnknownItemPolicy ?? "").Trim(), "WATCH", StringComparison.OrdinalIgnoreCase);
                if (undecided.Count == decisions.Count || !policyWatch)
                {
                    res.Items = decisions;
                    res.Rejected = true;
                    res.RejectMessage = Clip(string.Join("; ", undecided.Select(d => "Item " + d.PoItem + ": " + d.Problem)), 500);
                    return res;
                }
                var rule = SapVerdictMap.Rule(SapOutcomePath.CannotValidate);
                foreach (var d in undecided)
                {
                    d.Path = SapOutcomePath.CannotValidate;
                    d.Verdict = rule.Verdict;
                    d.VerdictCode = SapVerdicts.Code(rule.Verdict);
                    d.ReasonCode = rule.ReasonCode;
                    d.RuleId = rule.RuleId;
                    d.ReasonText = Clip(d.Problem ?? rule.ReasonCodeText, ReasonTextMax);
                    d.GroupQty = d.OrderQty;
                }
            }

            // 4. overall verdict, routing, summary
            res.Items = decisions;
            res.OverallVerdict = SapVerdicts.MostSevere(decisions.Select(d => d.Verdict));
            res.VerdictCode = SapVerdicts.Code(res.OverallVerdict);
            var netValue = items.Sum(i => i.NetValue ?? 0);
            res.BmHeadRequired = options.BmHeadRule?.Evaluate(res.OverallVerdict, netValue) ?? false;
            res.RouteTo = SapRoutes.For(res.OverallVerdict, res.BmHeadRequired);
            res.SummaryText = Summary(decisions);
            return res;
        }

        private static void FillEngine(SapItemDecision d, Tool1Outcome o)
        {
            d.Engine = o.Engine;
            d.OptionType = o.OptionType;
            d.EngineDecision = o.Engine == "B" ? (o.BFirst ?? o.B)?.Decision ?? o.Decision : o.Decision;
            d.EngineReason = o.Engine == "B" ? (o.BFirst ?? o.B)?.DecisionReason ?? o.DecisionReason : o.DecisionReason;
        }

        private static string Display(string? s) => string.IsNullOrWhiteSpace(s) ? "(none)" : SapInputNormalizer.StripLeadingZeros(s);

        /// <summary>The metrics of the spec from the engine's own figures (Single Concept: first run at the proposed quantity).</summary>
        public static SapItemMetrics Metrics(Tool1Outcome o)
        {
            var m = new SapItemMetrics();
            if (o.Engine == "B" && (o.BFirst ?? o.B) is { } b)
            {
                m.StockQty = Finite(b.OpStock);
                if (b.ExistingPO != null) m.OpenPoQty = Finite(b.ExistingPO.Values.Sum());
                if (b.Chain.Count > 1) m.PlanQty = Finite(Js.Round(b.Chain[b.D1Idx ?? 1].ColPlan));
                if (b.MaxPermQty is double mp) m.HeadroomQty = Finite(mp);
                if (b.ArtSTR is double s && b.WindowDemand is > 0) m.CoverDays = Finite(Js.Round(s));
            }
            else if (o.Engine == "A" && o.A is { Pdstr: double pd })
                m.SellThroughPct = Finite(Math.Round(pd * 100, 2, MidpointRounding.AwayFromZero));
            return m;
        }

        private static double? Finite(double v) => double.IsFinite(v) ? v : null;

        /// <summary>
        /// Splits <paramref name="max"/> over items in proportion to their quantities (floor, then the remaining units to the
        /// largest remainders, ties to the earlier item); never more than an item's own quantity.
        /// </summary>
        public static List<double> SplitMax(double max, IReadOnlyList<double> qtys)
        {
            var total = qtys.Sum();
            var m = Math.Max(0, Math.Floor(double.IsFinite(max) ? max : 0));
            if (qtys.Count == 1) return new List<double> { Math.Min(m, qtys[0]) };
            if (total <= 0) return qtys.Select(_ => 0.0).ToList();
            var exact = qtys.Select(q => m * q / total).ToList();
            var res = exact.Select(Math.Floor).ToList();
            var left = (int)(m - res.Sum());
            foreach (var i in Enumerable.Range(0, qtys.Count).OrderByDescending(i => exact[i] - res[i]).ThenBy(i => i))
            {
                if (left <= 0) break;
                if (res[i] < qtys[i]) { res[i] += 1; left--; }
            }
            return res.Select((v, i) => Math.Min(v, qtys[i])).ToList();
        }

        private static string Pct(double v) => Js.ToFixed(v, 1);

        /// <summary>A short reason (≤ 132 characters, spec items[].reason_text) from the engine's figures; the full engine reason is logged.</summary>
        public static string ReasonText(SapOutcomePath path, Tool1Outcome o, EngineParams p, SapItemMetrics m, double qty, int itemsInGroup)
        {
            var b = o.BFirst ?? o.B;
            var a = o.A;
            var effT = b?.EffectiveT is double e ? Js.NumberToString(Js.Round(e)) : Js.NumberToString(Js.Round(p.T1 * (1 + p.StrBuffer / 100)));
            var cover = m.CoverDays is double cd ? Js.NumberToString(cd) : "?";
            var pd = a?.Pdstr is double x ? Pct(x * 100) : "?";
            var gap = a?.GapPct is double gp ? Js.NumberToString(gp) : "?";
            var grp = itemsInGroup > 1 ? " for " + itemsInGroup + " items together (" + Js.Fmt(qty) + " pcs ordered)" : "";
            return path switch
            {
                SapOutcomePath.CoverWithinNorm => "Cover " + cover + " days within the " + effT + "-day norm" + grp,
                SapOutcomePath.NoPlanData => "No plan for this MC-MRP-Flag in the uploaded plan; cover not computed, allowed",
                SapOutcomePath.CoverAboveNorm => "Stock + pending + this PO cover " + cover + " days, above the " + effT + "-day norm; max " +
                                                 Js.Fmt(m.HeadroomQty ?? 0) + " pcs" + grp,
                SapOutcomePath.CoverNoHeadroom => "Stock + pending already above the " + effT + "-day cover norm; no headroom (max 0)" + grp,
                SapOutcomePath.MixBelowMin => "Colour mix " + Pct((b?.MixPct ?? 0) * 100) + "% below the " + Js.NumberToString(p.Mix) + "% minimum of its MC-MRP",
                SapOutcomePath.PeerQ1 or SapOutcomePath.PeerQ2 => "Ranks " + a?.Quartile + " in its peer group (sell-thru " + pd + "%/day): healthy seller",
                SapOutcomePath.PeerQ3WithinBorder => "Ranks Q3 (sell-thru " + pd + "%/day), at or near the peer average",
                SapOutcomePath.PeerQ3BeyondBorder => "Ranks Q3, sell-thru " + gap + "% below the peer average (beyond the " +
                                                     Js.NumberToString(a?.BorderTolUsed ?? p.BorderTolerance) + "% border zone)",
                SapOutcomePath.PeerQ4Softened => "Ranks Q4 but at or near the peer average (sell-thru " + pd + "%/day)",
                SapOutcomePath.BlockQ4 => "Ranks Q4, sell-thru " + gap + "% below the peer average: confirmed slow seller",
                SapOutcomePath.BlockAbsolute => "Sell-thru " + pd + "%/day below 1% after " + Js.NumberToString(a?.Wof ?? 0) + " weeks on floor: confirmed slow seller",
                SapOutcomePath.BobsSiblingsOk => "New colour: all sibling colours rank Q1/Q2",
                SapOutcomePath.BobsSiblingLow => "New colour: at least one sibling colour ranks Q3/Q4",
                SapOutcomePath.LowHistory => "Less than " + Js.NumberToString(p.Wof) + " weeks of selling history: allowed",
                SapOutcomePath.NoPeerPool => "No BSR peer group available: allowed",
                SapOutcomePath.NewArticle => "New article (not in Universe Master or BSR): allowed",
                _ => SapVerdictMap.Rule(path).ReasonCodeText
            };
        }

        /// <summary>
        /// summary_text (≤ 255): "Item 10: reduce to 500 - …; Item 20: hold - …" for the items that are not ALLOW, most severe
        /// first; "All N item(s) within the norms" when everything is allowed.
        /// </summary>
        public static string Summary(IReadOnlyList<SapItemDecision> items)
        {
            var flagged = items.Where(i => i.Verdict != SapVerdicts.Allow)
                               .OrderByDescending(i => SapVerdicts.Severity(i.Verdict)).ThenBy(i => i.PoItem).ToList();
            if (flagged.Count == 0) return "All " + items.Count + " item(s) within the norms";
            var parts = flagged.Select(i =>
                "Item " + i.PoItem + ": " + (i.Verdict == SapVerdicts.Reduce ? "reduce to " + Js.Fmt(i.MaxQty ?? 0) : i.Verdict.ToLowerInvariant()) +
                " - " + i.ReasonText);
            return Clip(string.Join("; ", parts), SummaryTextMax);
        }

        public static string Clip(string s, int max) => s.Length <= max ? s : s[..(max - 1)] + "…";
    }
}
