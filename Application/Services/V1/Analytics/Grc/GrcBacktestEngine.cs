using Application.DTOs.Analytics.Grc;
using Domain.Entities.Common;
using Domain.Entities.Engine;

namespace Application.Services.V1.Analytics.Grc
{
    /// <summary>One GRC line as the import produced it ({brand, gm, artdesc, colour, period, periodType, qty, value}).</summary>
    public sealed class GrcLine
    {
        public required SheetRow Row { get; init; }
        /// <summary>String(r.gm).</summary>
        public required string Gm { get; init; }
        public required object? Colour { get; init; }
        public required string Period { get; init; }
        public required double Qty { get; init; }
        /// <summary>r.value (null for the legacy monthly format / empty value cells).</summary>
        public required double? Value { get; init; }
        public required object? Brand { get; init; }

        public static GrcLine From(SheetRow r)
        {
            var v = r["value"];
            return new GrcLine
            {
                Row = r,
                Gm = Js.ToJsString(r["gm"]),
                Colour = r["colour"],
                Period = Js.ToJsString(r["period"]),
                Qty = Js.Num(r["qty"]),
                Value = v == null ? null : Js.Num(v),
                Brand = r["brand"],
            };
        }

        public static List<GrcLine> All(EngineData data) =>
            data.GetOrBuild("grc|lines", d => (d.Grc ?? Array.Empty<SheetRow>()).Select(From).ToList());
    }

    /// <summary>
    /// GRC back-test engine — ports of index.html <c>_computeGRCBacktest</c> (line 2950),
    /// <c>_traceArticleHistory</c> (line 3108) and <c>exportGRCRowDetail</c> (line 3571) on one data
    /// snapshot, one season selection and one "as of" reference date.
    /// </summary>
    public sealed class GrcBacktestEngine
    {
        private readonly EngineData _data;
        private readonly List<string> _seasonCurrent, _seasonFallback;
        private readonly double _asOfMs;

        /// <param name="asOfPeriod">S.params.grcAsOfPeriod ("WW.YYYY" / "MM.YYYY"); null → <paramref name="nowMs"/>.</param>
        /// <param name="nowMs">Current time, UTC epoch ms (JS <c>new Date()</c>).</param>
        public GrcBacktestEngine(EngineData data, IEnumerable<string> seasonCurrent, IEnumerable<string> seasonFallback, string? asOfPeriod, double nowMs)
        {
            _data = data;
            _seasonCurrent = seasonCurrent.ToList();
            _seasonFallback = seasonFallback.ToList();
            AsOfPeriod = asOfPeriod;
            _asOfMs = GrcPeriodMath.AsOfDate(asOfPeriod, data.GrcPeriodType, nowMs);
        }

        public string? AsOfPeriod { get; }
        /// <summary><c>S.params.wof||8</c>.</summary>
        public double WofGate => GrcJs.OrDefault(_data.Params.Wof, 8);
        /// <summary><c>S.params.peer||5</c>.</summary>
        public double PeerMin => GrcJs.OrDefault(_data.Params.Peer, 5);
        /// <summary><c>S.files.grcPeriodType||'month'</c>.</summary>
        public string PeriodType => string.IsNullOrEmpty(_data.GrcPeriodType) ? "month" : _data.GrcPeriodType!;

        private GrcHistoricalRanker NewRanker() =>
            new(_data, _seasonCurrent, _seasonFallback, WofGate, PeerMin, PeriodType, _asOfMs);

        private sealed class Ranked
        {
            public required string Bucket;
            public double? Pdstr;
            public double? Wof;
        }

        private Ranked RankToBucket(GrcHistoricalRanker ranker, string gm, object? colour, GrcResolveInfo info, string period)
        {
            var res = ranker.Rank(gm, colour, info.McCd, info.Mrp, info.Flag, period);
            if (res == null) return new Ranked { Bucket = GrcBuckets.NoPeerData };
            if (res.FuturePeriod) return new Ranked { Bucket = GrcBuckets.FuturePeriod };
            if (res.TooNew) return new Ranked { Bucket = GrcBuckets.TooNew };
            if (res.InsufficientPool) return new Ranked { Bucket = GrcBuckets.InsufficientPool };
            var absBlock = res.Pdstr < 0.01 && res.Wof >= WofGate;
            return new Ranked { Bucket = absBlock ? GrcBuckets.AbsBlock : res.Q, Pdstr = res.Pdstr, Wof = res.Wof };
        }

