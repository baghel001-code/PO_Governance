using Application.DTOs.Tool2;
using Application.Services.V1.Engines.Common;
using Domain.Constants;
using Domain.Entities.Common;

namespace Application.Services.V1.Tool2
{
    /// <summary>
    /// Dashboard aggregations of the "📋 Raised PO Assessment" screen — ports of index.html
    /// <c>renderAssessmentDashboard</c> (KPIs, charts, engine split), <c>renderBrandSummary</c> (tree),
    /// <c>populateDetailFilters</c> / <c>populateRollupFilters</c>, <c>renderRollupTables</c> /
    /// <c>buildRollupDetailTable</c>, <c>buildEngineB_Groups</c>, <c>setEngineTab</c> /
    /// <c>renderDetailHeader</c> / <c>filterAssTable</c> / <c>setDecFilter</c>.
    /// </summary>
    public sealed partial class Tool2Workspace
    {
        private static bool IsAction(Tool2LineResult r) => r.Decision != Decisions.Retain && r.Decision != Decisions.CannotValidate;

        /// <summary>cut qty × (PO rate, else MRP) when that rate is a positive finite number, else 0.</summary>
        public static double ValueAtRisk(Tool2LineResult r, double cutQty)
        {
            var rate = r.PoRate ?? Js.Number(r.Mrp);
            return double.IsFinite(rate) && rate > 0 ? rate * cutQty : 0;
        }

        private string CurMonKey => MonthMath.Mmyyyy(Ctx.Y, Ctx.M);

        // ── line DTO ───────────────────────────────────────────────────────

        public Tool2LineDto ToDto(Tool2LineResult r)
        {
            var rcOpts = ReduceCancelOptions(r, 3);
            var email = CatHeadEmailFor(r.BrandOrNonPl, r.Info?.McCd, r.Mrp);
            return new Tool2LineDto
            {
                Idx = r.Idx, Gm = r.Gm, Colour = r.Colour, Dm = r.Dm, RelQty = r.RelQty, PoNum = r.PoNum, Engine = r.Engine,
                Decision = r.Decision, DecisionReason = r.DecisionReason,
                StrCaution = r.StrCaution is { Items.Count: > 0 } ? FillCautionTexts(r.StrCaution) : null,
                ArtStr = r.ArtStr, RecQty = r.RecQty, ExcessQty = r.ExcessQty, MixPct = r.MixPct, OpStock = r.OpStock,
                Div = r.Div, McDesc = r.McDesc, Mrp = r.Mrp, Vendor = r.Vendor,
                Info = r.Info == null ? null : new Tool2InfoDto
                {
                    Brand = r.Info.Brand, Div = r.Info.Div, McCd = r.Info.McCd, McDesc = r.Info.McDesc, Mrp = r.Info.Mrp, Flag = r.Info.Flag,
                    Season = r.Info.Season, ArtDesc = r.Info.ArtDesc, VendorName = r.Info.VendorName
                },
                PoValue = r.PoValue, PoRate = r.PoRate, OptionType = r.OptionType, Quartile = r.Quartile, Percentile = r.Percentile, Pdstr = r.Pdstr,
                PeerAvg = r.PeerAvg, GapPct = r.GapPct, InBorderZone = r.InBorderZone, BorderTolUsed = r.BorderTolUsed, Wof = r.Wof, TotalBsr = r.TotalBsr,
                BsrLabel = r.BsrLabel, PeerPoolSize = r.PeerPool?.Count, CleanPoolSize = r.CleanPool?.Count, SiblingsCount = r.Siblings?.Count, QualSibsCount = r.QualSibs?.Count,
                WindowDailyRate = r.WindowDailyRate, WinStockStart = r.WinStockStart,
                Chain = r.Chain?.Select(c => new Tool2ChainStepDto
                {
                    Y = c.Y, M = c.M, MKey = c.MKey, OpenStock = c.OpenStock, EPO = c.EPO, ColPlan = c.ColPlan, IsCurrMonth = c.IsCurrMonth,
                    DemandDays = c.DemandDays, TotalDays = c.TotalDays, Fc = c.Fc, CloseStock = c.CloseStock, Str = c.Str, Status = c.Status
                }).ToList(),
                Brand = r.BrandOrNonPl, Flag = r.Info?.Flag ?? "", McCd = r.Info?.McCd ?? "",
                EngineLabel = r.Engine == "B" ? "Single Concept MRP" : "Multi Concept MRP",
                DecisionColor = DecColor(r.Decision), DecisionBg = DecBg(r.Decision),
                StrText = StrText(r.ArtStr), StrBreach = r.ArtStr != null && r.ArtStr > EffT2,
                Tooltip = Tooltip(r), Reason = DetailedReason(r),
                CatHead = CatHeadDisplayName(email), CatHeadEmail = email,
                Bucket = BucketFor(r),
                RecommendedActionA = RecommendedActionA(r),
                RankedOptions = RankedOptionsAny(r, 4),
                RcOptions = rcOpts,
                RcRecommendation = ReduceCancelRecommendation(r, rcOpts),
                PoStatus = PoStatusFlag(r),
                Driver = DriverBucket(r.DecisionReason)
            };
        }

