using System.Collections.Concurrent;
using Application.DTOs.Recommendation;
using Application.Interfaces.V1.Recommendation;
using Application.Services.V1.Engines.Common;
using Domain.Entities.Common;
using Domain.Entities.Engine;
using static Application.Services.V1.Recommendation.RecommendationData;

namespace Application.Services.V1.Recommendation
{
    /// <summary>
    /// The "View Recommendation" engine — port of the second &lt;script&gt; of index.html (v1.1.4,
    /// lines 9369–11512) minus the attribute-analysis screen. Pure: reads only the context it is given.
    /// </summary>
    public sealed partial class RecommendationEngine : IRecommendationEngine
    {
        // ── _coreColourSTRFast (line 10329) ───────────────────────────────────

        /// <summary>
        /// Port of <c>_coreColourSTRFast(opStock, mixPct, forecastByMon, f2RawMap, anchorMonth)</c>:
        /// pre-rolls stock from today's month to the anchor, builds a 13-month chain from the anchor,
        /// then the rolling D-day window from Day 1 of the month after the anchor → STR and the floored
        /// Max Permissible qty. Null when the anchor is not "MM.YYYY".
        /// </summary>
        public static CoreStrResultDto? CoreColourStrFast(RecommendationContext ctx, double opStock, double mixPct,
            JsObjectMap<double> forecastByMon, JsObjectMap<double>? f2RawMap, string anchorMonth)
        {
            var parts = MonthMath.ParseMmyyyy(anchorMonth);
            if (parts == null) return null;
            int dmM = parts.Value.M, dmY = parts.Value.Y;
            int currY = ctx.Y, currM = ctx.M, todayDate = ctx.D;
            var dmKey = MonthMath.Mmyyyy(dmY, dmM);
            var chain = new List<ChainStepDto>();
            var carry = PrevMonthCarry.Compute(f2RawMap, currY, currM, todayDate, ctx.P);

            var runStock = opStock;
            for (var i = 0; ; i++)
            {
                var ym = MonthMath.AddMonths(currY, currM, i);
                var mkey = MonthMath.Mmyyyy(ym.Y, ym.M);
                if (mkey == dmKey) break;
                if (i > 24) break;
                var ePO = PrevMonthCarry.EPOWithCarry(f2RawMap, mkey, carry);
                var fc = JsOps.Or(forecastByMon.Get(mkey), 0);
                double totalDays = MonthMath.DaysInMonth(ym.Y, ym.M);
                var remainingDays = i == 0 ? totalDays - todayDate : totalDays;
                var cp = fc * mixPct * (remainingDays / totalDays);
                runStock = Math.Max(runStock + ePO - cp, 0);
            }

            var currKey = MonthMath.Mmyyyy(currY, currM);
            for (var j = 0; j < 13; j++)
            {
                var ym2 = MonthMath.AddMonths(dmY, dmM, j);
                var mkey2 = MonthMath.Mmyyyy(ym2.Y, ym2.M);
                var ePO2 = PrevMonthCarry.EPOWithCarry(f2RawMap, mkey2, carry);
                var fc2 = JsOps.Or(forecastByMon.Get(mkey2), 0);
                double totalDays2 = MonthMath.DaysInMonth(ym2.Y, ym2.M);
                var isCurrMonth = mkey2 == currKey;
                var demandDays = isCurrMonth ? totalDays2 - todayDate : totalDays2;
                var colPlan = fc2 * mixPct * (demandDays / totalDays2);
                var openStock = j == 0 ? runStock : chain[j - 1].CloseStock;
                var closeStock = Math.Max(openStock + ePO2 - colPlan, 0);
                chain.Add(new ChainStepDto { Y = ym2.Y, M = ym2.M, MKey = mkey2, OpenStock = openStock, PoIn = ePO2, ColPlan = colPlan, CloseStock = closeStock });
            }

            var winDays = JsOps.Or(ctx.P.D, 60);
            const int d1Idx = 1;
            var winStockStart = chain.Count > d1Idx ? chain[d1Idx].OpenStock : 0;
            double budget = winDays, windowDemand = 0;
            for (var k = d1Idx; k < chain.Count && budget > 0; k++)
            {
                double days = MonthMath.DaysInMonth(chain[k].Y, chain[k].M);
                var take = Math.Min(days, budget);
                windowDemand += chain[k].ColPlan * (take / days);
                budget -= take;
            }
            var windowDailyRate = winDays > 0 && windowDemand > 0 ? windowDemand / winDays : 0;
            var remForecast = chain.Count > 0 ? Js.Round(chain[0].ColPlan) : 0;

            if (windowDemand == 0)
            {
                return new CoreStrResultDto
                {
                    Str = null, Dr = 0, Rec = 0, Action = "No data", OpStock = opStock, MixPct = Js.Round(mixPct * 1000) / 10,
                    WinStockStart = Js.Round(winStockStart), RemForecast = remForecast, Chain = chain, WinDays = winDays, D1Idx = d1Idx
                };
            }

            var artSTR = (winStockStart / windowDemand) * winDays;
            var T = JsOps.Or(ctx.P.T1, 75);
            var buf = JsOps.Or(ctx.P.StrBuffer, 0);
            var effectiveT = T * (1 + buf / 100);
            var maxPermQty = Math.Max(0, Math.Floor(effectiveT * windowDailyRate - winStockStart));
            var action = maxPermQty > 0 ? "Raise" : "Hold";
            return new CoreStrResultDto
            {
                Str = Js.Round(artSTR), Dr = Js.Round(windowDailyRate * 10) / 10, Rec = maxPermQty, Eff = Js.Round(effectiveT), EffExact = effectiveT,
                Action = action, OpStock = opStock, MixPct = Js.Round(mixPct * 1000) / 10, WinStockStart = Js.Round(winStockStart),
                RemForecast = remForecast, Chain = chain, WinDays = winDays, D1Idx = d1Idx
            };
        }

