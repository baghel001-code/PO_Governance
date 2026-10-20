using DecisionEngine.Common;
using DecisionEngine.Model;
using DecisionEngine.Results.Analytics.Grc;

namespace DecisionEngine.Grc
{
    /// <summary>
    /// Port of index.html <c>renderGRCImpact()</c> (line 3218): resolves the controls ("Assess as of",
    /// View by, cutover, filters, drill period) exactly as the browser's selects behaved, runs the back-test
    /// and builds every table / card / bar of the GRC Impact card as data.
    /// </summary>
    public static class GrcImpactBuilder
    {
        private const string Green = "#0E9C8C", Amber = "#B4790E", Red = "#D8483C";

        /// <summary>
        /// "Assess as of" resolution: the requested period if it is in the file, else the saved
        /// S.params.grcAsOfPeriod if it is in the file, else the LAST native period. Null when the file has
        /// no periods (then the saved value is used unchanged).
        /// </summary>
        public static string? ResolveAsOf(EngineData data, string? requested)
        {
            var native = NativePeriods(data);
            if (native.Count == 0) return data.Params.GrcAsOfPeriod;
            if (!string.IsNullOrEmpty(requested) && native.Contains(requested)) return requested;
            var saved = data.Params.GrcAsOfPeriod;
            if (!string.IsNullOrEmpty(saved) && native.Contains(saved)) return saved;
            return native[^1];
        }

        public static List<string> NativePeriods(EngineData data) =>
            data.GetOrBuild("grc|nativePeriods", d => GrcJs.SortedPeriods(GrcLine.All(d).Select(r => r.Period)));