        // ── KPI strip ──────────────────────────────────────────────────────

        public List<Tool2KpiDto> Kpis()
        {
            var total = Results.Count;
            int By(string d) => Results.Count(r => r.Decision == d);
            int retain = By(Decisions.Retain), reduce = By(Decisions.ReduceQty), cancel = By(Decisions.Cancel), watch = By(Decisions.WatchList);
            double atRisk = 0, value = 0;
            foreach (var r in Results.Where(IsAction)) { atRisk += r.CutQty; value += ValueAtRisk(r, r.CutQty); }
            var pctAction = total > 0 ? Js.Round((double)(reduce + cancel + watch) / total * 100) : 0;
            return new List<Tool2KpiDto>
            {
                new() { Label = "Total Lines", Value = total.ToString(), Number = total, Color = "#1A4FAF", Bg = "#EAF0FB" },
                new() { Label = "Retain", Value = retain.ToString(), Number = retain, Color = "#00873E", Bg = "#DCFCE7", Decision = Decisions.Retain },
                new() { Label = "Reduce Qty", Value = reduce.ToString(), Number = reduce, Color = "#D97706", Bg = "#FFFBEB", Decision = Decisions.ReduceQty },
                new() { Label = "Cancel", Value = cancel.ToString(), Number = cancel, Color = "#DC2626", Bg = "#FEF2F2", Decision = Decisions.Cancel },
                new() { Label = "Watch List", Value = watch.ToString(), Number = watch, Color = "#D97706", Bg = "#FFF3CD", Decision = Decisions.WatchList },
                new() { Label = "Qty at Risk", Value = Js.Fmt(atRisk), Number = atRisk, Color = "#E8006E", Bg = "#FCE7F3" },
                new() { Label = "₹ Value at Risk", Value = value > 0 ? "₹" + Js.FmtCr(value) : "—", Number = value, Color = "#7C3AED", Bg = "#F5F3FF" },
                new() { Label = "% Need Action", Value = N(pctAction) + "%", Number = pctAction, Color = "#7C3AED", Bg = "#F5F3FF" }
            };
        }

        // ── charts / engine split ──────────────────────────────────────────

        private sealed class Counts
        {
            public int Retain, Reduce, Cancel, Watch, Total;
            public double TotalQty;
            public void Add(string d)
            {
                if (d == Decisions.Retain) Retain++; else if (d == Decisions.ReduceQty) Reduce++; else if (d == Decisions.Cancel) Cancel++; else if (d == Decisions.WatchList) Watch++;
            }
        }

        public List<Tool2ChartBarDto> DivisionChart()
        {
            var map = new JsObjectMap<Counts>();
            foreach (var r in Results)
            {
                var d = string.IsNullOrEmpty(r.Div) ? "Unknown" : r.Div;
                if (!map.ContainsKey(d)) map[d] = new Counts();
                map[d].Add(r.Decision); map[d].Total++;
            }
            var entries = map.ToList();
            JsSort.StableSort(entries, (a, b) => b.Value.Total - a.Value.Total);
            entries = entries.Take(8).ToList();
            double max = Math.Max(entries.Count > 0 ? entries.Max(e => e.Value.Total) : 0, 1);
            return entries.Select(e => new Tool2ChartBarDto
            {
                Label = e.Key, Note = e.Value.Total.ToString(), Retain = e.Value.Retain, Reduce = e.Value.Reduce, Cancel = e.Value.Cancel, Watch = e.Value.Watch,
                Widths = new List<double> { e.Value.Retain / max * 100, e.Value.Reduce / max * 100, e.Value.Cancel / max * 100, e.Value.Watch / max * 100 }
            }).ToList();
        }

