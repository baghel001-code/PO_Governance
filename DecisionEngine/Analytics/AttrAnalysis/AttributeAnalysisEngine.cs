using DecisionEngine.Analytics.Common;
using DecisionEngine.Common;
using DecisionEngine.Model;
using DecisionEngine.Recommendation;
using DecisionEngine.Results.Analytics.AttrAnalysis;

namespace DecisionEngine.Analytics.AttrAnalysis
{
    /// <summary>
    /// "🔎 Attribute Analysis" calculations (index.html view-attranalysis, functions _aaBuildTree …
    /// _aaRenderLevel3, lines 9516–9902): Brand → MC → MRP × Flag drill-down with Sales Mix / Stock Mix /
    /// Deviation per Article Master attribute and colour-level STR. Pure: reads only the snapshot.
    /// </summary>
    public interface IAttributeAnalysisEngine
    {
        /// <summary>Level 1 — Brand → MC table with the filters applied.</summary>
        AaLevel1Dto GetLevel1(EngineData data, AttrAnalysisQuery? query);
        /// <summary>Level 2 — MRP × Flag buckets of one Brand × MC. Throws <see cref="EngineNotFoundException"/> when the MC is not in the data.</summary>
        AaLevel2Dto GetLevel2(EngineData data, string brand, string mcKey);
        /// <summary>Level 3 — attribute tables + colour-level STR of one bucket. Throws <see cref="EngineNotFoundException"/> when unknown.</summary>
        AaLevel3Dto GetLevel3(EngineData data, string brand, string mcKey, string mrp, string flag);
    }

    /// <summary>
    /// Port of the "🔎 Attribute Analysis" page (index.html lines 9516–9902). Each level returns the
    /// figures, texts and colours its JS renderer put into the HTML.
    /// </summary>
    public sealed class AttributeAnalysisEngine : IAttributeAnalysisEngine
    {
        // AA_PAL
        public const string Blue = "#2563EB", BlueBg = "#EFF6FF", Green = "#059669", GreenBg = "#ECFDF5",
            Amber = "#D97706", AmberBg = "#FFFBEB", Red = "#DC2626", RedBg = "#FEF2F2", Grey = "#6B7280", GreyBg = "#F3F4F6";

        /// <summary>Port of <c>_aaStrColor</c>.</summary>
        public static AaStatusDto StrColor(double? str)
        {
            if (str == null) return new AaStatusDto { Color = Grey, Background = GreyBg, Label = "No Sales" };
            if (str < 30) return new AaStatusDto { Color = Blue, Background = BlueBg, Label = "Fast" };
            if (str < 75) return new AaStatusDto { Color = Green, Background = GreenBg, Label = "Healthy" };
            if (str < 120) return new AaStatusDto { Color = Amber, Background = AmberBg, Label = "Slow" };
            return new AaStatusDto { Color = Red, Background = RedBg, Label = "Very Slow" };
        }

        /// <summary>Port of <c>_aaDevColor</c>.</summary>
        public static string DevColor(double dev) => Math.Abs(dev) < 5 ? Green : dev > 0 ? Blue : Red;

        /// <summary>Port of <c>_aaImpliedStr</c>.</summary>
        public static double? ImpliedStr(double stk, double s45)
        {
            var dr = s45 / 45;
            return dr > 0 ? stk / dr : null;
        }

        /// <summary>Port of <c>_aaLegend</c>.</summary>
        public static List<AaLegendItemDto> Legend() => new()
        {
            new("Fast — selling ahead of stock", Blue), new("Healthy / balanced", Green), new("Slow", Amber),
            new("Very slow / overstocked", Red), new("No sales", Grey)
        };

        /// <summary>The deviation legend above the attribute tables.</summary>
        public static List<AaLegendItemDto> DeviationLegend() => new()
        {
            new("Selling ahead of stock share (chase risk)", Blue), new("Balanced (within ±5pp)", Green),
            new("Holding more stock share than sales (overstock risk)", Red)
        };

        private static string Tooltip(double? str, AaStatusDto sc) =>
            "Implied STR: " + (str != null ? AnalyticsJs.RoundStr(str.Value) + "d — " : "") + sc.Label;