        public CoreStrResultDto? CoreColourStr(RecommendationContext ctx, double opStock, double mixPct, IReadOnlyDictionary<string, double> forecastByMon,
                                               IReadOnlyDictionary<string, double>? f2RawMap, string anchorMonth)
        {
            var f = new JsObjectMap<double>();
            foreach (var kv in forecastByMon) f[kv.Key] = kv.Value;
            JsObjectMap<double>? p = null;
            if (f2RawMap != null) { p = new JsObjectMap<double>(); foreach (var kv in f2RawMap) p[kv.Key] = kv.Value; }
            return CoreColourStrFast(ctx, opStock, mixPct, f, p, anchorMonth);
        }

        // ── _recQty (line 10178) ──────────────────────────────────────────────

        public RecQtyResultDto RecQty(EngineParams p, double stock, double pending, double f4Total, string month, double sales45, double totalSales45)
        {
            var T = JsOps.Or(p.T1, 75); var buf = JsOps.Or(p.StrBuffer, 0); var eff = T * (1 + buf / 100);
            var parts = month.Split('.');
            var m = MonthMath.ParseInt(parts[0]);
            var y = parts.Length > 1 ? MonthMath.ParseInt(parts[1]) : double.NaN;
            double dim = double.IsNaN(m) || double.IsNaN(y) ? double.NaN : MonthMath.DaysInMonth((int)y, (int)m);
            var mix = totalSales45 > 0 ? sales45 / totalSales45 : 0;
            var f4col = f4Total * mix;
            var dr = dim > 0 ? f4col / dim : 0;
            if (dr == 0) return new RecQtyResultDto { Rec = 0, Str = null, Dr = 0, Action = "No data" };
            var netStk = stock + pending;
            var str = netStk / dr;
            var rec = Math.Max(0, Js.Round(eff * dr - stock));
            return new RecQtyResultDto { Rec = rec, Str = Js.Round(str), Dr = Js.Round(dr * 10) / 10, Eff = Js.Round(eff), Action = str <= eff && rec > 0 ? "Raise" : "Hold" };
        }

        // ── _runB (line 10441) ────────────────────────────────────────────────

        public RecBResultDto RunB(RecommendationContext ctx, string mc, string mrp, string brand, string flag, IReadOnlyList<string> months)
        {
            var data = ctx.Data;
            var f1 = F1ByCol(data, mc, mrp, brand, flag);
            var f3 = F3ByCol(data, mc, mrp, brand, flag);
            var totalS = f3.ByCol.Values.Aggregate(0.0, (a, b) => a + b);
            var totalStk = f1.Values.Aggregate(0.0, (a, b) => a + b);
            var cols = f1.Keys.Concat(f3.ByCol.Keys).Distinct().Where(c => c.Length > 0).ToList();
            var T = JsOps.Or(ctx.P.T1, 75); var buf = JsOps.Or(ctx.P.StrBuffer, 0); var eff = T * (1 + buf / 100);

            var forecastByMon = BuildF4Map(data, mc, mrp, brand, flag);
            var f2OnlyCols = BuildF2RawMap(data, mc, mrp, brand, flag);
            var f2AllCols = MergeSessionLedgerIntoF2Map(ctx, DeepClone(f2OnlyCols), mc, mrp, brand, flag);

            var mixT = JsOps.Or(ctx.P.Mix, 2);
            var rows = cols.Select(col =>
            {
                var stk = f1.Get(col);
                var s45 = f3.ByCol.Get(col);
                var mixRatio = totalS > 0 ? s45 / totalS : 0;
                var mix = totalS > 0 ? Js.Round(s45 / totalS * 1000) / 10 : 0;
                var stkmix = totalStk > 0 ? Js.Round(stk / totalStk * 1000) / 10 : 0;
                var dr45 = s45 / 45;
                double? strRos = dr45 > 0 ? Js.Round(stk / dr45) : null;
                var f2Raw = f2AllCols.Get(col);
                var carry = PrevMonthCarry.Compute(f2Raw, ctx.Y, ctx.M, ctx.D, ctx.P);
                var watchList = totalS > 0 && mix < mixT;
                var watchReason = watchList
                    ? "Colour Mix % = " + Js.NumberToString(mix) + "% < " + Js.NumberToString(mixT) + "% threshold. Broken/cut size likely — recommendation not computed."
                    : null;
                var f2Only = f2OnlyCols.Get(col);
                var mdata = months.Select(mon =>
                {
                    var appr = Math.Max(0, (f2Raw == null ? 0 : JsOps.Or(f2Raw.Get(mon), 0)) - (f2Only == null ? 0 : JsOps.Or(f2Only.Get(mon), 0)));
                    if (watchList)
                        return new RecBMonthDto { Mon = mon, Pend = PrevMonthCarry.EPOWithCarry(f2Raw, mon, carry) - appr, Appr = appr, Rec = 0, Str = null, Dr = 0, Action = "Watch List" };
                    var cs = CoreColourStrFast(ctx, stk, mixRatio, forecastByMon, f2Raw, mon);
                    var pend = PrevMonthCarry.EPOWithCarry(f2Raw, mon, carry) - appr;
                    if (cs == null) return new RecBMonthDto { Mon = mon, Pend = pend, Appr = appr, Rec = 0, Str = null, Dr = 0, Action = "No data" };
                    return new RecBMonthDto { Mon = mon, Pend = pend, Appr = appr, Rec = cs.Rec, Str = cs.Str, Dr = cs.Dr, Eff = cs.Eff, Action = cs.Action };
                }).ToList();
                return new RecBRowDto { Col = col, Stk = stk, S45 = s45, Mix = mix, StkMix = stkmix, StrRos = strRos, WatchList = watchList, WatchReason = watchReason, MData = mdata };
            }).ToList();

            JsSort.StableSort(rows, (a, b) => b.Stk - a.Stk);
            return new RecBResultDto { Rows = rows, TotalS = totalS, Eff = Js.Round(eff) };
        }

