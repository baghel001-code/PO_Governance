using System.Text.Json.Nodes;
using Application.DTOs.Analytics.Common;
using Application.DTOs.Analytics.PlannerDashboard;
using Application.Interfaces.V1.Analytics;
using Application.Services.V1.Analytics.Common;
using Application.Services.V1.Engines.Common;
using Domain.Entities.Common;
using Domain.Exceptions;
using Domain.Records;

namespace Application.Services.V1.Analytics.PlannerDashboard
{
    /// <summary>
    /// Port of index.html <c>renderPlannerDashboard</c> (line 9062) and <c>_plannerDashResetFilters</c>
    /// (reset = send an empty query): the Tool 2 run dashboard (KPIs, decision donut, drivers, MC risk
    /// with value at risk, recommendations for the planning team).
    /// </summary>
    public sealed class PlannerDashboardService : IPlannerDashboardService
    {
        public static readonly string[] DecisionOrder = { "Retain", "Watch List", "Reduce Qty", "Cancel", "Cannot Validate" };
        public static readonly IReadOnlyDictionary<string, string> DecisionColors = new Dictionary<string, string>
        {
            ["Retain"] = "#00873E", ["Watch List"] = "#B45309", ["Reduce Qty"] = "#D97706", ["Cancel"] = "#DC2626", ["Cannot Validate"] = "#6B7280"
        };
        public static readonly List<OptionDto> EngineOptions = new() { new("", "All Engines"), new("B", "Single Concept MRP"), new("A", "Multi Concept MRP") };
        public static readonly List<OptionDto> DecisionOptions = new()
        {
            new("", "All Decisions"), new("Retain", "Retain"), new("Watch List", "Watch List"), new("Reduce Qty", "Reduce Qty"),
            new("Cancel", "Cancel"), new("Cannot Validate", "Cannot Validate")
        };

        private readonly ITool2ResultSource _runs;

        public PlannerDashboardService(ITool2ResultSource runs) => _runs = runs;

        public async Task<PlannerDashboardDto> GetAsync(PlannerDashboardQuery? query, AppUserContext user, CancellationToken ct = default)
        {
            query ??= new PlannerDashboardQuery();
            Tool2RunRows? run;
            if (!string.IsNullOrEmpty(query.RunId))
            {
                run = await _runs.GetRunRowsAsync(query.RunId!, user, ct);
                if (run == null) throw new NotFoundException("Run not found");
            }
            else run = await _runs.GetLatestRowsAsync(user, ct);
            var dto = Build(run?.Rows ?? Array.Empty<JsonObject>(), query);
            dto.RunId = run?.RunId;
            return dto;
        }

        private static string Brand(JsonObject r) => AnalyticsJs.InfoStr(r, "brand", "NON-PL");
        private static string Div(JsonObject r) => AnalyticsJs.OrStr(r, "div", "—");
        private static string Mc(JsonObject r) => AnalyticsJs.OrStr(r, "mcDesc", "Unclassified");
        private static string Mrp(JsonObject r) => AnalyticsJs.OrStr(r, "mrp", "—");
        private static string Flag(JsonObject r) => AnalyticsJs.InfoStr(r, "flag", "—");
        private static string Dm(JsonObject r) => AnalyticsJs.OrStr(r, "dm", "—");
        private static string? Str(JsonObject r, string key) => AnalyticsJs.Get(r, key) as string;

        /// <summary><c>r.excessQty!=null ? r.excessQty : r.relQty</c>.</summary>
        public static double CutQty(JsonObject r) => AnalyticsJs.NotNull(r, "excessQty") ? AnalyticsJs.Arith(r, "excessQty") : AnalyticsJs.Arith(r, "relQty");

        /// <summary><c>r.poRate!=null ? r.poRate : Number(r.mrp)</c>, counted only when finite and &gt; 0.</summary>
        public static double ValueOf(JsonObject r, double cutQty)
        {
            var rate = AnalyticsJs.NotNull(r, "poRate") ? AnalyticsJs.Arith(r, "poRate") : AnalyticsJs.Number(r, "mrp");
            return double.IsFinite(rate) && rate > 0 ? rate * cutQty : 0;
        }