        public static GrcImpactDto Build(EngineData data, GrcImpactRequest req, IReadOnlyList<string> seasonCurrent,
                                         IReadOnlyList<string> seasonFallback, double nowMs,
                                         Func<GrcBacktestEngine, string?, string?, string, string?, GrcBacktestResultDto?>? compute = null)
        {
            compute ??= (e, d, c, v, b) => e.Compute(d, c, v, b);
            var native = NativePeriods(data);
            var asOf = ResolveAsOf(data, req.AsOfPeriod);
            var engine = new GrcBacktestEngine(data, seasonCurrent, seasonFallback, asOf, nowMs);

            var filterDiv = req.Division ?? "";
            var filterCatHead = req.CatHead ?? "";
            var filterBrand = req.Brand ?? "";
            var viewMode = req.ViewMode == "month" ? "month" : "native";
            var hasFilter = filterDiv.Length > 0 || filterCatHead.Length > 0 || filterBrand.Length > 0;

            var dto = new GrcImpactDto
            {
                HasGrcData = native.Count > 0,
                SeasonCurrent = seasonCurrent.ToList(),
                SeasonFallback = seasonFallback.ToList(),
                WofGate = engine.WofGate,
                PeerMin = engine.PeerMin,
            };
            var ctl = dto.Controls;
            ctl.AsOfOptions = native.ToList();
            ctl.AsOfPeriod = asOf;
            ctl.ViewMode = viewMode;
            ctl.CutoverOptions = native.ToList();
            ctl.Cutover = !string.IsNullOrEmpty(req.Cutover) && native.Contains(req.Cutover) ? req.Cutover : native.Count > 0 ? native[^1] : null;
            ctl.Division = filterDiv; ctl.CatHead = filterCatHead; ctl.Brand = filterBrand; ctl.DrillPeriod = req.DrillPeriod ?? "";

            var bt = compute(engine, NullIfEmpty(filterDiv), NullIfEmpty(filterCatHead), viewMode, NullIfEmpty(filterBrand));
            if (bt == null || bt.MonthsPresent.Count == 0)
            {
                dto.Message = "No GRC data to show" + (hasFilter ? " for this filter — try Clear Filters." : ".");
                return dto;
            }
            var periodLabel = bt.PeriodType == "week" ? "Week" : "Month";
            var periodLabelLower = periodLabel.ToLowerInvariant();
            dto.PeriodType = bt.PeriodType;
            dto.PeriodLabel = periodLabel;
            dto.Periods = bt.MonthsPresent.ToList();

            ctl.ViewModeVisible = data.GrcPeriodType == "week";

            // Dropdowns from the UNFILTERED universe; a selection survives only if still an option.
            var fullBt = hasFilter ? compute(engine, null, null, viewMode, null)! : bt;
            ctl.DivisionOptions = fullBt.AllDivisions.ToList();
            ctl.CatHeadOptions = fullBt.AllCatHeads.ToList();
            ctl.BrandOptions = fullBt.AllBrands.ToList();
            ctl.DrillOptions = bt.MonthsPresent.ToList();
            ctl.DrillAllLabel = $"All {periodLabelLower}s combined";
            ctl.Division = Preserve(ctl.DivisionOptions, filterDiv);
            ctl.CatHead = Preserve(ctl.CatHeadOptions, filterCatHead);
            ctl.Brand = Preserve(ctl.BrandOptions, filterBrand);
            var drillMonth = Preserve(ctl.DrillOptions, ctl.DrillPeriod);
            ctl.DrillPeriod = drillMonth;

            var cutover = ctl.Cutover ?? "";
            var cutoverIdx = bt.MonthsPresent.IndexOf(cutover);
            var beforeMonths = GrcJs.Slice(bt.MonthsPresent, 0, cutoverIdx);
            var withMonths = GrcJs.Slice(bt.MonthsPresent, cutoverIdx);

            // ── Legend ──
            dto.Legend = new()
            {
                new() { Label = "Q1 best", Color = "#2DD9C4" }, new() { Label = "Q2", Color = "#6FE0D0" },
                new() { Label = "Q3", Color = "#F2B84B" }, new() { Label = "Q4 worst", Color = "#FF6B5E" },
                new() { Label = "Absolute Block", Color = "#D8483C" },
            };

            // ── Quartile % by period ──
            dto.QuartileTitle = $"Quartile % by {periodLabelLower} — precise figures behind the chart above";
            var qt = dto.QuartileTable;
            qt.PeriodHeader = periodLabel;
            qt.Headers = GrcBuckets.Order.Append("Wrong Buy Value (Q4+Abs.Block)").ToList();
            foreach (var m in bt.MonthsPresent)
            {
                var b = bt.PerMonth[m];
                var total = GrcBuckets.Order.Aggregate(0d, (s, k) => s + b.Bucket(k));
                qt.Rows.Add(new GrcQuartileRowDto
                {
                    Period = m,
                    Cells = GrcBuckets.Order.Select(k => PctCell(k, total != 0 ? b.Bucket(k) / total * 100 : 0)).ToList(),
                    WrongValue = b.WrongValue, WrongValueText = GrcJs.Cr(b.WrongValue),
                });
            }
            var qtyTotals = GrcBuckets.Order.ToDictionary(k => k, k => bt.MonthsPresent.Aggregate(0d, (s, m) => s + bt.PerMonth[m].Bucket(k)));
            var qtyGrandTotal = GrcBuckets.Order.Aggregate(0d, (s, k) => s + qtyTotals[k]);
            var quartileWrongValueTotal = bt.MonthsPresent.Aggregate(0d, (s, m) => s + bt.PerMonth[m].WrongValue);
            qt.Total = new GrcQuartileRowDto
            {
                Period = "Total",
                Cells = GrcBuckets.Order.Select(k => PctCell(k, qtyGrandTotal != 0 ? qtyTotals[k] / qtyGrandTotal * 100 : 0)).ToList(),
                WrongValue = quartileWrongValueTotal, WrongValueText = GrcJs.Cr(quartileWrongValueTotal),
            };

            // ── Breakdown (Division / Category Head respect the drill period; Brand is always all periods) ──
            Dictionary<string, GrcAggDto?> divAgg, chAgg;
            Dictionary<string, double> divTrue, chTrue;
            if (drillMonth.Length > 0)
            {
                divAgg = bt.AllDivisions.Distinct().ToDictionary(d => d, d => bt.PerMonthDivision.GetValueOrDefault(drillMonth + "|||" + d));
                chAgg = bt.AllCatHeads.Distinct().ToDictionary(c => c, c => bt.PerMonthCatHead.GetValueOrDefault(drillMonth + "|||" + c));
                divTrue = bt.AllDivisions.Distinct().ToDictionary(d => d, d => bt.TrueTotalByMonthDivision.GetValueOrDefault(drillMonth + "|||" + d));
                chTrue = bt.AllCatHeads.Distinct().ToDictionary(c => c, c => bt.TrueTotalByMonthCatHead.GetValueOrDefault(drillMonth + "|||" + c));
            }
            else
            {
                divAgg = bt.PerDivision.ToDictionary(k => k.Key, k => (GrcAggDto?)k.Value);
                chAgg = bt.PerCatHead.ToDictionary(k => k.Key, k => (GrcAggDto?)k.Value);
                divTrue = bt.TrueTotalByDivision;
                chTrue = bt.TrueTotalByCatHead;
            }
            dto.Breakdown.Heading = (drillMonth.Length > 0 ? "Breakdown for " + drillMonth : $"Breakdown — all {periodLabelLower}s combined") + " · sorted worst-rate-first";
            foreach (var t in new[]
            {
                BreakdownTable("By Division", divAgg, bt.AllDivisions, divTrue),
                BreakdownTable("By Category Head", chAgg, bt.AllCatHeads, chTrue),
                BreakdownTable("By Brand Type", bt.PerBrand.ToDictionary(k => k.Key, k => (GrcAggDto?)k.Value), bt.AllBrands, bt.TrueTotalByBrand),
            })
                if (t != null) dto.Breakdown.Tables.Add(t);

            // ── Composition chart (100 % stacked, judgeable buckets only) ──
            foreach (var m in bt.MonthsPresent)
            {
                var b = bt.PerMonth[m];
                var total = GrcBuckets.Order.Aggregate(0d, (s, k) => s + b.Bucket(k));
                var bar = new GrcCompositionBarDto { Period = m, Total = total };
                if (total != 0)
                    foreach (var k in GrcBuckets.Order)
                    {
                        var v = b.Bucket(k);
                        var pct = v / total * 100;
                        bar.Segments.Add(new GrcCompositionSegmentDto
                        {
                            Bucket = k, Color = GrcBuckets.Colors[k], Qty = v, Share = v / total, Pct = pct,
                            Label = pct > 5 ? Js.ToFixed(pct, 0) + "%" : null,
                        });
                    }
                dto.Composition.Add(bar);
            }

            // ── Coverage bars ──
            dto.CoverageTitle = $"Coverage — % of each {periodLabelLower}'s buying that could be BSR-ranked at all";
            foreach (var m in bt.MonthsPresent)
            {
                var b = bt.PerMonth[m];
                var covPct = b.Total != 0 ? b.Judgeable / b.Total * 100 : 0;
                dto.Coverage.Add(new GrcCoverageBarDto
                {
                    Period = m, Pct = covPct, Text = Js.ToFixed(covPct, 0) + "%",
                    Color = covPct >= 80 ? Green : covPct >= 40 ? Amber : Red,
                });
            }

            // ── Before / onward group cards ──
            GroupStats GroupStatsOf(List<string> monthList)
            {
                var agg = GrcBuckets.Order.ToDictionary(k => k, _ => 0d);
                double judgeable = 0, trueTotal = 0, wrongValue = 0;
                foreach (var m in monthList)
                {
                    var b = bt.PerMonth[m];
                    foreach (var k in GrcBuckets.Order) agg[k] += b.Bucket(k);
                    judgeable += b.Judgeable;
                    trueTotal += bt.TrueTotalByMonth.GetValueOrDefault(m);
                    wrongValue += b.WrongValue;
                }
                var total = GrcBuckets.Order.Aggregate(0d, (s, k) => s + agg[k]);
                var wrong = agg[GrcBuckets.Q4] + agg[GrcBuckets.AbsBlock];
                var totalAll = monthList.Aggregate(0d, (s, m) => s + bt.PerMonth[m].Total);
                return new GroupStats(agg, total, trueTotal, wrong, total != 0 ? wrong / total * 100 : 0,
                    totalAll != 0 ? judgeable / totalAll * 100 : 0, wrongValue);
            }
            var beforeStats = beforeMonths.Count > 0 ? GroupStatsOf(beforeMonths) : null;
            var withStats = GroupStatsOf(withMonths);
            dto.BeforeCard = GroupCard("Before " + cutover, beforeStats, periodLabelLower);
            dto.WithCard = GroupCard(cutover + " onward", withStats, periodLabelLower);
            if (withStats.Coverage < 50)
                dto.CoverageWarning = $"⚠ Coverage for {cutover} onward is only {Js.ToFixed(withStats.Coverage, 0)}% — most likely a BSR reporting lag for the most recent arrivals, not a quality signal. Read this period's rate as directional, not precise.";

            // ── Ledger ──
            var lg = dto.Ledger;
            lg.PeriodHeader = periodLabel;
            lg.Headers = new() { periodLabel, "Total GRC Qty", "Multi Concept GRC Qty", "Coverage", "Judgeable Qty", "Wrong Buying Qty", "Rate", "Value" };
            double tTrue = 0, tTotal = 0, tJudge = 0, tWrong = 0, tWrongValue = 0;
            foreach (var m in bt.MonthsPresent)
            {
                var b = bt.PerMonth[m];
                var wrong = b.Bucket(GrcBuckets.Q4) + b.Bucket(GrcBuckets.AbsBlock);
                var rate = b.Judgeable != 0 ? wrong / b.Judgeable * 100 : 0;
                var covPct = b.Total != 0 ? b.Judgeable / b.Total * 100 : 0;
                var trueTotal = bt.TrueTotalByMonth.GetValueOrDefault(m);
                lg.Rows.Add(LedgerRow(m, trueTotal, b.Total, covPct, covPct >= 80 ? Green : Red, b.Judgeable, wrong, rate, b.WrongValue));
                tTrue += trueTotal; tTotal += b.Total; tJudge += b.Judgeable; tWrong += wrong; tWrongValue += b.WrongValue;
            }
            lg.Total = LedgerRow("Total", tTrue, tTotal, tTotal != 0 ? tJudge / tTotal * 100 : 0, null, tJudge, tWrong,
                tJudge != 0 ? tWrong / tJudge * 100 : 0, tWrongValue);

            // ── Notes ──
            if (bt.PeriodType == "month" && data.GrcPeriodType == "week")
            {
                dto.ShowMonthMappingNote = true;
                dto.MonthMappingNoteTitle = "How weeks are mapped to months (View by: Month):";
                dto.MonthMappingNoteParagraphs = new()
                {
                    "How weeks are mapped to months (View by: Month): each Monday–Sunday week is assigned to whichever calendar month owns the majority of its 7 days — not just the month its Monday falls in. Since a month is always at least 28 days, a single week can span at most 2 months, so this always gives a clear majority with no ties.",
                    "Worked example (real week from this calendar): the week of Mon 30-Mar-2026 → Sun 5-Apr-2026 has 2 days in March (30, 31) and 5 days in April (1, 2, 3, 4, 5). Under this rule, the entire week — all its GRC quantity — is counted under April, even though it starts in March, because more of its actual selling days fall there. The underlying WOF back-calculation and peer-pool ranking still happen at the exact week level regardless — only the display grouping changes here.",
                };
            }
            dto.FooterNote = $"Multi Concept MRP only (not in Universe Master). Quartile and Absolute Block use the exact same rules as live decisions today (S.params.wof={Js.NumberToString(engine.WofGate)}w gate, peer min={Js.NumberToString(engine.PeerMin)}). \"Judgeable\" excludes rows with no BSR match, WOF below the gate today, or (via WOF back-calculation) WOF that would have been below the gate as of that specific {periodLabelLower} — so an article isn't wrongly judged using floor-time it hadn't actually accumulated yet. Rate is computed on judgeable quantity only, so recent periods with lower coverage are still comparable rather than distorted by a shrinking base.";
            return dto;
        }