        private static JsObjectMap<JsObjectMap<double>> DeepClone(JsObjectMap<JsObjectMap<double>> src)
        {
            var o = new JsObjectMap<JsObjectMap<double>>();
            foreach (var k in src.Keys) o[k] = src[k].Clone();
            return o;
        }

        // ── _runA (line 10593) ────────────────────────────────────────────────

        private sealed class AttrWork
        {
            public required RecAttrDto Dto { get; init; }
            public required AttrBucket Bucket { get; init; }
            public required JsObjectMap<double> AttrForecastByMon { get; init; }
            public List<(ColRankDto Dto, AttrColBucket Bucket)> ColRanked { get; set; } = new();
        }

        public RecAResultDto RunA(RecommendationContext ctx, string mc, string mrp, string brand, string flag, IReadOnlyList<string> months, string? overrideDim = null)
        {
            var data = ctx.Data;
            var dim = !string.IsNullOrEmpty(overrideDim) ? overrideDim : RecommendationData.AssessDim(data, mc, mrp, brand, flag);
            if (string.IsNullOrEmpty(dim))
                return new RecAResultDto { Err = "No entry in Attribute Relevance Master for this Brand×MC×MRP×Flag. Upload the file and add a row for this combination." };

            var T = JsOps.Or(ctx.P.T1, 75); var buf = JsOps.Or(ctx.P.StrBuffer, 0); var eff = T * (1 + buf / 100);

            if (dim.ToLowerInvariant() == "colour-only")
                return new RecAResultDto { DimMode = "colour-only", Data = RunB(ctx, mc, mrp, brand, flag, months) };

            var f3Bucket = F3ByCol(data, mc, mrp, brand, flag);
            var f1Bucket = F1ByCol(data, mc, mrp, brand, flag);
            var totalS = f3Bucket.ByCol.Values.Aggregate(0.0, (a, b) => a + b);
            var totalStk = f1Bucket.Values.Aggregate(0.0, (a, b) => a + b);

            var forecastByMon = BuildF4Map(data, mc, mrp, brand, flag);
            var f2AllCols = MergeSessionLedgerIntoF2Map(ctx, BuildF2RawMap(data, mc, mrp, brand, flag), mc, mrp, brand, flag);
            var f2ByAttr = MergeSessionLedgerIntoF2ByAttrMap(ctx, BuildF2RawMapByAttr(data, mc, mrp, brand, flag, dim), mc, mrp, brand, flag, dim);
            var optLookups = BuildOptionLookups(data, mc, mrp, brand, flag);
            var attrMap = F1F3ByAttr(data, mc, mrp, brand, flag, dim);

            if (attrMap.Count == 0)
                return new RecAResultDto { Err = "No GMs in this MC×MRP×Brand×Flag have a \"" + dim + "\" value filled in Article Master. Add " + dim + " values for the Generic Materials in this category." };

            var anchorMonth = months.Count > 0 && !string.IsNullOrEmpty(months[0]) ? months[0] : MonthMath.Mmyyyy(ctx.Y, ctx.M);

            var attrs = attrMap.Keys.Select(av =>
            {
                var d = attrMap[av];
                var smix = totalS > 0 ? Js.Round(d.S45 / totalS * 1000) / 10 : 0;
                var stkmix = totalStk > 0 ? Js.Round(d.Stk / totalStk * 1000) / 10 : 0;
                var attrF2Raw = new JsObjectMap<double>();
                var attrF2ByCol = f2ByAttr.Get(av) ?? new JsObjectMap<JsObjectMap<double>>();
                foreach (var colour in attrF2ByCol.Keys)
                {
                    var colF2 = attrF2ByCol[colour];
                    foreach (var mon in colF2.Keys) attrF2Raw[mon] = attrF2Raw.Get(mon) + colF2[mon];
                }
                var mixRatio = totalS > 0 ? d.S45 / totalS : 0;
                var cs = CoreColourStrFast(ctx, d.Stk, mixRatio, forecastByMon, attrF2Raw, anchorMonth);
                var str = cs != null && cs.Str != null ? cs.Str.Value : 9999;
                var attrForecastByMon = new JsObjectMap<double>();
                foreach (var mon in forecastByMon.Keys) attrForecastByMon[mon] = forecastByMon[mon] * mixRatio;
                var amdata = months.Select(mon => AnchorMonth(ctx, d.Stk, mixRatio, forecastByMon, attrF2Raw, mon)).ToList();
                var dto = new RecAttrDto
                {
                    Av = av, Str = Js.Round(str), SMix = smix, StkMix = stkmix, Dev = Js.Round((smix - stkmix) * 100) / 100,
                    Cols = d.Cols.Keys.ToDictionary(k => k, k => new AttrColDto { S45 = d.Cols[k].S45, Stk = d.Cols[k].Stk, Gms = d.Cols[k].Gms.ToList() }),
                    S45 = d.S45, Stk = d.Stk, AttrForecastByMon = attrForecastByMon.ToDictionary(),
                    AttrDR = cs != null ? cs.Dr : 0, AmData = amdata
                };
                return new AttrWork { Dto = dto, Bucket = d, AttrForecastByMon = attrForecastByMon };
            }).ToList();
            JsSort.StableSort(attrs, (a, b) => b.Dto.Dev - a.Dto.Dev);

            var bucketF2Raw = new JsObjectMap<double>();
            foreach (var colour in f2AllCols.Keys)
            {
                var colF2 = f2AllCols[colour];
                foreach (var mon in colF2.Keys) bucketF2Raw[mon] = bucketF2Raw.Get(mon) + colF2[mon];
            }
            var bucketCs = CoreColourStrFast(ctx, totalStk, 1, forecastByMon, bucketF2Raw, anchorMonth);
            var bucketSTR = new BucketStrDto
            {
                Str = bucketCs?.Str, Stk = totalStk, S45 = totalS, Pend = CumPO(bucketF2Raw, anchorMonth),
                Dr = bucketCs?.Dr ?? 0, RemForecast = bucketCs?.RemForecast ?? 0, WinStockStart = bucketCs?.WinStockStart ?? 0,
                Chain = bucketCs?.Chain ?? new List<ChainStepDto>(), D1Idx = bucketCs?.D1Idx ?? 1, AnchorMonth = anchorMonth,
                AmData = months.Select(mon => AnchorMonth(ctx, totalStk, 1, forecastByMon, bucketF2Raw, mon)).ToList()
            };

            foreach (var attr in attrs)
            {
                var ranked = attr.Bucket.Cols.Keys.Select(col =>
                {
                    var d = attr.Bucket.Cols[col];
                    var mixRatio = attr.Bucket.S45 > 0 ? d.S45 / attr.Bucket.S45 : 0;
                    var f2Raw = f2ByAttr.Get(attr.Dto.Av)?.Get(col);
                    var cs = CoreColourStrFast(ctx, d.Stk, mixRatio, attr.AttrForecastByMon, f2Raw, anchorMonth);
                    var str = cs != null && cs.Str != null ? cs.Str.Value : 9999;
                    return (new ColRankDto { Col = col, Str = Js.Round(str), Stk = d.Stk, S45 = d.S45, Gms = d.Gms.ToList(), Cs = cs }, d);
                }).ToList();
                JsSort.StableSort(ranked, (a, b) => a.Item1.Str - b.Item1.Str);
                attr.ColRanked = ranked;
                attr.Dto.ColRanked = ranked.Select(x => x.Item1).ToList();
            }

            var recs = new List<RecARecDto>();
            foreach (var tQ in new[] { "Q1", "Q2" })
            {
                foreach (var attr in attrs)
                {
                    foreach (var (cd, cb) in attr.ColRanked)
                    {
                        if (tQ == "Q2" && recs.Any(r => r.Col == cd.Col && r.Attr == attr.Dto.Av)) continue;
                        var candidates = cd.Gms.Count > 0 ? cd.Gms : GetGMsForColour(ctx, mc, mrp, brand, flag, cd.Col);
                        string? bestGm = null; double bestPDSTR = -1;
                        foreach (var gm in candidates)
                        {
                            var bsr = BsrQ(ctx, gm, cd.Col, mc, mrp, flag);
                            if (bsr == null || bsr.Q.ToUpperInvariant() != tQ) continue;
                            if (bsr.Pdstr > bestPDSTR) { bestPDSTR = bsr.Pdstr; bestGm = gm; }
                        }
                        if (bestGm == null) continue;
                        if (cd.Cs != null && cd.Cs.Action == "Hold") continue;

                        var optKey = Js.N(bestGm) + "|" + Js.N(cd.Col);
                        var mdata = months.Select(mon =>
                        {
                            var f2Raw = f2ByAttr.Get(attr.Dto.Av)?.Get(cd.Col);
                            var pend = f2Raw != null ? JsOps.Or(f2Raw.Get(mon), 0) : 0;
                            var optPend = JsOps.Or(optLookups.F2ByOpt.Get(optKey)?.Get(mon) ?? 0, 0);
                            var mixRatio = attr.Bucket.S45 > 0 ? cd.S45 / attr.Bucket.S45 : 0;
                            var rv = CoreColourStrFast(ctx, cd.Stk, mixRatio, attr.AttrForecastByMon, f2Raw, mon);
                            if (rv == null) return new RecAMonthDto { Mon = mon, Pend = pend, OptPend = optPend, Rec = 0, Str = null, Dr = 0, Action = "No data" };
                            return new RecAMonthDto
                            {
                                Mon = mon, Pend = pend, OptPend = optPend, Rec = rv.Rec, Str = rv.Str, Dr = rv.Dr, Eff = rv.Eff, EffExact = rv.EffExact,
                                Action = rv.Action, Stk = cd.Stk, MixPct = Js.Round(mixRatio * 1000) / 10, RemForecast = rv.RemForecast,
                                WinStockStart = rv.WinStockStart, Chain = rv.Chain, D1Idx = rv.D1Idx
                            };
                        }).ToList();

                        var colMixInAttr = attr.Bucket.S45 > 0 ? Js.Round(cd.S45 / attr.Bucket.S45 * 1000) / 10 : 0;
                        var colStockMixInAttr = attr.Bucket.Stk > 0 ? Js.Round(cd.Stk / attr.Bucket.Stk * 1000) / 10 : 0;
                        var colDev = Js.Round((colMixInAttr - colStockMixInAttr) * 100) / 100;
                        var optionStockQty = optLookups.StkByOpt.Get(optKey);
                        var hasAm = optLookups.AmInfo.TryGetValue(Js.N(bestGm), out var am);

                        recs.Add(new RecARecDto
                        {
                            Attr = attr.Dto.Av, AttrStr = attr.Dto.Str, AttrSmix = attr.Dto.SMix, AttrStkmix = attr.Dto.StkMix, AttrDev = attr.Dto.Dev,
                            AttrAmdata = attr.Dto.AmData,
                            Col = cd.Col, ColStr = cd.Str, ColMixInAttr = colMixInAttr, ColStockMixInAttr = colStockMixInAttr, ColDev = colDev,
                            OptionStockQty = optionStockQty, ColStockQty = cd.Stk, AttrStockQty = attr.Bucket.Stk,
                            GmDesc = hasAm ? OrEmpty(am.ArtDesc) : "", VendorCd = hasAm ? OrEmpty(am.Vendor) : "", VendorName = hasAm ? OrEmpty(am.VendorName) : "",
                            Gm = bestGm, Q = tQ, Pdstr = Js.Round(bestPDSTR * 1000) / 10, Priority = tQ == "Q1" ? "P1" : "P2", MData = mdata
                        });
                    }
                }
            }

            return new RecAResultDto
            {
                Dim = dim, Attrs = attrs.Select(a => a.Dto).ToList(), Recs = recs, Eff = Js.Round(eff), BucketStr = bucketSTR,
                F2ByAttr = f2ByAttr.Keys.ToDictionary(av => av, av => f2ByAttr[av].Keys.ToDictionary(c => c, c => f2ByAttr[av][c].ToDictionary())),
                OptLookups = new OptionLookupsDto
                {
                    StkByOpt = optLookups.StkByOpt.ToDictionary(),
                    F2ByOpt = optLookups.F2ByOpt.Keys.ToDictionary(k => k, k => optLookups.F2ByOpt[k].ToDictionary()),
                    AmInfo = optLookups.AmInfo.Keys.ToDictionary(k => k, k =>
                    {
                        var a = optLookups.AmInfo[k];
                        return new AmInfoDto { ArtDesc = Js.ToJsString(a.ArtDesc), Vendor = Js.ToJsString(a.Vendor), VendorName = Js.ToJsString(a.VendorName) };
                    })
                },
                AnchorMonth = anchorMonth, Months = months.ToList(), Mc = mc, Mrp = mrp, Brand = brand, Flag = flag
            };
        }