        public List<Tool2ChartBarDto> MonthChart()
        {
            var map = new JsObjectMap<Counts>();
            foreach (var r in Results)
            {
                var m = string.IsNullOrEmpty(r.Dm) ? "Unknown" : r.Dm;
                if (!map.ContainsKey(m)) map[m] = new Counts();
                map[m].Add(r.Decision);
                if (r.Decision != Decisions.Retain) map[m].TotalQty += r.CutQty;
            }
            var entries = map.ToList();
            JsSort.StableSort(entries, (a, b) => Tool2Js.LocaleCompare(a.Key, b.Key));
            double max = Math.Max(entries.Count > 0 ? entries.Max(e => e.Value.Retain + e.Value.Reduce + e.Value.Cancel + e.Value.Watch) : 0, 1);
            return entries.Select(e =>
            {
                var c = e.Value; var tot = c.Retain + c.Reduce + c.Cancel + c.Watch;
                return new Tool2ChartBarDto
                {
                    Label = e.Key, Note = tot + " lines" + (c.TotalQty > 0 ? " · " + Js.Fmt(c.TotalQty) + " at risk" : ""),
                    Retain = c.Retain, Reduce = c.Reduce, Cancel = c.Cancel, Watch = c.Watch,
                    Widths = new List<double> { c.Retain / max * 100, c.Reduce / max * 100, c.Cancel / max * 100, c.Watch / max * 100 }
                };
            }).ToList();
        }

        public List<Tool2EngineSplitDto> EngineSplit()
        {
            var total = Results.Count;
            var a = Results.Count(r => r.Engine == "A"); var b = Results.Count(r => r.Engine == "B");
            return new List<Tool2EngineSplitDto>
            {
                new() { Label = "Multi Concept MRP  ·  Multi-Concept BSR", Count = a, Width = total > 0 ? (double)a / total * 100 : 0, Color = "#1A4FAF", Bg = "#EAF0FB" },
                new() { Label = "Single Concept MRP  ·  Colour STR", Count = b, Width = total > 0 ? (double)b / total * 100 : 0, Color = "#00AEEF", Bg = "#E0F6FF" }
            };
        }

        // ── tree (renderBrandSummary) ──────────────────────────────────────

        private sealed class TNode
        {
            public int Total, Retain, Reduce, Cancel, Watch;
            public double AtRisk;
            public JsObjectMap<TNode> Kids = new();
            public List<Tool2LineResult> Rows = new();
        }

        public List<Tool2TreeNodeDto> Tree()
        {
            var tree = new JsObjectMap<TNode>();
            TNode Get(JsObjectMap<TNode> m, string k) { if (!m.ContainsKey(k)) m[k] = new TNode(); return m[k]; }
            foreach (var r in Results)
            {
                var b = Get(tree, r.BrandOrNonPl);
                var d = Get(b.Kids, (string.IsNullOrEmpty(r.Div) ? "—" : r.Div).Trim());
                var m = Get(d.Kids, (string.IsNullOrEmpty(r.McDesc) ? "—" : r.McDesc).Trim());
                var p = Get(m.Kids, (string.IsNullOrEmpty(r.Mrp) ? "—" : r.Mrp).Trim());
                foreach (var n in new[] { b, d, m, p })
                {
                    n.Total++;
                    if (r.Decision == Decisions.Retain) n.Retain++;
                    else if (r.Decision == Decisions.ReduceQty) { n.Reduce++; n.AtRisk += r.RelQty; }
                    else if (r.Decision == Decisions.Cancel) { n.Cancel++; n.AtRisk += r.RelQty; }
                    else if (r.Decision == Decisions.WatchList) { n.Watch++; n.AtRisk += r.RelQty; }
                }
                p.Rows.Add(r);
            }
            string[] depth = { "Brand Type", "Division", "MC", "MRP" };
            List<Tool2TreeNodeDto> Build(JsObjectMap<TNode> m, int level)
            {
                var entries = m.ToList();
                JsSort.StableSort(entries, (a, b) => b.Value.AtRisk - a.Value.AtRisk);
                return entries.Select(e =>
                {
                    var n = e.Value;
                    var pct = n.Total > 0 ? Js.Round((double)(n.Reduce + n.Cancel + n.Watch) / n.Total * 100) : 0;
                    var dto = new Tool2TreeNodeDto
                    {
                        Label = level == 3 ? "Rs." + e.Key : e.Key, Key = e.Key, Level = level, Depth = depth[level],
                        Total = n.Total, Retain = n.Retain, Reduce = n.Reduce, Cancel = n.Cancel, Watch = n.Watch, Pct = pct,
                        PctColor = level == 0 ? "rgba(255,255,255,.85)" : pct > 50 ? "#DC2626" : pct > 20 ? "#D97706" : "#00873E",
                        AtRisk = n.AtRisk, AtRiskText = n.AtRisk > 0 ? Js.Fmt(n.AtRisk) : "—"
                    };
                    if (level < 3) dto.Children = Build(n.Kids, level + 1);
                    else
                    {
                        var rows = n.Rows.ToList();
                        JsSort.StableSort(rows, (a, b) => b.RelQty - a.RelQty);
                        dto.Rows = rows.Select(x => x.Idx).ToList();
                    }
                    return dto;
                }).ToList();
            }
            return Build(tree, 0);
        }

        // ── filter option lists ────────────────────────────────────────────