        public PlannerDashboardDto Build(IReadOnlyList<JsonObject> all, PlannerDashboardQuery? query)
        {
            query ??= new PlannerDashboardQuery();
            var dto = new PlannerDashboardDto { TotalAll = all.Count };
            dto.Options.Engines = EngineOptions;
            dto.Options.Decisions = DecisionOptions;
            if (all.Count == 0) { dto.State = "noRun"; return dto; }

            dto.Options.Brands = AnalyticsJs.SortStrings(AnalyticsJs.Distinct(all.Select(Brand)));
            dto.Options.Divisions = AnalyticsJs.SortStrings(AnalyticsJs.Distinct(all.Select(Div)));
            dto.Options.Mcs = AnalyticsJs.SortStrings(AnalyticsJs.Distinct(all.Select(Mc)));
            var mrps = AnalyticsJs.Distinct(all.Select(Mrp));
            JsSort.StableSort(mrps, (a, b) => JsOps.Or(Js.ParseFloat(a), 0) - JsOps.Or(Js.ParseFloat(b), 0));
            dto.Options.Mrps = mrps;
            dto.Options.Flags = AnalyticsJs.SortStrings(AnalyticsJs.Distinct(all.Select(Flag)));
            dto.Options.DeliveryMonths = AnalyticsJs.SortStrings(AnalyticsJs.Distinct(all.Select(Dm)));

            var f = new PlannerDashboardQuery
            {
                RunId = query.RunId,
                Brand = DashboardMath.Preserve(query.Brand, dto.Options.Brands),
                Division = DashboardMath.Preserve(query.Division, dto.Options.Divisions),
                Mc = DashboardMath.Preserve(query.Mc, dto.Options.Mcs),
                Mrp = DashboardMath.Preserve(query.Mrp, dto.Options.Mrps),
                Flag = DashboardMath.Preserve(query.Flag, dto.Options.Flags),
                DeliveryMonth = DashboardMath.Preserve(query.DeliveryMonth, dto.Options.DeliveryMonths),
                Engine = DashboardMath.StaticSelect(query.Engine, EngineOptions),
                Decision = DashboardMath.StaticSelect(query.Decision, DecisionOptions)
            };
            dto.Applied = f;
            var rows = all.Where(r =>
            {
                if (f.Brand!.Length > 0 && Brand(r) != f.Brand) return false;
                if (f.Division!.Length > 0 && Div(r) != f.Division) return false;
                if (f.Mc!.Length > 0 && Mc(r) != f.Mc) return false;
                if (f.Mrp!.Length > 0 && Mrp(r) != f.Mrp) return false;
                if (f.Flag!.Length > 0 && Flag(r) != f.Flag) return false;
                if (f.DeliveryMonth!.Length > 0 && Dm(r) != f.DeliveryMonth) return false;
                if (f.Engine!.Length > 0 && Str(r, "engine") != f.Engine) return false;
                if (f.Decision!.Length > 0 && Str(r, "decision") != f.Decision) return false;
                return true;
            }).ToList();
            dto.FilterActive = new[] { f.Brand, f.Division, f.Mc, f.Mrp, f.Flag, f.DeliveryMonth, f.Engine, f.Decision }.Any(x => x!.Length > 0);

            if (rows.Count == 0)
            {
                dto.State = "noMatch";
                dto.EmptyTitle = "No lines match these filters";
                dto.EmptyMessage = all.Count + " lines exist in this assessment run, but none match the current filter combination.";
                return dto;
            }

            var total = rows.Count;
            dto.Total = total;
            var decCounts = new JsObjectMap<int>();
            foreach (var r in rows) { var k = AnalyticsJs.Concat(r, "decision"); decCounts[k] = decCounts.Get(k) + 1; }
            int C(string d) => decCounts.Get(d);
            var flagged = rows.Where(r => Str(r, "decision") is not ("Retain" or "Cannot Validate")).ToList();
            double qtyAtRisk = 0, valueAtRisk = 0;
            foreach (var r in flagged) qtyAtRisk += CutQty(r);
            foreach (var r in flagged) valueAtRisk += ValueOf(r, CutQty(r));
            dto.QtyAtRisk = qtyAtRisk;
            dto.ValueAtRisk = valueAtRisk;
            var cutCt = C("Reduce Qty") + C("Cancel");
            var cutRate = Js.Round((double)cutCt / total * 100);
            var watchRate = Js.Round((double)C("Watch List") / total * 100);
            var retainRate = Js.Round((double)C("Retain") / total * 100);
            string N(double v) => Js.NumberToString(v);

            dto.Kpis = new List<KpiTileDto>
            {
                new() { Label = "Total Lines", Value = total + (dto.FilterActive ? " / " + all.Count : ""), Color = "#1A4FAF", Background = "#EAF0FB" },
                new() { Label = "Retain", Value = C("Retain") + " (" + N(retainRate) + "%)", Color = "#00873E", Background = "#DCFCE7" },
                new() { Label = "Watch List", Value = C("Watch List") + " (" + N(watchRate) + "%)", Color = "#B45309", Background = "#FFF3CD" },
                new() { Label = "Reduce/Cancel", Value = cutCt + " (" + N(cutRate) + "%)", Color = "#DC2626", Background = "#FEF2F2" },
                new() { Label = "Qty at Risk", Value = Js.Fmt(qtyAtRisk), Color = "#12213D", Background = "#F5F8FF" },
                new() { Label = "Value at Risk (₹)", Value = Js.Fmt(valueAtRisk), Color = "#7C3AED", Background = "#F5F3FF" }
            };
            dto.DecisionMix = new DecisionMixDto
            {
                Title = "📊 Decision Mix" + (dto.FilterActive ? " (filtered)" : ""),
                Segments = DashboardMath.Donut(DecisionOrder, C, total, d => (DecisionColors[d], null, null))
            };

            var driverCounts = new JsObjectMap<int>();
            foreach (var r in flagged) { var b = DashboardMath.DriverBucketT2(AnalyticsJs.Get(r, "decisionReason")); driverCounts[b] = driverCounts.Get(b) + 1; }
            var driverRows = DashboardMath.DriverRows(driverCounts, flagged.Count);
            dto.Drivers = new DriverBreakdownDto
            {
                Title = "🔎 What's Driving Reduce / Cancel / Watch List", Rows = driverRows,
                EmptyMessage = driverRows.Count > 0 ? null : "Nothing flagged in this view — every matching line is within stock-cover threshold. 🎉"
            };

            var mcMap = new JsObjectMap<PlannerMcRiskRowDto>();
            foreach (var r in rows)
            {
                var mc = Mc(r);
                if (!mcMap.TryGetValue(mc, out var m)) mcMap[mc] = m = new PlannerMcRiskRowDto { Mc = mc };
                m.Lines++;
                var d = Str(r, "decision");
                if (d == "Retain") m.Retain++;
                else if (d == "Watch List") m.Watch++;
                else if (d is "Reduce Qty" or "Cancel")
                {
                    m.Cut++;
                    var cq = CutQty(r);
                    m.QtyAtRisk += cq;
                    m.ValueAtRisk += ValueOf(r, cq);
                }
            }
            var mcRowsAll = mcMap.Values.ToList();
            JsSort.StableSort(mcRowsAll, (a, b) => (double)(b.Cut + b.Watch) / b.Lines - (double)(a.Cut + a.Watch) / a.Lines);
            foreach (var m in mcRowsAll)
            {
                m.RiskPct = Js.Round((double)(m.Cut + m.Watch) / m.Lines * 100);
                m.RiskColor = DashboardMath.RiskColor(m.RiskPct);
                m.ValueAtRiskText = Js.Fmt(m.ValueAtRisk);
            }
            dto.McRisk = new PlannerMcRiskTableDto
            {
                Title = "Risk by Merchandise Category (MC)" + (mcRowsAll.Count > 20 ? " — top 20 of " + mcRowsAll.Count : ""),
                TotalMcs = mcRowsAll.Count, Rows = mcRowsAll.Take(20).ToList()
            };

            var recs = new List<string>();
            if (cutRate >= 30)
            {
                var top = driverRows.Count > 0 ? driverRows[0].Label : null;
                recs.Add("🚨 <strong>" + N(cutRate) + "% of lines in this view need Reduce/Cancel action</strong>" + (top != null ? " — leading driver: <strong>" + top + "</strong>" : "") + ", worth ₹" + Js.Fmt(valueAtRisk) + ". Prioritise these before the next PO cycle.");
            }
            else if (cutCt > 0)
                recs.Add("⚠️ " + cutCt + " line(s) need Reduce/Cancel action, worth ₹" + Js.Fmt(valueAtRisk) + ". See the MC table below for where it's concentrated.");
            if (C("Watch List") > 0)
                recs.Add("👀 " + C("Watch List") + " line(s) on Watch List need Category Head review before deciding Retain vs Cut.");
            var riskiest = mcRowsAll.FirstOrDefault(v => v.Lines >= 2 && (double)(v.Cut + v.Watch) / v.Lines >= 0.4);
            if (riskiest != null)
                recs.Add("📌 <strong>" + riskiest.Mc + "</strong> is the riskiest MC in this view — " + N(riskiest.RiskPct) + "% Reduce/Cancel/Watch, ₹" + Js.Fmt(riskiest.ValueAtRisk) + " at risk. Check whether this is category-wide or specific to certain vendors/colours.");
            if (recs.Count == 0)
                recs.Add("✅ Everything in this view is Retain, within stock-cover threshold — no red flags detected.");
            dto.Recommendations = new RecommendationsCardDto { Title = "💡 Recommendations for the Planning Team", Items = recs.Select(DashboardMath.Rec).ToList() };
            return dto;
        }
    }
}