        private static string OrEmpty(object? v) => Js.Truthy(v) ? Js.ToJsString(v) : "";

        private static AnchorMonthDto AnchorMonth(RecommendationContext ctx, double stk, double mixRatio, JsObjectMap<double> forecastByMon, JsObjectMap<double> f2Raw, string mon)
        {
            var mCs = CoreColourStrFast(ctx, stk, mixRatio, forecastByMon, f2Raw, mon);
            var pend = JsOps.Or(f2Raw.Get(mon), 0);
            if (mCs == null) return new AnchorMonthDto { Mon = mon, Pend = pend, Str = null };
            return new AnchorMonthDto { Mon = mon, Pend = pend, Str = mCs.Str, Dr = mCs.Dr, WinStockStart = mCs.WinStockStart, Chain = mCs.Chain, D1Idx = mCs.D1Idx };
        }

        // ── _runACached / _runBCached (lines 7562 / 7570) ──────────────────────

        private static string CacheKey(string mc, string mrp, string brand, string flag, IReadOnlyList<string> months) =>
            Js.N(mc) + "|" + Js.N(mrp) + "|" + Js.N(brand) + "|" + Js.N(flag) + "|" + string.Join(",", months);

        public RecAResultDto? RunACached(RecommendationContext ctx, string mc, string mrp, string brand, string flag, IReadOnlyList<string> months, RecommendationRunCache? cache = null)
        {
            var key = CacheKey(mc, mrp, brand, flag, months);
            if (cache != null && cache.A.TryGetValue(key, out var hit)) return hit;
            RecAResultDto? res;
            try { res = RunA(ctx, mc, mrp, brand, flag, months); } catch (Exception) { res = null; }
            if (cache != null) cache.A[key] = res;
            return res;
        }