        private sealed record GroupStats(Dictionary<string, double> Agg, double Total, double TrueTotal, double Wrong, double Rate, double Coverage, double WrongValue);

        private static string? NullIfEmpty(string s) => s.Length == 0 ? null : s;

        /// <summary><c>_populateSelectPreserving</c>: the current value survives only if it is still an option.</summary>
        private static string Preserve(List<string> options, string current) =>
            current.Length == 0 || options.Contains(current) ? current : "";

        private static GrcPctCellDto PctCell(string bucket, double pct) =>
            new() { Bucket = bucket, Pct = pct, Text = Js.ToFixed(pct, 1) + "%", Color = GrcBuckets.Colors[bucket] };

        private static GrcGroupCardDto GroupCard(string title, GroupStats? stats, string periodLabelLower)
        {
            if (stats == null)
                return new GrcGroupCardDto { Title = title, IsEmpty = true, EmptyText = $"No {periodLabelLower}s before the cutover." };
            return new GrcGroupCardDto
            {
                Title = title,
                TrueTotal = stats.TrueTotal, TrueTotalText = GrcJs.Fmt(stats.TrueTotal),
                Total = stats.Total, TotalText = GrcJs.Fmt(stats.Total),
                Wrong = stats.Wrong, Rate = stats.Rate, WrongValue = stats.WrongValue,
                Coverage = stats.Coverage, CoverageText = $"Coverage: {Js.ToFixed(stats.Coverage, 0)}% of buying judgeable",
                Buckets = GrcBuckets.Order.Select(k => PctCell(k, stats.Total != 0 ? stats.Agg[k] / stats.Total * 100 : 0)).ToList(),
            };
        }