        private static bool IsJudgeable(string bucket) => !GrcBuckets.NonJudgeable.Contains(bucket);

        // ───────────────────────────────────────────────────────────────────

        /// <summary>
        /// Port of <c>_computeGRCBacktest(filterDiv, filterCatHead, viewMode, filterBrand)</c>. Null when no
        /// GRC file is loaded. Division / Category Head / Brand filters restrict which rows count at all.
        /// </summary>
        public GrcBacktestResultDto? Compute(string? filterDiv, string? filterCatHead, string? viewMode, string? filterBrand)
        {
            var lines = GrcLine.All(_data);
            if (lines.Count == 0) return null;
            filterDiv = string.IsNullOrEmpty(filterDiv) ? null : filterDiv;
            filterCatHead = string.IsNullOrEmpty(filterCatHead) ? null : filterCatHead;
            filterBrand = string.IsNullOrEmpty(filterBrand) ? null : filterBrand;

            var resolve = GrcLookups.ResolveMap(_data);
            var um = GrcLookups.UmSet(_data);
            var catHeadMap = GrcLookups.CatHeadMap(_data);
            var periodType = PeriodType;
            var useMonthView = viewMode == "month" && periodType == "week";
            var monthLabelCache = new Dictionary<string, string>(StringComparer.Ordinal);
            string DisplayPeriodOf(string p)
            {
                if (!useMonthView) return p;
                if (!monthLabelCache.TryGetValue(p, out var m)) monthLabelCache[p] = m = GrcPeriodMath.WeekPeriodToMonthLabel(p);
                return m;
            }

            var monthsPresent = GrcJs.SortedPeriods(lines.Select(r => DisplayPeriodOf(r.Period)));

            var ranker = NewRanker();
            var rankCache = new Dictionary<string, Ranked>(StringComparer.Ordinal);
            var flatRows = new List<GrcFlatRowDto>();
            var trueTotalRows = new List<(string DisplayPeriod, string Division, string CatHead, string Brand, double Qty)>();
            var allDivisions = new HashSet<string>(StringComparer.Ordinal);
            var allCatHeads = new HashSet<string>(StringComparer.Ordinal);
            var allBrands = new HashSet<string>(StringComparer.Ordinal);
            var divOrder = new List<string>(); var chOrder = new List<string>(); var brOrder = new List<string>();

            foreach (var r in lines)
            {
                if (!resolve.TryGetValue(r.Gm, out var info) || info.McCd.Length == 0) continue; // can't route
                var gmKey = r.Gm;
                var displayPeriod = DisplayPeriodOf(r.Period);

                var division = info.Div.Length > 0 ? info.Div : "Unclassified";
                var brand = info.Brand.Length > 0 ? info.Brand : Js.Truthy(r.Brand) ? Js.ToJsString(r.Brand) : "NON-PL";
                var catHeadEmail = GrcLookups.CatHeadEmailFor(catHeadMap, info.Brand.Length > 0 ? info.Brand : "NON-PL", info.McCd, info.Mrp);
                var catHead = GrcLookups.CatHeadDisplayName(catHeadEmail);
                if (allDivisions.Add(division)) divOrder.Add(division);
                if (allCatHeads.Add(catHead)) chOrder.Add(catHead);
                if (allBrands.Add(brand)) brOrder.Add(brand);
                if (filterDiv != null && division != filterDiv) continue;
                if (filterCatHead != null && catHead != filterCatHead) continue;
                if (filterBrand != null && brand != filterBrand) continue;

                // Every resolvable row counts toward the TRUE total (Single Concept included).
                trueTotalRows.Add((displayPeriod, division, catHead, brand, r.Qty));

                if (um.Contains(gmKey)) continue; // Single Concept — out of scope for quartile ranking

                var rankKey = gmKey + "|" + Js.ToJsString(r.Colour) + "|" + info.McCd + "|" + info.Mrp + "|" + info.Flag + "|" + r.Period;
                if (!rankCache.TryGetValue(rankKey, out var ranked))
                {
                    ranked = RankToBucket(ranker, gmKey, r.Colour, info, r.Period);
                    rankCache[rankKey] = ranked;
                }

                var mrpNum = Js.ParseFloat(info.Mrp.Replace(",", ""));
                if (double.IsNaN(mrpNum) || mrpNum == 0) mrpNum = 0;
                var value = r.Value ?? r.Qty * mrpNum;
                flatRows.Add(new GrcFlatRowDto
                {
                    Period = r.Period, DisplayPeriod = displayPeriod, Division = division, CatHead = catHead, Brand = brand,
                    Bucket = ranked.Bucket, Qty = r.Qty, Mrp = mrpNum, Value = value,
                });
            }

            Dictionary<string, GrcAggDto> AggregateBy(Func<GrcFlatRowDto, string> keyFn)
            {
                var o = new Dictionary<string, GrcAggDto>(StringComparer.Ordinal);
                foreach (var r in flatRows)
                {
                    var k = keyFn(r);
                    if (!o.TryGetValue(k, out var a)) o[k] = a = NewAgg();
                    a.Buckets[r.Bucket] = a.Bucket(r.Bucket) + r.Qty;
                    a.Total += r.Qty;
                    a.Value += r.Value;
                    if (IsJudgeable(r.Bucket))
                    {
                        a.Judgeable += r.Qty;
                        if (r.Bucket == GrcBuckets.Q4 || r.Bucket == GrcBuckets.AbsBlock) a.WrongValue += r.Value;
                    }
                }
                return o;
            }
            Dictionary<string, double> AggregateQtyBy(Func<(string DisplayPeriod, string Division, string CatHead, string Brand, double Qty), string> keyFn)
            {
                var o = new Dictionary<string, double>(StringComparer.Ordinal);
                foreach (var r in trueTotalRows) { var k = keyFn(r); o[k] = o.GetValueOrDefault(k) + r.Qty; }
                return o;
            }

            var perMonth = AggregateBy(r => r.DisplayPeriod);
            foreach (var m in monthsPresent) if (!perMonth.ContainsKey(m)) perMonth[m] = NewAgg();

            return new GrcBacktestResultDto
            {
                MonthsPresent = monthsPresent,
                PerMonth = perMonth,
                PerDivision = AggregateBy(r => r.Division),
                PerCatHead = AggregateBy(r => r.CatHead),
                PerBrand = AggregateBy(r => r.Brand),
                PerMonthDivision = AggregateBy(r => r.DisplayPeriod + "|||" + r.Division),
                PerMonthCatHead = AggregateBy(r => r.DisplayPeriod + "|||" + r.CatHead),
                PeriodType = useMonthView ? "month" : periodType,
                TrueTotalByMonth = AggregateQtyBy(r => r.DisplayPeriod),
                TrueTotalByDivision = AggregateQtyBy(r => r.Division),
                TrueTotalByCatHead = AggregateQtyBy(r => r.CatHead),
                TrueTotalByBrand = AggregateQtyBy(r => r.Brand),
                TrueTotalByMonthDivision = AggregateQtyBy(r => r.DisplayPeriod + "|||" + r.Division),
                TrueTotalByMonthCatHead = AggregateQtyBy(r => r.DisplayPeriod + "|||" + r.CatHead),
                AllDivisions = GrcJs.SortOrdinal(divOrder),
                AllCatHeads = GrcJs.StableSort(chOrder, (a, b) => a == "Unassigned" ? 1 : b == "Unassigned" ? -1 : GrcJs.LocaleCompare(a, b)),
                AllBrands = GrcJs.SortOrdinal(brOrder),
                FlatRows = flatRows,
            };
        }