        public RecBResultDto? RunBCached(RecommendationContext ctx, string mc, string mrp, string brand, string flag, IReadOnlyList<string> months, RecommendationRunCache? cache = null)
        {
            var key = CacheKey(mc, mrp, brand, flag, months);
            if (cache != null && cache.B.TryGetValue(key, out var hit)) return hit;
            RecBResultDto? res;
            try { res = RunB(ctx, mc, mrp, brand, flag, months); } catch (Exception) { res = null; }
            if (cache != null) cache.B[key] = res;
            return res;
        }

        // ── _bsrQ (line 10030) ────────────────────────────────────────────────

        private sealed class BsrIndexes
        {
            public required Dictionary<string, List<SheetRow>> Cur { get; init; }
            public required Dictionary<string, List<SheetRow>> Fb { get; init; }
            public required Dictionary<string, SheetRow> OwnRow { get; init; }
            public ConcurrentDictionary<string, List<SheetRow>> Pools { get; } = new(StringComparer.Ordinal);
        }

        private static readonly System.Text.RegularExpressions.Regex SeasonStrip = new(@"[-_]", System.Text.RegularExpressions.RegexOptions.Compiled);
        private static readonly System.Text.RegularExpressions.Regex Ws = new(@"\s+", System.Text.RegularExpressions.RegexOptions.ECMAScript | System.Text.RegularExpressions.RegexOptions.Compiled);