        /// <summary>Port of <c>renderAttrAnalysis</c> / <c>_aaPopulateFilters</c> / <c>_aaRenderLevel1</c>.</summary>
        public AaLevel1Dto GetLevel1(EngineData data, AttrAnalysisQuery? query)
        {
            query ??= new AttrAnalysisQuery();
            var tree = AttrAnalysisTree.Build(data);
            var dto = new AaLevel1Dto { Brands = AnalyticsJs.SortStrings(tree.Keys) };
            if (data.F1 == null && data.F3 == null)
            {
                dto.State = "noData";
                dto.EmptyTitle = "No F1/F3 data loaded";
                dto.EmptyMessage = "Upload F1 Stock and F3 Sales Trend in Data Setup first.";
                return dto;
            }
            var fBrand = query.Brand ?? "";
            var fMc = (query.Mc ?? "").Trim().ToLowerInvariant();
            var fMinStk = Js.Sf(string.IsNullOrEmpty(query.MinStock) ? 0.0 : query.MinStock);
            var rows = new List<AaMcRowDto>();
            foreach (var brand in AnalyticsJs.SortStrings(tree.Keys))
            {
                if (fBrand.Length > 0 && brand != fBrand) continue;
                foreach (var (mcKey, m) in tree[brand])
                {
                    if (fMc.Length > 0 && !m.McCd.ToLowerInvariant().Contains(fMc, StringComparison.Ordinal)
                        && !m.McDesc.ToLowerInvariant().Contains(fMc, StringComparison.Ordinal)) continue;
                    if (fMinStk > 0 && m.Stk < fMinStk) continue;
                    rows.Add(new AaMcRowDto { Brand = brand, McKey = mcKey, McCd = m.McCd, McDesc = m.McDesc, Stock = m.Stk, Sales45 = m.S45, Buckets = m.Buckets.Count });
                }
            }
            JsSort.StableSort(rows, (a, b) => b.Stock - a.Stock);
            dto.Breadcrumb.Add(new AaCrumbDto("Brand → MC", null));
            if (rows.Count == 0)
            {
                dto.State = "noMatch";
                dto.EmptyMessage = "No Brand/MC combinations match the current filters.";
                return dto;
            }
            dto.Legend = Legend();
            for (var i = 0; i < rows.Count; i++)
            {
                var r = rows[i];
                r.Index = i;
                r.ImpliedStr = ImpliedStr(r.Stock, r.Sales45);
                r.Status = StrColor(r.ImpliedStr);
                r.Tooltip = Tooltip(r.ImpliedStr, r.Status);
                r.StockText = Js.Fmt(r.Stock);
                r.Sales45Text = Js.Fmt(r.Sales45);
            }
            dto.Rows = rows;
            return dto;
        }

        private static AaMcNode McNode(EngineData data, string brand, string mcKey)
        {
            var tree = AttrAnalysisTree.Build(data);
            if (brand == null || mcKey == null || !tree.TryGetValue(brand, out var mcs) || !mcs.TryGetValue(mcKey, out var node))
                throw new EngineNotFoundException("This Brand / MC is not in the current F1 / F3 data");
            return node;
        }

        /// <summary>Port of <c>_aaRenderLevel2</c>.</summary>
        public AaLevel2Dto GetLevel2(EngineData data, string brand, string mcKey)
        {
            var mcNode = McNode(data, brand, mcKey);
            var rows = mcNode.Buckets.Values.Select(b => new AaBucketRowDto
            {
                Mrp = b.Mrp, Flag = b.Flag, Stock = b.Stk, Sales45 = b.S45, Colours = b.Cols.Count
            }).ToList();
            JsSort.StableSort(rows, (a, b) => b.Stock - a.Stock);
            for (var i = 0; i < rows.Count; i++)
            {
                var r = rows[i];
                r.Index = i;
                r.MrpText = "₹" + r.Mrp;
                r.ImpliedStr = ImpliedStr(r.Stock, r.Sales45);
                r.Status = StrColor(r.ImpliedStr);
                r.Tooltip = Tooltip(r.ImpliedStr, r.Status);
                r.StockText = Js.Fmt(r.Stock);
                r.Sales45Text = Js.Fmt(r.Sales45);
            }
            return new AaLevel2Dto
            {
                Brand = brand, McKey = mcKey, McCd = mcNode.McCd, McDesc = mcNode.McDesc,
                Breadcrumb = { new AaCrumbDto("Brand → MC", 1), new AaCrumbDto(brand + " · " + mcNode.McDesc + " (" + mcNode.McCd + ")", null) },
                Legend = Legend(), Rows = rows
            };
        }