        private static GrcAggDto NewAgg()
        {
            var a = new GrcAggDto();
            foreach (var b in GrcBuckets.All) a.Buckets[b] = 0;
            return a;
        }

        // ───────────────────────────────────────────────────────────────────

        /// <summary>
        /// Port of <c>_traceArticleHistory(gm)</c> + the display parts of <c>renderArticleTrace()</c>.
        /// </summary>
        public GrcArticleTraceDto Trace(string? gmInput)
        {
            var gm = (gmInput ?? "").Trim();
            if (gm.Length == 0) return new GrcArticleTraceDto { IsBlank = true, AsOfPeriod = AsOfPeriod };
            var lines = GrcLine.All(_data);
            if (lines.Count == 0) return new GrcArticleTraceDto { Error = "No GRC file loaded.", AsOfPeriod = AsOfPeriod };
            var resolve = GrcLookups.ResolveMap(_data);
            var gmKey = gm;
            if (!resolve.TryGetValue(gmKey, out var info) || info.McCd.Length == 0)
                return new GrcArticleTraceDto { Error = $"GM {gmKey} not found in Article Master (or has no MC CD) — can't resolve it to a bucket.", AsOfPeriod = AsOfPeriod };

            var wofGate = WofGate;
            var isSingleConcept = GrcLookups.UmSet(_data).Contains(gmKey);
            var rows = lines.Where(r => r.Gm.Trim() == gmKey).ToList();
            if (rows.Count == 0)
                return new GrcArticleTraceDto { Error = $"GM {gmKey} was resolved via Article Master but has no rows in the GRC file — it wasn't actually bought in this period.", AsOfPeriod = AsOfPeriod };

            var ranker = NewRanker();
            var weekRows = rows.Select(r =>
            {
                var w = new GrcTraceWeekRowDto
                {
                    Period = r.Period, Colour = Js.ToJsString(r.Colour), Qty = r.Qty, QtyText = GrcJs.Fmt(r.Qty), Value = r.Value,
                };
                if (isSingleConcept)
                {
                    w.Bucket = GrcBuckets.SingleConcept;
                    w.Detail = "This GM is in Universe Master, so it never enters the Multi Concept quartile ranking at all.";
                    return Finish(w);
                }
                var res = ranker.Rank(gmKey, r.Colour, info.McCd, info.Mrp, info.Flag, r.Period);
                if (res == null)
                {
                    w.Bucket = GrcBuckets.NoPeerData;
                    w.Detail = "This GM+Colour was not found in the CY BSR file at all — no ranking possible regardless of timing.";
                }
                else if (res.FuturePeriod)
                {
                    w.Bucket = GrcBuckets.FuturePeriod;
                    w.WeeksElapsed = res.WeeksElapsed;
                    w.Detail = "This week comes after the \"Assess as of\" date you've set — treated as not-yet-happened from that vantage point, so it's excluded rather than judged.";
                }
                else if (res.TooNew)
                {
                    w.Bucket = GrcBuckets.TooNew;
                    w.EstimatedWof = res.EstimatedWof; w.WofNow = res.WofNow; w.WofGate = res.WofGate;
                    w.Detail = $"Back-calculated WOF at this week was {Js.ToFixed(res.EstimatedWof, 1)}w (today's WOF {Js.NumberToString(res.WofNow)}w, gate is {Js.NumberToString(res.WofGate)}w) — hadn't accumulated enough floor-time yet at this point.";
                }
                else if (res.InsufficientPool)
                {
                    w.Bucket = GrcBuckets.InsufficientPool;
                    w.PoolSize = res.PoolSize; w.PeerMin = res.PeerMin;
                    w.Detail = $"Only {res.PoolSize} peer(s) had themselves cleared the WOF gate by this week (need {Js.NumberToString(res.PeerMin)}) — too few to trust a fair ranking at this point in time.";
                }
                else
                {
                    var absBlock = res.Pdstr < 0.01 && res.Wof >= wofGate;
                    w.Bucket = absBlock ? GrcBuckets.AbsBlock : res.Q;
                    w.Pdstr = res.Pdstr; w.Wof = res.Wof; w.PoolSize = res.PoolSize;
                    w.Detail = $"Ranked {Js.ToFixed(res.Pdstr * 100, 2)}%/day sell-through against {res.PoolSize} qualified peers at this week → {w.Bucket}.";
                }
                return Finish(w);
            }).ToList();

            weekRows = GrcJs.StableSort(weekRows, (a, b) => GrcJs.ComparePeriods(a.Period, b.Period));

            return new GrcArticleTraceDto
            {
                AsOfPeriod = AsOfPeriod,
                Gm = gmKey,
                Division = info.Div.Length > 0 ? info.Div : "Unclassified",
                McCd = info.McCd,
                McDesc = info.McDesc,
                Mrp = info.Mrp,
                Brand = info.Brand.Length > 0 ? info.Brand : "NON-PL",
                Flag = info.Flag,
                FlagText = info.Flag.Length > 0 ? info.Flag : "—",
                IsSingleConcept = isSingleConcept,
                SingleConceptNote = isSingleConcept ? "⚠ Single Concept — out of scope for quartile ranking" : null,
                WeekRows = weekRows,
            };

            static GrcTraceWeekRowDto Finish(GrcTraceWeekRowDto w)
            {
                w.BucketColor = GrcBuckets.TraceColors.TryGetValue(w.Bucket, out var c) ? c : GrcBuckets.DefaultTraceColor;
                if (w.Bucket == GrcBuckets.TooNew)
                    w.Extra = $"Back-calc WOF: {Js.ToFixed(w.EstimatedWof!.Value, 1)}w (today's WOF {Js.NumberToString(w.WofNow!.Value)}w, gate {Js.NumberToString(w.WofGate!.Value)}w)";
                else if (w.Bucket == GrcBuckets.InsufficientPool)
                    w.Extra = $"Peer pool: {Js.NumberToString(w.PoolSize!.Value)} (need {Js.NumberToString(w.PeerMin!.Value)})";
                else if (w.PoolSize != null)
                    w.Extra = $"PDSTR: {Js.ToFixed(w.Pdstr!.Value * 100, 2)}%/day · WOF: {Js.NumberToString(w.Wof!.Value)}w · Peer pool: {Js.NumberToString(w.PoolSize.Value)}";
                return w;
            }
        }