        private static BsrIndexes GetBsrIndexes(EngineData data) => data.GetOrBuild("rec.bsrIndexes", d =>
        {
            var resolve = ResolveIndex.ResolveMap(d);
            var curSet = new HashSet<string>(d.Params.SeasonCurrent ?? new List<string>(), StringComparer.Ordinal);
            var fbSet = new HashSet<string>(d.Params.SeasonFallback ?? new List<string>(), StringComparer.Ordinal);
            string? SeasonOf(SheetRow r)
            {
                var g = JsOps.StrOr(r, "Generic Material", "GENERIC MATERIAL").Trim();
                if (!resolve.TryGetValue(g, out var info) || string.IsNullOrEmpty(info.Season)) return null;
                return Ws.Replace(SeasonStrip.Replace(info.Season.Trim().ToUpperInvariant(), ""), "");
            }
            var cur = new Dictionary<string, List<SheetRow>>(StringComparer.Ordinal);
            var fb = new Dictionary<string, List<SheetRow>>(StringComparer.Ordinal);
            var own = new Dictionary<string, SheetRow>(StringComparer.Ordinal);
            foreach (var r in d.Bsr ?? (IReadOnlyList<SheetRow>)Array.Empty<SheetRow>())
            {
                var bKey = Js.N(JsOps.RawOr(r, "", "MC CD", "MC_CD")) + "|" + Js.N(JsOps.RawOr(r, "", "MRP"));
                var s = SeasonOf(r);
                if (s != null && curSet.Contains(s)) { if (!cur.TryGetValue(bKey, out var l)) cur[bKey] = l = new(); l.Add(r); }
                if (s != null && fbSet.Contains(s)) { if (!fb.TryGetValue(bKey, out var l)) fb[bKey] = l = new(); l.Add(r); }
                var key = Js.N(JsOps.RawOr(r, "", "Generic Material", "GENERIC MATERIAL")) + "|" + Js.N(JsOps.RawOr(r, "", "Color", "Colour", "COLOR", "COLOUR"));
                own.TryAdd(key, r);
            }
            return new BsrIndexes { Cur = cur, Fb = fb, OwnRow = own };
        });

        private static double WofOf(SheetRow r)
        {
            var c = r.Col("WEEK ON FLOOR", "WOF");
            return Js.Sf(c.Length > 0 ? c : 0.0);
        }