        private static List<string> SortedUnique(IEnumerable<string> src, Func<string, string, double>? cmp = null)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal); var l = new List<string>();
            foreach (var s in src) if (seen.Add(s)) l.Add(s);
            if (cmp == null) l.Sort(string.CompareOrdinal); else JsSort.StableSort(l, cmp);
            return l;
        }

        private static double MrpCmp(string a, string b) => Js.ParseFloat(a) - Js.ParseFloat(b);

        /// <summary>Port of <c>populateDetailFilters()</c>.</summary>
        public Tool2FilterOptionsDto DetailFilters() => new()
        {
            Brands = SortedUnique(Results.Select(r => r.Info != null && r.Info.Brand.Length > 0 ? r.Info.Brand : "NON-PL").Where(s => s.Length > 0)),
            Divisions = SortedUnique(Results.Select(r => (string.IsNullOrEmpty(r.Div) ? "—" : r.Div).Trim()).Where(d => d.Length > 0 && d != "—")),
            Mcs = SortedUnique(Results.Select(r => (string.IsNullOrEmpty(r.McDesc) ? "—" : r.McDesc).Trim()).Where(d => d.Length > 0 && d != "—")),
            Mrps = SortedUnique(Results.Select(r => (r.Mrp ?? "").Trim()).Where(s => s.Length > 0), MrpCmp).Select(m => "₹" + m).ToList(),
            Months = SortedUnique(Results.Select(r => (r.Dm ?? "").Trim()).Where(s => s.Length > 0))
        };

        /// <summary>Port of <c>populateRollupFilters()</c>.</summary>
        public Tool2FilterOptionsDto RollupFilters()
        {
            var d = DetailFilters();
            d.Brands = new List<string>();
            return d;
        }

        // ── rollup tables (renderRollupTables) ─────────────────────────────

        private sealed class VAgg
        {
            public int Retain, Reduce, Cancel, Watch, Lines;
            public double AtRisk, Value;
            public string Brand = "", Div = "", Mc = "";
            public List<Tool2LineResult> Rows = new();
            public void Add(string d)
            {
                if (d == Decisions.Retain) Retain++; else if (d == Decisions.ReduceQty) Reduce++; else if (d == Decisions.Cancel) Cancel++; else if (d == Decisions.WatchList) Watch++;
            }
        }

        public Tool2RollupDto Rollup(Tool2RollupFilterDto? f)
        {
            f ??= new Tool2RollupFilterDto();
            var fMrp = (f.Mrp ?? "").Replace("₹", "");
            var fMrpFirst = f.Mrp ?? "";
            var idxR = fMrpFirst.IndexOf('₹');
            if (idxR >= 0) fMrp = fMrpFirst[..idxR] + fMrpFirst[(idxR + 1)..]; // JS .replace('₹','') — first occurrence only
            var rows = Results.Where(r =>
                (string.IsNullOrEmpty(f.Div) || (r.Div ?? "").Trim() == f.Div) &&
                (string.IsNullOrEmpty(f.Mc) || (r.McDesc ?? "").Trim() == f.Mc) &&
                (fMrp.Length == 0 || (r.Mrp ?? "").Trim() == fMrp) &&
                (string.IsNullOrEmpty(f.Mon) || (r.Dm ?? "").Trim() == f.Mon) &&
                (string.IsNullOrEmpty(f.Dec) || r.Decision == f.Dec)).ToList();

            var dto = new Tool2RollupDto();
            var vend = new JsObjectMap<VAgg>();
            foreach (var r in rows)
            {
                var v = string.IsNullOrEmpty(r.Vendor) ? "Unknown" : r.Vendor;
                if (!vend.ContainsKey(v)) vend[v] = new VAgg();
                var a = vend[v];
                a.Add(r.Decision);
                if (IsAction(r)) { a.AtRisk += r.CutQty; a.Value += ValueAtRisk(r, r.CutQty); a.Rows.Add(r); }
            }
            var ve = vend.ToList();
            JsSort.StableSort(ve, (a, b) => b.Value.AtRisk - a.Value.AtRisk);
            foreach (var (name, a) in ve)
                dto.Vendors.Add(new Tool2VendorRowDto
                {
                    Vendor = name, Total = a.Retain + a.Reduce + a.Cancel + a.Watch, Retain = a.Retain, Reduce = a.Reduce, Cancel = a.Cancel, Watch = a.Watch,
                    AtRisk = a.AtRisk, ValueAtRisk = a.Value, AtRiskText = a.AtRisk > 0 ? Js.Fmt(a.AtRisk) : "—", ValueText = a.Value > 0 ? "₹" + Js.FmtCr(a.Value) : "—",
                    Rows = a.Rows.Select(x => x.Idx).ToList()
                });

            var dm = new JsObjectMap<VAgg>();
            double totalRisk = 0;
            foreach (var r in rows)
            {
                if (!IsAction(r)) continue;
                var b = r.BrandOrNonPl; var d = string.IsNullOrEmpty(r.Div) ? "Unknown" : r.Div; var mc = string.IsNullOrEmpty(r.McDesc) ? "Unknown" : r.McDesc;
                var key = b + "|||" + d + "|||" + mc;
                if (!dm.ContainsKey(key)) dm[key] = new VAgg { Brand = b, Div = d, Mc = mc };
                var a = dm[key];
                a.Lines++; a.Add(r.Decision);
                a.AtRisk += r.CutQty; totalRisk += r.CutQty;
                a.Value += ValueAtRisk(r, r.CutQty);
                a.Rows.Add(r);
            }
            var de = dm.Values.ToList();
            JsSort.StableSort(de, (a, b) => b.AtRisk - a.AtRisk);
            foreach (var a in de)
            {
                var pct = totalRisk > 0 ? a.AtRisk / totalRisk * 100 : 0;
                dto.DivMc.Add(new Tool2DivMcRowDto
                {
                    Brand = a.Brand, Div = a.Div, Mc = a.Mc, Lines = a.Lines, Reduce = a.Reduce, Cancel = a.Cancel, Watch = a.Watch,
                    AtRisk = a.AtRisk, ValueAtRisk = a.Value, AtRiskText = a.AtRisk > 0 ? Js.Fmt(a.AtRisk) : "—", ValueText = a.Value > 0 ? "₹" + Js.FmtCr(a.Value) : "—",
                    PctOfTotal = pct, PctText = Js.ToFixed(pct, 1) + "%", Rows = a.Rows.Select(x => x.Idx).ToList()
                });
            }
            foreach (var r in dto.Vendors.SelectMany(v => v.Rows).Concat(dto.DivMc.SelectMany(v => v.Rows)).Distinct())
                dto.Lines[r] = RollupLine(Results.First(x => x.Idx == r));
            return dto;
        }

        /// <summary>One line of <c>buildRollupDetailTable</c>.</summary>
        public static Tool2RollupLineDto RollupLine(Tool2LineResult r)
        {
            var cut = r.CutQty;
            var col = r.Decision switch { "Reduce Qty" => "#D97706", "Cancel" => "#DC2626", "Watch List" => "#D97706", "Retain" => "#00873E", _ => "#6B7280" };
            return new Tool2RollupLineDto
            {
                Idx = r.Idx, Gm = r.Gm, Colour = string.IsNullOrEmpty(r.Colour) ? "—" : r.Colour,
                MrpText = !string.IsNullOrEmpty(r.Mrp) && r.Mrp != "—" ? "₹" + r.Mrp : "—",
                PoNum = string.IsNullOrEmpty(r.PoNum) ? "—" : r.PoNum, Released = Js.Fmt(r.RelQty),
                CutQty = cut > 0 ? Js.Fmt(cut) : "—", CutPositive = cut > 0, Decision = r.Decision, DecisionColor = col
            };
        }

        // ── Single Concept colour groups (buildEngineB_Groups) ─────────────

        public List<Tool2ColourGroupDto> BuildEngineBGroups(IEnumerable<Tool2LineResult> rows)
        {
            var groups = new JsObjectMap<(Tool2LineResult First, List<Tool2LineResult> Pos)>();
            foreach (var r in rows)
            {
                var brand = Or(r.Info?.Brand, "NON-PL");
                var flag = Or(r.Info?.Flag, "—");
                var key = brand + "|" + (r.Info?.McCd ?? "") + "|" + (r.Info?.Mrp ?? "") + "|" + flag + "|" + r.Colour + "|" + r.Dm;
                if (!groups.ContainsKey(key)) groups[key] = (r, new List<Tool2LineResult>());
                groups[key].Pos.Add(r);
            }
            var outL = new List<Tool2ColourGroupDto>();
            foreach (var (key, (g, pos)) in groups)
            {
                double totalQty = 0; foreach (var p in pos) totalQty += p.RelQty;
                var T = P.T2;
                var dailyRate = g.WindowDailyRate is double wd && wd != 0 && !double.IsNaN(wd) ? wd : 0;
                var winStock = g.WinStockStart ?? g.OpStock;
                double maxPerm, excess;
                if (g.ArtStr == null || dailyRate == 0) { maxPerm = totalQty; excess = 0; }
                else if (g.ArtStr <= EffT2) { maxPerm = totalQty; excess = 0; }
                else
                {
                    var stockNoPO = Math.Max(0, winStock - totalQty);
                    maxPerm = Math.Max(0, Js.Round(EffT2 * dailyRate - stockNoPO));
                    excess = Math.Max(0, totalQty - maxPerm);
                }
                string cd;
                if (g.Decision == Decisions.CannotValidate) cd = Decisions.CannotValidate;
                else if (g.Decision == Decisions.WatchList) cd = Decisions.WatchList;
                else if (g.ArtStr == null) cd = Decisions.Retain;
                else if (excess == 0) cd = Decisions.Retain;
                else if (maxPerm == 0) cd = "Cancel All";
                else cd = Decisions.ReduceQty;

                var sorted = pos.ToList();
                JsSort.StableSort(sorted, (a, b) => b.RelQty - a.RelQty);
                var remaining = excess;
                var actions = sorted.Select(p =>
                {
                    if (remaining <= 0) return new Tool2PoActionDto { PoNum = p.PoNum, Gm = p.Gm, RelQty = p.RelQty, Action = Decisions.Retain, RecQty = p.RelQty };
                    if (remaining >= p.RelQty) { remaining -= p.RelQty; return new Tool2PoActionDto { PoNum = p.PoNum, Gm = p.Gm, RelQty = p.RelQty, Action = Decisions.Cancel, RecQty = 0 }; }
                    var retain = p.RelQty - remaining; remaining = 0;
                    return new Tool2PoActionDto { PoNum = p.PoNum, Gm = p.Gm, RelQty = p.RelQty, Action = Decisions.ReduceQty, RecQty = retain };
                }).ToList();
                foreach (var a in actions) a.ActionColor = a.Action == Decisions.Cancel ? "#DC2626" : a.Action == Decisions.ReduceQty ? "#D97706" : "#00873E";

                var dto = new Tool2ColourGroupDto
                {
                    Key = key, Gm = g.Gm, Colour = g.Colour, Dm = g.Dm, Div = g.Div, McDesc = g.McDesc, Mrp = g.Mrp,
                    Brand = Or(g.Info?.Brand, "NON-PL"), Flag = Or(g.Info?.Flag, "—"), McCd = g.Info?.McCd ?? "",
                    ArtStr = g.ArtStr, MixPct = g.MixPct, OpStock = g.OpStock, WindowDailyRate = g.WindowDailyRate, WinStockStart = g.WinStockStart,
                    RowDecision = g.Decision, RowDecisionReason = g.DecisionReason,
                    TotalQty = totalQty, MaxPermissible = maxPerm, ExcessUnits = excess, ColourDecision = cd, PoCount = pos.Count, PoActions = actions
                };
                FillGroupDisplay(dto);
                outL.Add(dto);
            }
            return outL;
        }

        private void FillGroupDisplay(Tool2ColourGroupDto g)
        {
            var dec = g.ColourDecision;
            g.DecisionColor = dec == "Cancel All" || dec == "Cancel" ? "#DC2626" : dec == "Reduce Qty" ? "#D97706" : dec == "Watch List" ? "#92400E" : dec == "Cannot Validate" ? "#6B7280" : "#00873E";
            g.DecisionBg = dec == "Cancel All" || dec == "Cancel" ? "#FEF2F2" : dec == "Reduce Qty" ? "#FFFBEB" : dec == "Watch List" ? "#FFF7ED" : dec == "Cannot Validate" ? "#F3F4F6" : "#DCFCE7";
            g.StrText = StrText(g.ArtStr);
            g.StrColor = g.ArtStr != null && g.ArtStr > EffT2 ? "#DC2626" : "var(--ink2)";
            g.MixText = Pct1(g.MixPct) + "%";
            g.MixOk = g.MixPct * 100 >= P.Mix;
            g.TotalQtyText = Js.Fmt(g.TotalQty);
            g.MaxPermText = g.MaxPermissible > 0 ? Js.Fmt(g.MaxPermissible) : "—";
            g.PanelTitle = dec + " — Colour STR Computation & PO Allocation";
            if (g.RowDecision == Decisions.CannotValidate || g.RowDecision == Decisions.WatchList || (g.ArtStr == null && g.RowDecision == Decisions.Retain))
                g.WhyNoStr = Or(g.RowDecisionReason, "No reason recorded.");
            var wdr = g.WindowDailyRate is double w && w != 0 ? w : 0;
            g.Kvs = new List<KvDto>
            {
                new("Colour", g.Colour),
                new("Brand × MC × MRP × Flag", g.Brand + " × " + Or(g.McCd, "—") + " × ₹" + g.Mrp + " × " + g.Flag),
                new("Op Stock (F1)", Js.Fmt(g.OpStock) + " units"),
                new("Colour Mix%", Pct1(g.MixPct) + "%"),
                new("Daily Rate (" + N(P.D) + "d window)", wdr != 0 ? Js.ToFixed(wdr, 1) + " units/day" : "—"),
                new("Opening STR", g.ArtStr != null ? N(Js.Round(g.ArtStr.Value)) + "d" : "N/A"),
                new("Threshold (T+Buffer)", EffT2R + "d"),
                new("Total PO Qty", Js.Fmt(g.TotalQty) + " units"),
                new("Max Permissible Qty", Js.Fmt(g.MaxPermissible) + " units")
            };
            if (g.ArtStr != null)
            {
                var ws = g.WinStockStart is double x && !double.IsNaN(x) ? x : 0;
                var stockNoPO = Math.Max(0, Js.Round(ws - g.TotalQty));
                var dr2 = wdr != 0 ? Js.ToFixed(wdr, 2) : "0";
                var f = new List<List<TextSegDto>>
                {
                    new() { new TextSegDto("Opening STR  = Stock at Win Start (with POs) ÷ Daily Rate × " + N(P.D)) },
                    new() { new TextSegDto("= " + Js.Fmt(Js.Round(ws)) + " ÷ " + dr2 + " × " + N(P.D) + " = "), new TextSegDto(g.StrText, true, g.StrColor) }
                };
                if (g.ArtStr <= EffT2)
                    f.Add(new() { new TextSegDto("STR ≤ " + EffT2R + "d (T=" + N(P.T2) + "d + " + N(Buf) + "% buffer) → "), new TextSegDto("Retain full PO. No reduction needed.", true, "#00873E") });
                else
                {
                    f.Add(new() { new TextSegDto("Stock WITHOUT assessed POs  = Win Start Stock − Total PO Qty = " + Js.Fmt(Js.Round(ws)) + " − " + Js.Fmt(g.TotalQty) + " = " + Js.Fmt(stockNoPO) + " units") });
                    f.Add(new() { new TextSegDto("Max Permissible Qty  = MAX(0, T×(1+Buffer%) × Daily Rate − Stock w/o POs) = MAX(0, " + EffT2R + " × DailyRate − StockNoPO)") });
                    f.Add(new() { new TextSegDto("= MAX(0, " + EffT2R + " × " + dr2 + " − " + Js.Fmt(stockNoPO) + ") = "), new TextSegDto(Js.Fmt(g.MaxPermissible) + " units", true, "#E8006E") });
                }
                g.Formula = f;
            }
            double tr = 0; foreach (var a in g.PoActions) tr += a.RecQty;
            g.TotalRetain = tr;
        }

        // ── detail view (setEngineTab / renderDetailHeader / filterAssTable) ─

        public static List<Tool2HeaderColDto> DetailHeader(string tab) => tab == "B"
            ? new List<Tool2HeaderColDto>
            {
                new("Colour", "left"), new("Brand", "left"), new("MC Desc", "left", "130px"), new("MRP", "right"), new("BOGO / Non-BOGO", "center"),
                new("Del. Month", "center"), new("# POs", "center"), new("Total PO Qty", "right"), new("Colour Mix%", "right"), new("Opening STR", "right"),
                new("Max Permissible Qty", "right"), new("Colour Decision", "center"), new("▼", "center")
            }
            : new List<Tool2HeaderColDto>
            {
                new("PO Number", "left"), new("Gen. Material", "left"), new("Colour", "left"), new("Brand", "left"), new("Division", "left"),
                new("MC Desc", "left", "140px"), new("MRP", "right"), new("Del. Month", "center"), new("Released Qty", "right"), new("Option Type", "left"),
                new("Quartile", "center"), new("Percentile", "right"), new("Decision", "center"), new("▼", "center")
            };

        public Tool2DetailViewDto Detail(Tool2DetailFilterDto? f, List<Tool2ColourGroupDto>? allGroups = null)
        {
            f ??= new Tool2DetailFilterDto();
            var tab = f.Tab == "A" ? "A" : "B";
            string fBrand = f.Brand ?? "", fDiv = f.Div ?? "", fMC = f.Mc ?? "", fMRP = f.Mrp ?? "", fMon = f.Mon ?? "", fDec = f.Dec ?? "";
            var cur = CurMonKey;
            var showAll = fDec.Length > 0;
            var view = new Tool2DetailViewDto { Tab = tab, Header = DetailHeader(tab) };
            if (tab == "B")
            {
                view.BannerTitle = "Single Concept MRP · Colour STR";
                view.BannerText = "Grouped by Brand × MC × MRP × Flag × Colour × Del. Month. STR computed at colour level; PO-wise actions allocated by FIFO (largest PO cut first).";
                view.BannerColor = "#004F73"; view.BannerBg = "#E0F6FF";
            }
            else
            {
                view.BannerTitle = "Multi Concept MRP · Peer BSR";
                view.BannerText = "One row per PO. Key metrics: Option Type, Quartile, Percentile vs peer group.";
                view.BannerColor = "#0F2D70"; view.BannerBg = "#EAF0FB";
            }
            var baseRows = Results.Where(r =>
            {
                if (r.Engine != tab) return false;
                if (tab != "B")
                {
                    if (showAll) { if ((r.Decision ?? "").Trim() != fDec) return false; }
                    else if (fMon.Length == 0 && string.CompareOrdinal(r.Dm, cur) < 0) return false;
                }
                else if (fMon.Length == 0 && string.CompareOrdinal(r.Dm, cur) < 0) return false;
                if (fBrand.Length > 0 && r.BrandOrNonPl.Trim() != fBrand.Trim()) return false;
                if (fDiv.Length > 0 && (r.Div ?? "").Trim() != fDiv.Trim()) return false;
                if (fMC.Length > 0 && (r.McDesc ?? "").Trim() != fMC.Trim()) return false;
                if (fMRP.Length > 0 && "₹" + (r.Mrp ?? "").Trim() != fMRP.Trim()) return false;
                if (fMon.Length > 0 && (r.Dm ?? "").Trim() != fMon.Trim()) return false;
                return true;
            }).ToList();
            var filtered = fBrand.Length > 0 || fDiv.Length > 0 || fMC.Length > 0 || fMRP.Length > 0 || fMon.Length > 0 || fDec.Length > 0;

            if (tab == "B")
            {
                var groups = BuildEngineBGroups(baseRows);
                var breaching = groups.Where(g =>
                {
                    if (fDec.Length > 0)
                    {
                        if (fDec == Decisions.Cancel) return g.ColourDecision == "Cancel All" || g.ColourDecision == Decisions.Cancel;
                        return g.ColourDecision == fDec;
                    }
                    return g.ColourDecision != Decisions.Retain;
                }).ToList();
                JsSort.StableSort(breaching, (a, b) => a.Dm != b.Dm ? Tool2Js.LocaleCompare(a.Dm, b.Dm) : b.ExcessUnits - a.ExcessUnits);
                view.CountText = breaching.Count + " colour groups · Single Concept MRP only" + (filtered ? " (filtered)" : "");
                if (breaching.Count == 0) { view.Empty = true; view.Months = new(); return view; }
                var byMonth = new JsObjectMap<List<Tool2ColourGroupDto>>();
                foreach (var g in breaching) { if (!byMonth.ContainsKey(g.Dm)) byMonth[g.Dm] = new(); byMonth[g.Dm].Add(g); }
                var months = byMonth.Keys.ToList();
                months.Sort(string.CompareOrdinal);
                view.Months = months.Select(m =>
                {
                    var gs = byMonth[m];
                    double ex = 0, q = 0; foreach (var g in gs) { ex += g.ExcessUnits; q += g.TotalQty; }
                    return new Tool2MonthSectionDto
                    {
                        Month = m, Groups = gs, Excess = ex, TotalQty = q, GroupsText = gs.Count + " colour group" + (gs.Count > 1 ? "s" : ""),
                        ExcessText = "Excess: " + Js.Fmt(ex) + " units", TotalQtyText = "Total PO Qty: " + Js.Fmt(q)
                    };
                }).ToList();
                return view;
            }
            JsSort.StableSort(baseRows, (a, b) => b.RelQty - a.RelQty);
            view.CountText = baseRows.Count + " rows" + " · Multi Concept MRP only" + (filtered ? " (filtered)" : "");
            view.Empty = baseRows.Count == 0;
            view.Rows = baseRows.Select(r => r.Idx).ToList();
            return view;
        }

        // ── the whole dashboard ────────────────────────────────────────────

        public Tool2ParamsDto ParamsDto() => new()
        {
            T2 = P.T2, D = P.D, StrBuffer = Buf, EffT2 = Js.Round(EffT2), Mix = P.Mix, BorderTolerance = BorderTol, Wof = P.Wof, Peer = P.Peer,
            SeasonCurrent = P.SeasonCurrent?.ToList() ?? new(), SeasonFallback = P.SeasonFallback?.ToList() ?? new()
        };

        public Tool2ViewDto BuildView()
        {
            var v = new Tool2ViewDto
            {
                Params = ParamsDto(), Today = Ctx.Today,
                Lines = Results.Select(ToDto).ToList(),
                Kpis = Kpis(), Tree = Tree(), DivisionChart = DivisionChart(), MonthChart = MonthChart(), EngineSplit = EngineSplit(),
                Rollup = Rollup(null), DetailFilters = DetailFilters(), RollupFilters = RollupFilters(),
                Detail = Detail(new Tool2DetailFilterDto { Tab = "B" }),
                ColourGroups = BuildEngineBGroups(Results.Where(r => r.Engine == "B")),
                CatHeads = AllCatHeadNames(),
                StatusText = "✓ " + Results.Count + " lines assessed"
            };
            return v;
        }
    }
}