        // ───────────────────────────────────────────────────────────────────

        /// <summary>
        /// Port of <c>exportGRCRowDetail()</c>: one row per GRC line explaining where it landed. Null when no
        /// GRC file is loaded (browser toast "No GRC file loaded").
        /// </summary>
        public GrcRowDetailExportDto? RowDetail(double nowMs)
        {
            var lines = GrcLine.All(_data);
            if (lines.Count == 0) return null;
            var resolve = GrcLookups.ResolveMap(_data);
            var um = GrcLookups.UmSet(_data);
            var catHeadMap = GrcLookups.CatHeadMap(_data);
            var ranker = NewRanker();
            var rankCache = new Dictionary<string, Ranked>(StringComparer.Ordinal);
            var rows = new List<List<object?>>(lines.Count);

            foreach (var r in lines)
            {
                resolve.TryGetValue(r.Gm, out var info);
                var inUm = um.Contains(r.Gm);
                object division = "—", mcCd = "—", mrp = "—", flag = "—", catHead = "—", bsrFound = "—", pdstr = "—", wof = "—";
                string bucket, judgeable = "No", wrong = "No";

                if (info == null || info.McCd.Length == 0)
                {
                    bucket = GrcBuckets.NotResolved;
                }
                else
                {
                    division = info.Div.Length > 0 ? info.Div : "Unclassified";
                    mcCd = info.McCd; mrp = info.Mrp; flag = info.Flag.Length > 0 ? info.Flag : "—";
                    object brandForMap = info.Brand.Length > 0 ? info.Brand : Js.Truthy(r.Brand) ? r.Brand! : "NON-PL";
                    catHead = GrcLookups.CatHeadDisplayName(GrcLookups.CatHeadEmailFor(catHeadMap, brandForMap, info.McCd, info.Mrp));
                    if (inUm)
                    {
                        bucket = GrcBuckets.SingleConcept;
                    }
                    else
                    {
                        var rankKey = r.Gm + "|" + Js.ToJsString(r.Colour) + "|" + info.McCd + "|" + info.Mrp + "|" + info.Flag + "|" + r.Period;
                        if (!rankCache.TryGetValue(rankKey, out var cached))
                        {
                            cached = RankToBucket(ranker, r.Gm, r.Colour, info, r.Period);
                            rankCache[rankKey] = cached;
                        }
                        bucket = cached.Bucket;
                        bsrFound = bucket == GrcBuckets.NoPeerData ? "No" : "Yes";
                        if (cached.Pdstr != null) { pdstr = Js.ParseFloat(Js.ToFixed(cached.Pdstr.Value * 100, 2)); wof = cached.Wof!.Value; }
                        judgeable = IsJudgeable(bucket) ? "Yes" : "No";
                        wrong = bucket == GrcBuckets.Q4 || bucket == GrcBuckets.AbsBlock ? "Yes" : "No";
                    }
                }

                var artdesc = r.Row["artdesc"];
                rows.Add(new List<object?>
                {
                    r.Row["period"], r.Row["brand"], r.Row["gm"], r.Row["colour"], Js.Truthy(artdesc) ? artdesc : "",
                    r.Row["qty"], r.Row["value"] ?? "",
                    division, mcCd, mrp, flag, catHead, inUm ? "Yes" : "No", bsrFound, pdstr, wof, bucket, judgeable, wrong,
                });
            }

            return new GrcRowDetailExportDto
            {
                FileName = $"GRC_Row_Detail_{GrcPeriodMath.IsoDate(nowMs)}.xlsx",
                Rows = rows,
                Message = $"Exported {rows.Count} rows",
            };
        }
    }
}