        /// <summary>The BSR peer pool of a bucket (current seasons, fallback seasons when fewer than <c>peer</c>).</summary>
        public static List<SheetRow> BsrPool(EngineData data, string mc, string mrp, string flag)
        {
            var idx = GetBsrIndexes(data);
            var wofGate = JsOps.Or(data.Params.Wof, 8);
            var peerMin = JsOps.Or(data.Params.Peer, 5);
            var poolKey = Js.N(mc) + "|" + Js.N(mrp) + "|" + Js.N(flag);
            return idx.Pools.GetOrAdd(poolKey, _ =>
            {
                List<SheetRow> BuildPool(Dictionary<string, List<SheetRow>> bucketIdx)
                {
                    string nMC = Js.N(mc), nMRP = Js.N(mrp), nFlag = Js.N(flag);
                    var rows = bucketIdx.TryGetValue(nMC + "|" + nMRP, out var l) ? l : new List<SheetRow>();
                    return rows.Where(r =>
                    {
                        var rArt = Js.N(JsOps.RawOr(r, "", "Article Type", "BOGO/NON-BOGO", "Flag"));
                        var wof = WofOf(r);
                        return (nFlag.Length == 0 || rArt == nFlag || rArt == "") && wof >= wofGate;
                    }).ToList();
                }
                var pool = BuildPool(idx.Cur);
                if (pool.Count < peerMin)
                {
                    var fbPool = BuildPool(idx.Fb);
                    if (fbPool.Count > 0) pool = fbPool;
                }
                return pool;
            });
        }

        public BsrQDto? BsrQ(RecommendationContext ctx, string gm, string colour, string mc, string mrp, string flag) =>
            BsrQStatic(ctx.Data, gm, colour, mc, mrp, flag);

        /// <summary>Port of <c>_bsrQ(gm, colour, mc, mrp, flag)</c>: own BSR row (WOF ≥ gate) → PDSTR and its quartile in the peer pool.</summary>
        public static BsrQDto? BsrQStatic(EngineData data, string gm, string colour, string mc, string mrp, string flag)
        {
            var wofGate = JsOps.Or(data.Params.Wof, 8);
            var pool = BsrPool(data, mc, mrp, flag);
            if (data.Bsr == null || data.Bsr.Count == 0) return null;
            var idx = GetBsrIndexes(data);
            if (!idx.OwnRow.TryGetValue(Js.N(gm) + "|" + Js.N(colour), out var r)) return null;
            var wof = WofOf(r);
            if (wof < wofGate) return null;
            var pdstrRaw = r.Col("AVG PERDAY SELL THRU%", "AVG PERDAY SELL THRU %");
            if (pdstrRaw.Length == 0) pdstrRaw = "0";
            var pdstr = Js.Sf(pdstrRaw);
            if (pdstrRaw.Contains('%')) pdstr /= 100;
            var q = pool.Count > 0 ? BsrPeerRank.Quartile(pdstr, pool) : "Q3";
            return new BsrQDto { Wof = wof, Pdstr = pdstr, Q = q };
        }

        // ── _getGMsForColour (line 10162) ─────────────────────────────────────

        public List<string> GetGMsForColour(RecommendationContext ctx, string mc, string mrp, string brand, string flag, string colour)
        {
            var o = new List<string>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            void Scan(IReadOnlyList<SheetRow>? src)
            {
                if (src == null) return;
                foreach (var r in src)
                {
                    if (!Match(r, mc, mrp, brand, flag, colour)) continue;
                    var gm = Js.N(JsOps.RawOr(r, "", GmKeys));
                    if (gm.Length > 0 && seen.Add(gm)) o.Add(gm);
                }
            }
            Scan(ctx.Data.Bsr);
            Scan(ctx.Data.F2);
            return o;
        }

        // ── _strCaution (line 9935) ───────────────────────────────────────────