        private static GrcLedgerRowDto LedgerRow(string period, double trueTotal, double total, double cov, string? covColor,
                                                 double judgeable, double wrong, double rate, double wrongValue) => new()
        {
            Period = period,
            TrueTotal = trueTotal, TrueTotalText = GrcJs.Fmt(trueTotal),
            Total = total, TotalText = GrcJs.Fmt(total),
            Coverage = cov, CoverageText = Js.ToFixed(cov, 0) + "%", CoverageColor = covColor,
            Judgeable = judgeable, JudgeableText = GrcJs.Fmt(judgeable),
            Wrong = wrong, WrongText = GrcJs.Fmt(wrong),
            Rate = rate, RateText = Js.ToFixed(rate, 1) + "%",
            WrongValue = wrongValue, WrongValueText = GrcJs.Cr(wrongValue),
        };

        /// <summary>Port of the inner <c>breakdownTable(title, aggMap, allKeysSorted, trueTotalMap)</c>; null when it has no rows.</summary>
        private static GrcBreakdownTableDto? BreakdownTable(string title, Dictionary<string, GrcAggDto?> aggMap, List<string> allKeysSorted,
                                                            Dictionary<string, double> trueTotalMap)
        {
            var rowsData = new List<GrcBreakdownRowDto>();
            foreach (var k in allKeysSorted)
            {
                var agg = aggMap.GetValueOrDefault(k);
                var total = agg?.Total ?? 0;
                var judgeable = agg?.Judgeable ?? 0;
                var wrong = (agg?.Bucket(GrcBuckets.Q4) ?? 0) + (agg?.Bucket(GrcBuckets.AbsBlock) ?? 0);
                var wrongValue = agg?.WrongValue ?? 0;
                var trueTotal = trueTotalMap.GetValueOrDefault(k);
                if (!(trueTotal > 0 || total > 0)) continue;
                var rate = judgeable != 0 ? wrong / judgeable * 100 : 0;
                var cov = total != 0 ? judgeable / total * 100 : 0;
                var rateOfMc = total != 0 ? wrong / total * 100 : 0;
                rowsData.Add(BreakdownRow(k, trueTotal, total, judgeable, wrong, wrongValue, cov, rate, rateOfMc,
                    rate >= 20 ? Red : rate >= 10 ? Amber : Green));
            }
            if (rowsData.Count == 0) return null;
            rowsData = GrcJs.StableSort(rowsData, (a, b) => b.Rate - a.Rate);

            double tTrue = 0, tTotal = 0, tJudge = 0, tWrong = 0, tWrongValue = 0;
            foreach (var r in rowsData) { tTrue += r.TrueTotal; tTotal += r.Total; tJudge += r.Judgeable; tWrong += r.Wrong; tWrongValue += r.WrongValue; }
            var keyHeader = title.Contains("Division") ? "Division" : title.Contains("Brand") ? "Brand Type" : "Category Head";
            return new GrcBreakdownTableDto
            {
                Title = title,
                KeyHeader = keyHeader,
                Headers = new() { keyHeader, "Total GRC Qty", "Multi Concept GRC Qty", "Judgeable Qty", "Wrong Buy Qty", "Wrong Buy Value", "Coverage", "Wrong Buy % (of Judgeable)", "Wrong Buy % (of Multi Concept GRC Qty)" },
                Rows = rowsData,
                Total = BreakdownRow("Total", tTrue, tTotal, tJudge, tWrong, tWrongValue,
                    tTotal != 0 ? tJudge / tTotal * 100 : 0, tJudge != 0 ? tWrong / tJudge * 100 : 0, tTotal != 0 ? tWrong / tTotal * 100 : 0, null),
            };
        }

        private static GrcBreakdownRowDto BreakdownRow(string key, double trueTotal, double total, double judgeable, double wrong,
                                                       double wrongValue, double cov, double rate, double rateOfMc, string? color) => new()
        {
            Key = key,
            TrueTotal = trueTotal, TrueTotalText = GrcJs.Fmt(trueTotal),
            Total = total, TotalText = GrcJs.Fmt(total),
            Judgeable = judgeable, JudgeableText = GrcJs.Fmt(judgeable),
            Wrong = wrong, WrongText = GrcJs.Fmt(wrong),
            WrongValue = wrongValue, WrongValueText = GrcJs.Cr(wrongValue),
            Coverage = cov, CoverageText = Js.ToFixed(cov, 0) + "%",
            Rate = rate, RateText = Js.ToFixed(rate, 1) + "%",
            RateOfMc = rateOfMc, RateOfMcText = Js.ToFixed(rateOfMc, 1) + "%",
            RateColor = color,
        };
    }
}