        private static string Pct1(double v) => AnalyticsJs.ToFixed(v, 1) + "%";

        /// <summary>Port of <c>_aaRenderLevel3</c>.</summary>
        public AaLevel3Dto GetLevel3(EngineData data, string brand, string mcKey, string mrp, string flag)
        {
            var mcNode = McNode(data, brand, mcKey);
            if (!mcNode.Buckets.Values.Any(b => b.Mrp == mrp && b.Flag == flag))
                throw new EngineNotFoundException("This MRP × Flag bucket is not in the current F1 / F3 data");
            var mcCd = mcNode.McCd;
            var mcDesc = mcNode.McDesc;

            var f1ByCol = RecommendationData.F1ByCol(data, mcCd, mrp, brand, flag);
            var f3ByCol = RecommendationData.F3ByCol(data, mcCd, mrp, brand, flag).ByCol;
            double totalStk = 0, totalS45 = 0;
            foreach (var v in f1ByCol.Values) totalStk += v;
            foreach (var v in f3ByCol.Values) totalS45 += v;

            var dto = new AaLevel3Dto
            {
                Brand = brand, McKey = mcKey, McCd = mcCd, McDesc = mcDesc, Mrp = mrp, Flag = flag,
                TotalStock = totalStk, TotalSales45 = totalS45,
                Breadcrumb = { new AaCrumbDto("Brand → MC", 1), new AaCrumbDto(brand + " · " + mcDesc, 2), new AaCrumbDto("₹" + mrp + " · " + flag, null) },
                Summary =
                {
                    new("Brand Type", brand), new("MC", mcCd), new("MC Desc", mcDesc), new("MRP", "₹" + mrp), new("Flag", flag),
                    new("Total Stock", Js.Fmt(totalStk)), new("Total Sales (45d)", Js.Fmt(totalS45))
                }
            };

            var attrCols = AttrAnalysisTree.DiscoverAttrCols(data);
            dto.AttributeColumns = attrCols;
            if (data.ArtMaster == null || data.ArtMaster.Count == 0)
            {
                dto.Note = "Article Master not loaded — attribute-wise Sales Mix / Stock Mix / Deviation cannot be computed without it. Colour-level STR below still works from F1/F3 alone.";
                dto.NoteKind = "warning";
            }
            else if (attrCols.Count == 0)
            {
                dto.Note = "No attribute columns found in Article Master beyond the standard identifier fields.";
                dto.NoteKind = "info";
            }
            if (attrCols.Count > 0) dto.DeviationLegend = DeviationLegend();

            foreach (var dim in attrCols)
            {
                var attrMap = RecommendationData.F1F3ByAttr(data, mcCd, mrp, brand, flag, dim);
                if (attrMap.Count == 0) continue;
                var rows = attrMap.Keys.Select(v =>
                {
                    var node = attrMap[v];
                    var stkMix = totalStk > 0 ? node.Stk / totalStk * 100 : 0;
                    var salMix = totalS45 > 0 ? node.S45 / totalS45 * 100 : 0;
                    return new AaAttrRowDto { Value = v, Stock = node.Stk, Sales45 = node.S45, StockMix = stkMix, SalesMix = salMix, Deviation = salMix - stkMix };
                }).ToList();
                JsSort.StableSort(rows, (a, b) => b.Stock - a.Stock);
                double mappedStk = 0, mappedS45 = 0;
                foreach (var r in rows) mappedStk += r.Stock;
                foreach (var r in rows) mappedS45 += r.Sales45;
                var unmappedStk = totalStk - mappedStk;
                var unmappedS45 = totalS45 - mappedS45;
                if (unmappedStk > 0.5 || unmappedS45 > 0.5)
                {
                    var uStkMix = totalStk > 0 ? unmappedStk / totalStk * 100 : 0;
                    var uSalMix = totalS45 > 0 ? unmappedS45 / totalS45 * 100 : 0;
                    rows.Add(new AaAttrRowDto
                    {
                        Value = "(No " + dim + " tag in Article Master)", Stock = Math.Max(unmappedStk, 0), Sales45 = Math.Max(unmappedS45, 0),
                        StockMix = uStkMix, SalesMix = uSalMix, Deviation = uSalMix - uStkMix, Unmapped = true
                    });
                }
                foreach (var r in rows)
                {
                    var dev = r.Deviation!.Value;
                    r.StockText = Js.Fmt(r.Stock);
                    r.Sales45Text = Js.Fmt(r.Sales45);
                    r.StockMixText = Pct1(r.StockMix);
                    r.SalesMixText = Pct1(r.SalesMix);
                    r.DeviationText = (dev > 0 ? "+" : "") + AnalyticsJs.ToFixed(dev, 1) + "pp";
                    r.DeviationColor = DevColor(dev);
                }
                dto.AttributeTables.Add(new AaAttrTableDto
                {
                    Dimension = dim, Title = dim + " — Sales Mix vs Stock Mix", Rows = rows,
                    Total = new AaAttrRowDto
                    {
                        Value = "Total (bucket)", Stock = totalStk, StockText = Js.Fmt(totalStk), StockMix = 100, StockMixText = "100.0%",
                        Sales45 = totalS45, Sales45Text = Js.Fmt(totalS45), SalesMix = 100, SalesMixText = "100.0%", DeviationText = "—"
                    }
                });
            }

            // colour-level STR
            var cols = AnalyticsJs.Distinct(f1ByCol.Keys.Concat(f3ByCol.Keys)).Where(c => c.Length > 0).ToList();
            var colRows = cols.Select(c =>
            {
                var stk = f1ByCol.TryGetValue(c, out var a) && a != 0 && !double.IsNaN(a) ? a : 0;
                var s45 = f3ByCol.TryGetValue(c, out var b) && b != 0 && !double.IsNaN(b) ? b : 0;
                var dr45 = s45 / 45;
                double? str = dr45 > 0 ? Js.Round(stk / dr45) : null;
                return new AaColourRowDto
                {
                    Colour = c, Stock = stk, Sales45 = s45, Str = str,
                    StockMix = totalStk > 0 ? stk / totalStk * 100 : 0,
                    SalesMix = totalS45 > 0 ? s45 / totalS45 * 100 : 0
                };
            }).ToList();
            JsSort.StableSort(colRows, (a, b) => b.Stock - a.Stock);
            dto.ColourTitle = "Colour-level STR — bucket: ₹" + mrp + " · " + flag;

            var bands = new List<(string Label, double? Min, double? Max, string Color)>
            {
                ("No Sales (45d)", null, null, Grey), ("< 30d — Fast", 0, 30, Blue), ("30–75d — Healthy", 30, 75, Green),
                ("75–120d — Slow", 75, 120, Amber), ("> 120d — Very Slow", 120, double.PositiveInfinity, Red)
            };
            var counts = new int[bands.Count];
            var stks = new double[bands.Count];
            foreach (var r in colRows)
            {
                int bi;
                if (r.Str == null) bi = 0;
                else
                {
                    bi = -1;
                    for (var k = 1; k < bands.Count; k++)
                        if (r.Str >= bands[k].Min && r.Str < bands[k].Max) { bi = k; break; }
                    if (bi < 0) bi = bands.Count - 1;
                }
                counts[bi]++;
                stks[bi] += r.Stock;
            }
            for (var k = 0; k < bands.Count; k++)
                dto.Bands.Add(new AaBandDto { Label = bands[k].Label, Color = bands[k].Color, Count = counts[k], Stock = stks[k], StockText = Js.Fmt(stks[k]) + " units" });

            foreach (var r in colRows)
            {
                r.Status = StrColor(r.Str);
                r.StockText = Js.Fmt(r.Stock);
                r.Sales45Text = Js.Fmt(r.Sales45);
                r.StockMixText = Pct1(r.StockMix);
                r.SalesMixText = Pct1(r.SalesMix);
                r.StrText = r.Str != null ? Js.NumberToString(r.Str.Value) + "d" : "—";
            }
            dto.ColourRows = colRows;
            return dto;
        }
    }
}