        public StrCautionDto? StrCaution(RecommendationContext ctx, string mc, string mrp, string brand, string flag, string colour, string gm, string? delMonth)
        {
            if (string.IsNullOrEmpty(delMonth)) return null;
            var data = ctx.Data;
            var T2 = JsOps.Or(ctx.P.T2, 100); var buf = JsOps.Or(ctx.P.StrBuffer, 0);
            var effT2 = T2 * (1 + buf / 100);
            var dim = RecommendationData.AssessDim(data, mc, mrp, brand, flag);
            var forecastByMon = BuildF4Map(data, mc, mrp, brand, flag);
            var items = new List<StrCautionItemDto>();
            var colKey = Js.N(colour); var gmKey = Js.N(gm);

            if (!string.IsNullOrEmpty(dim))
            {
                var attrMap = F1F3ByAttr(data, mc, mrp, brand, flag, dim);
                var amMap = AmAttrMap(data, dim);
                var av = amMap.Get(gmKey);
                if (!string.IsNullOrEmpty(av) && attrMap.TryGetValue(av, out var d))
                {
                    double totalS45 = 0; var f3Bucket = F3ByCol(data, mc, mrp, brand, flag);
                    foreach (var k in f3Bucket.ByCol.Keys) totalS45 += f3Bucket.ByCol[k];
                    var attrMixRatio = totalS45 > 0 ? d.S45 / totalS45 : 0;
                    var f2ByAttr = BuildF2RawMapByAttr(data, mc, mrp, brand, flag, dim);
                    var attrF2Raw = new JsObjectMap<double>();
                    var attrF2ByCol = f2ByAttr.Get(av) ?? new JsObjectMap<JsObjectMap<double>>();
                    foreach (var c in attrF2ByCol.Keys) foreach (var mon in attrF2ByCol[c].Keys) attrF2Raw[mon] = attrF2Raw.Get(mon) + attrF2ByCol[c][mon];
                    var attrCs = CoreColourStrFast(ctx, d.Stk, attrMixRatio, forecastByMon, attrF2Raw, delMonth);
                    if (attrCs != null && attrCs.Str != null && attrCs.Str > effT2)
                        items.Add(new StrCautionItemDto { Label = "Attribute (" + av + ")", Str = attrCs.Str.Value, Cs = attrCs });
                    if (d.Cols.TryGetValue(colKey, out var cd))
                    {
                        var attrForecastByMon = new JsObjectMap<double>();
                        foreach (var mon in forecastByMon.Keys) attrForecastByMon[mon] = forecastByMon[mon] * attrMixRatio;
                        var colMixRatio = d.S45 > 0 ? cd.S45 / d.S45 : 0;
                        var colF2Raw = f2ByAttr.Get(av)?.Get(colKey);
                        var colCs = CoreColourStrFast(ctx, cd.Stk, colMixRatio, attrForecastByMon, colF2Raw, delMonth);
                        if (colCs != null && colCs.Str != null && colCs.Str > effT2)
                            items.Add(new StrCautionItemDto { Label = "Colour (" + colour.ToUpperInvariant() + ")", Str = colCs.Str.Value, Cs = colCs });
                    }
                }
            }
            else
            {
                var f1B = F1ByCol(data, mc, mrp, brand, flag); var f3B = F3ByCol(data, mc, mrp, brand, flag);
                double totalS2 = 0; foreach (var k in f3B.ByCol.Keys) totalS2 += f3B.ByCol[k];
                var colStk = f1B.Get(colKey); var colS45 = f3B.ByCol.Get(colKey);
                var colMixRatio2 = totalS2 > 0 ? colS45 / totalS2 : 0;
                var f2AllCols = BuildF2RawMap(data, mc, mrp, brand, flag);
                var colF2 = f2AllCols.Get(colKey);
                var colCs2 = CoreColourStrFast(ctx, colStk, colMixRatio2, forecastByMon, colF2, delMonth);
                if (colCs2 != null && colCs2.Str != null && colCs2.Str > effT2)
                    items.Add(new StrCautionItemDto { Label = "Colour (" + colour.ToUpperInvariant() + ")", Str = colCs2.Str.Value, Cs = colCs2 });
            }
            if (items.Count == 0) return null;
            var res = new StrCautionDto { EffT2 = Js.Round(effT2), DelMonth = delMonth, Items = items };
            foreach (var it in items)
            {
                it.Text = it.Label + " STR = " + Js.NumberToString(it.Str) + "d for " + res.DelMonth + ", above Tool 2 threshold of " + Js.NumberToString(res.EffT2) + "d.";
                it.Footer = "Window Start " + Fmt(it.Cs.WinStockStart) + " ÷ Daily Rate " + Js.NumberToString(it.Cs.Dr) + " = " + Js.NumberToString(it.Str) + "d";
            }
            return res;
        }

        // ── months / dimensions ──────────────────────────────────────────────

        public string? AssessDim(EngineData data, string mc, string mrp, string brand, string flag) =>
            RecommendationData.AssessDim(data, mc, mrp, brand, flag);

        public List<string> BuildMonths(EngineData data, string mc, string mrp, string brand, string flag) =>
            RecommendationData.BuildMonths(data, mc, mrp, brand, flag);

        public List<string> DefaultMonths(IReadOnlyList<string> allMonths, string today)
        {
            var (ny, nm, _) = MonthMath.ParseIsoDate(today);
            var months = allMonths.Where(m =>
            {
                var p = m.Split('.');
                var mm = MonthMath.ParseInt(p[0]);
                var my = p.Length > 1 ? MonthMath.ParseInt(p[1]) : double.NaN;
                return my * 12 + mm >= ny * 12 + nm;
            }).Take(4).ToList();
            if (months.Count == 0) months = allMonths.Take(4).ToList();
            return months;
        }

        private static readonly string[] Dims = { "Style", "Fit", "Product Material", "Design" };

        public List<DimOptionDto> DimOverrideOptions(string? relevantDim, string? dimOverride)
        {
            var current = !string.IsNullOrEmpty(dimOverride) ? dimOverride : !string.IsNullOrEmpty(relevantDim) ? relevantDim : Dims[0];
            return Dims.Select(d => new DimOptionDto { Value = d, Label = d + (d == relevantDim ? " (Relevant)" : ""), Selected = d == current }).ToList();
        }
    }
}
