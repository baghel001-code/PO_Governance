using System.Text.Json;
using System.Text.Json.Nodes;
using Application.Interfaces.V1;
using Application.Interfaces.V1.Analytics;
using Application.Services.V1.Analytics.Ai;
using DecisionEngine.Analytics.Ai;
using DecisionEngine.Analytics.AttrAnalysis;
using DecisionEngine.Analytics.Common;
using DecisionEngine.Analytics.Help;
using DecisionEngine.Analytics.Insights;
using DecisionEngine.Common;
using DecisionEngine.Model;
using DecisionEngine.Results.Analytics.Ai;
using DecisionEngine.Results.Analytics.AttrAnalysis;
using DecisionEngine.Results.Analytics.CatInsights;
using DecisionEngine.Results.Analytics.Common;
using DecisionEngine.Results.Analytics.PlannerDashboard;
using static POGovernance.Tests.Analytics.Screens.AnalyticsParity;

namespace POGovernance.Tests.Analytics.Screens
{
    /// <summary>
    /// Parity of the analytics screens with the original JS (tests/parity/analytics/run.js → expected.json).
    /// The C# DTOs are reduced to the same plain structures the harness extracted from the rendered HTML.
    /// </summary>
    public sealed class AnalyticsParityTests
    {
        private sealed class NoCache : IDatasetCache
        {
            public Task<EngineData> GetCurrentAsync(CancellationToken ct = default) => throw new NotSupportedException();
            public Task<EngineData> GetForVersionsAsync(IReadOnlyDictionary<string, string> versions, EngineParams p, CancellationToken ct = default) => throw new NotSupportedException();
            public void Invalidate() { }
        }

        private static void Eq(object? expected, object? actual, string where) =>
            Assert.True(Equals(expected, actual), where + ": expected «" + expected + "» but got «" + actual + "»");

        private static void EqList(IReadOnlyList<string> expected, IReadOnlyList<string> actual, string where)
        {
            Assert.True(expected.Count == actual.Count, where + ": count " + expected.Count + " vs " + actual.Count + " — expected [" + string.Join(" ; ", expected) + "] got [" + string.Join(" ; ", actual) + "]");
            for (var i = 0; i < expected.Count; i++) Eq(Norm(expected[i]), Norm(actual[i]), where + "[" + i + "]");
        }

        private static string? S(JsonNode? n) => n == null ? null : n.GetValueKind() == JsonValueKind.String ? n.GetValue<string>() : n.ToJsonString();

        /// <summary>Compares one parsed table: header-less rows → cells, optional row style / title and cell styles.</summary>
        private static void Table(JsonNode exp, List<(List<string> Cells, string? Style, string? Title, Dictionary<int, string?>? CellStyles)> rows, string where)
        {
            var er = exp["rows"]!.AsArray();
            Eq(er.Count, rows.Count, where + " rows");
            for (var i = 0; i < er.Count; i++)
            {
                EqList(Strings(er[i]!["cells"]), rows[i].Cells, where + " row " + i);
                if (rows[i].Style != null || S(er[i]!["style"]) != null) Eq(S(er[i]!["style"]), rows[i].Style, where + " row " + i + " style");
                if (rows[i].Title != null || S(er[i]!["title"]) != null) Eq(S(er[i]!["title"]), rows[i].Title, where + " row " + i + " title");
                if (rows[i].CellStyles != null)
                    foreach (var (ci, st) in rows[i].CellStyles!) Eq(st, S(er[i]!["cellStyles"]![ci]), where + " row " + i + " cell " + ci + " style");
            }
        }

        // ── Attribute Analysis ────────────────────────────────────────────────

        public static IEnumerable<object[]> AttrCases() => Names("attrAnalysis");

        [Theory]
        [MemberData(nameof(AttrCases))]
        public void AttributeAnalysis(int index, string name)
        {
            var c = Case("attrAnalysis", index);
            var data = Data(Strings(c["drop"]));
            var svc = new AttributeAnalysisEngine();
            var f = c["filters"]!.AsObject();
            var q = new AttrAnalysisQuery { Brand = S(f["brand"]), Mc = S(f["mc"]), MinStock = S(f["minStk"]) };
            var l1 = svc.GetLevel1(data, q);
            var e1 = c["level1"]!;
            EqList(Strings(c["brands"]), new[] { "" }.Concat(l1.Brands).ToList(), name + " brands");
            if (l1.State == "noData")
            {
                Eq(null, S(e1["crumb"]), name + " crumb");
                EqList(Strings(e1["empty"]), new[] { "📂" + l1.EmptyTitle + l1.EmptyMessage }, name + " empty");
                return;
            }
            Eq(Norm(S(e1["crumb"])), string.Join(" ▸ ", l1.Breadcrumb.Select(b => b.Text)), name + " crumb");
            if (l1.State == "noMatch")
            {
                EqList(Strings(e1["empty"]), new[] { l1.EmptyMessage! }, name + " empty");
                Eq(0, e1["tables"]!.AsArray().Count, name + " tables");
                return;
            }
            Eq(true, e1["hasLegend"]!.GetValue<bool>(), name + " legend");
            Table(e1["tables"]![0]!, l1.Rows.Select(r => (new List<string> { r.Brand, r.McCd, r.McDesc, r.StockText, r.Sales45Text, r.Buckets.ToString() },
                (string?)("cursor:pointer;border-left:3px solid " + r.Status.Color), (string?)r.Tooltip,
                (Dictionary<int, string?>?)new Dictionary<int, string?> { [3] = "text-align:right;font-family:var(--mono);color:" + r.Status.Color + ";font-weight:700" })).ToList(), name + " L1");

            if (c["attrCols"] != null) EqList(Strings(c["attrCols"]), AttrAnalysisTree.DiscoverAttrCols(data), name + " attrCols");
            foreach (var lv in c["levels"]!.AsArray())
            {
                var i = lv!["index"]!.GetValue<int>();
                var row = l1.Rows[i];
                Eq(S(lv["brand"]), row.Brand, name + " L2 brand " + i);
                Eq(S(lv["mcKey"]), row.McKey, name + " L2 mcKey " + i);
                var l2 = svc.GetLevel2(data, row.Brand, row.McKey);
                var e2 = lv["level2"]!;
                var w2 = name + " L2[" + row.McKey + "]";
                Eq(Norm(S(e2["crumb"])), Norm(string.Join(" ▸ ", l2.Breadcrumb.Select(b => b.Text))), w2 + " crumb");
                Table(e2["tables"]![0]!, l2.Rows.Select(r => (new List<string> { r.MrpText, r.Flag, r.StockText, r.Sales45Text, r.Colours.ToString() },
                    (string?)("cursor:pointer;border-left:3px solid " + r.Status.Color), (string?)r.Tooltip, (Dictionary<int, string?>?)null)).ToList(), w2);
                foreach (var l3e in lv["level3"]!.AsArray())
                {
                    var j = l3e!["index"]!.GetValue<int>();
                    var b = l2.Rows[j];
                    Eq(S(l3e["mrp"]), b.Mrp, w2 + " mrp " + j);
                    Eq(S(l3e["flag"]), b.Flag, w2 + " flag " + j);
                    var l3 = svc.GetLevel3(data, row.Brand, row.McKey, b.Mrp, b.Flag);
                    var e3 = l3e["view"]!;
                    var w3 = w2 + " L3[" + b.Mrp + "|" + b.Flag + "]";
                    Eq(Norm(S(e3["crumb"])), Norm(string.Join(" ▸ ", l3.Breadcrumb.Select(x => x.Text))), w3 + " crumb");
                    var ekv = e3["kv"]!.AsArray();
                    Eq(ekv.Count, l3.Summary.Count, w3 + " kv count");
                    for (var k = 0; k < ekv.Count; k++) { Eq(S(ekv[k]![0]), l3.Summary[k].Label, w3 + " kv"); Eq(S(ekv[k]![1]), l3.Summary[k].Value, w3 + " kv " + l3.Summary[k].Label); }
                    EqList(Strings(e3["notes"]), l3.Note == null ? new List<string>() : new List<string> { l3.Note }, w3 + " notes");
                    EqList(Strings(e3["titles"]), l3.AttributeTables.Select(t => t.Title).Append(l3.ColourTitle).ToList(), w3 + " titles");
                    Eq(e3["hasDevLegend"]!.GetValue<bool>(), l3.DeviationLegend.Count > 0, w3 + " dev legend");
                    var eb = e3["bands"]!.AsArray();
                    Eq(eb.Count, l3.Bands.Count, w3 + " bands");
                    for (var k = 0; k < eb.Count; k++)
                        EqList(Strings(eb[k]), new[] { l3.Bands[k].Color, l3.Bands[k].Label, l3.Bands[k].Count.ToString(), l3.Bands[k].StockText }, w3 + " band " + k);
                    var et = e3["tables"]!.AsArray();
                    Eq(et.Count, l3.AttributeTables.Count + 1, w3 + " table count");
                    for (var t = 0; t < l3.AttributeTables.Count; t++)
                    {
                        var at = l3.AttributeTables[t];
                        var rows = at.Rows.Select(r => (new List<string> { r.Value, r.StockText, r.StockMixText, r.Sales45Text, r.SalesMixText, r.DeviationText },
                            (string?)(r.Unmapped ? "color:var(--ink3);font-style:italic" : null), (string?)null,
                            (Dictionary<int, string?>?)new Dictionary<int, string?> { [5] = "text-align:right;font-family:var(--mono);font-weight:700;color:" + r.DeviationColor })).ToList();
                        var tot = at.Total;
                        rows.Add((new List<string> { tot.Value, tot.StockText, tot.StockMixText, tot.Sales45Text, tot.SalesMixText, tot.DeviationText },
                            "border-top:2px solid var(--brd);font-weight:700", null, null));
                        Table(et[t]!, rows, w3 + " " + at.Dimension);
                    }
                    Table(et[^1]!, l3.ColourRows.Select(r => (new List<string> { r.Colour, r.StockText, r.StockMixText, r.Sales45Text, r.SalesMixText, r.StrText },
                        (string?)("border-left:3px solid " + r.Status.Color), (string?)null,
                        (Dictionary<int, string?>?)new Dictionary<int, string?> { [5] = "text-align:right;font-family:var(--mono);font-weight:700;color:" + r.Status.Color })).ToList(), w3 + " colours");
                }
            }
        }

        // ── Dashboards ────────────────────────────────────────────────────────

        private static void Kpis(JsonNode? exp, List<KpiTileDto> kpis, string where)
        {
            var e = exp!.AsArray();
            Eq(e.Count, kpis.Count, where + " kpi count");
            for (var i = 0; i < e.Count; i++)
                EqList(new[] { S(e[i]!["bg"])!, S(e[i]!["c"])!, S(e[i]!["val"])!, S(e[i]!["label"])! },
                       new[] { kpis[i].Background, kpis[i].Color, kpis[i].Value, kpis[i].Label }, where + " kpi " + i);
        }

        private static void Mix(JsonNode v, DecisionMixDto mix, DriverBreakdownDto drivers, string where)
        {
            Eq(S(v["mixTitle"]), mix.Title, where + " mix title");
            var arcs = v["arcs"]!.AsArray();
            Eq(arcs.Count, mix.Segments.Count, where + " arcs");
            for (var i = 0; i < arcs.Count; i++)
            {
                var s = mix.Segments[i];
                EqList(Strings(arcs[i]), new[] { s.Color, s.DashArray, s.DashGap, s.DashOffset }, where + " arc " + i);
                EqList(Strings(v["legend"]![i]), new[] { s.Color, s.Decision, s.Count.ToString(), s.PctText }, where + " legend " + i);
            }
            Eq(S(v["driverTitle"]), drivers.Title, where + " driver title");
            Eq(S(v["driverEmpty"]), drivers.EmptyMessage, where + " driver empty");
            var dr = v["drivers"]!.AsArray();
            Eq(dr.Count, drivers.Rows.Count, where + " drivers");
            for (var i = 0; i < dr.Count; i++)
                EqList(Strings(dr[i]), new[] { drivers.Rows[i].Label, drivers.Rows[i].Count.ToString(), Js.NumberToString(drivers.Rows[i].Pct) }, where + " driver " + i);
        }

        private static void Recs(JsonNode v, RecommendationsCardDto recs, string where)
        {
            Eq(S(v["recTitle"]), recs.Title, where + " rec title");
            EqList(Strings(v["recs"]), recs.Items.Select(x => x.Html).ToList(), where + " recs");
        }

        public static IEnumerable<object[]> CatCases() => Names("catInsights");

        [Theory]
        [MemberData(nameof(CatCases))]
        public void CategoryInsights(int index, string name)
        {
            var c = Case("catInsights", index);
            var reqs = Requests(c["reqIdx"]);
            var f = Strings(c["filters"]);
            string? F(int i) => i < f.Count ? f[i] : null;
            var svc = new InsightsEngine();
            var dto = svc.BuildCategoryInsights(reqs, new CatInsightsQuery { Brand = F(0), Division = F(1), Mc = F(2), Month = F(3), Engine = F(4), Decision = F(5) });
            var v = c["view"]!;
            if (dto.State == "noRequests")
            {
                Eq(S(v["emptyTitle"]), dto.EmptyTitle, name + " empty title");
                Eq(Norm(S(v["emptySub"])), dto.EmptyMessage, name + " empty sub");
                return;
            }
            var o = c["options"]!;
            EqList(Strings(o["cd-f-brand"]), dto.Options.Brands.Prepend("").ToList(), name + " brands");
            EqList(Strings(o["cd-f-div"]), dto.Options.Divisions.Prepend("").ToList(), name + " divs");
            EqList(Strings(o["cd-f-mc"]), dto.Options.Mcs.Prepend("").ToList(), name + " mcs");
            EqList(Strings(o["cd-f-month"]), dto.Options.Months.Prepend("").ToList(), name + " months");
            var a = dto.Applied;
            EqList(Strings(c["applied"]), new[] { a.Brand!, a.Division!, a.Mc!, a.Month!, a.Engine!, a.Decision! }, name + " applied");
            if (dto.State == "noMatch")
            {
                Eq(S(v["emptyTitle"]), dto.EmptyTitle, name + " empty title");
                Eq(Norm(S(v["emptySub"])), dto.EmptyMessage, name + " empty sub");
                return;
            }
            Assert.Null(v["emptyTitle"]);
            Kpis(v["kpis"], dto.Kpis, name);
            Mix(v, dto.DecisionMix!, dto.Drivers!, name);
            var titles = new List<string> { dto.McRisk!.Title };
            if (dto.Flagged != null) titles.Add(dto.Flagged.Title);
            EqList(Strings(v["tableTitles"]), titles, name + " table titles");
            var tables = v["tables"]!.AsArray();
            Table(tables[0]!, dto.McRisk.Rows.Select(r => (new List<string> { r.Mc, r.Requests.ToString(), r.QtyText, r.Allow.ToString(), r.Watch.ToString(), r.Block.ToString(), Js.NumberToString(r.RiskPct) + "%" },
                (string?)null, (string?)null, (Dictionary<int, string?>?)new Dictionary<int, string?> { [6] = "font-family:var(--mono);font-weight:700;color:" + r.RiskColor })).ToList(), name + " mc");
            Eq(dto.Flagged == null ? 1 : 2, tables.Count, name + " table count");
            if (dto.Flagged != null)
                Table(tables[1]!, dto.Flagged.Rows.Select(r => (new List<string> { r.Id, r.Gm, r.Colour, r.McDesc, r.Decision, r.Driver }, (string?)null, (string?)null, (Dictionary<int, string?>?)null)).ToList(), name + " flagged");
            Recs(v, dto.Recommendations!, name);
        }

        public static IEnumerable<object[]> PlannerCases() => Names("planner");

        [Theory]
        [MemberData(nameof(PlannerCases))]
        public void PlannerDashboard(int index, string name)
        {
            var c = Case("planner", index);
            var rows = Tool2Rows(c["rowIdx"]);
            var f = Strings(c["filters"]);
            string? F(int i) => i < f.Count ? f[i] : null;
            var svc = new InsightsEngine();
            var dto = svc.BuildPlannerDashboard(rows, new PlannerDashboardQuery { Brand = F(0), Division = F(1), Mc = F(2), Mrp = F(3), Flag = F(4), DeliveryMonth = F(5), Engine = F(6), Decision = F(7) });
            Eq(S(c["placeholder"]), dto.State == "noRun" ? "block" : "none", name + " placeholder");
            Eq(S(c["filtersBox"]), dto.State == "noRun" ? "none" : "block", name + " filters box");
            if (dto.State == "noRun") return;
            var o = c["options"]!;
            EqList(Strings(o["pd-f-brand"]), dto.Options.Brands.Prepend("").ToList(), name + " brands");
            EqList(Strings(o["pd-f-div"]), dto.Options.Divisions.Prepend("").ToList(), name + " divs");
            EqList(Strings(o["pd-f-mc"]), dto.Options.Mcs.Prepend("").ToList(), name + " mcs");
            EqList(Strings(o["pd-f-mrp"]), dto.Options.Mrps.Prepend("").ToList(), name + " mrps");
            EqList(Strings(o["pd-f-flag"]), dto.Options.Flags.Prepend("").ToList(), name + " flags");
            EqList(Strings(o["pd-f-dm"]), dto.Options.DeliveryMonths.Prepend("").ToList(), name + " dms");
            var a = dto.Applied;
            EqList(Strings(c["applied"]), new[] { a.Brand!, a.Division!, a.Mc!, a.Mrp!, a.Flag!, a.DeliveryMonth!, a.Engine!, a.Decision! }, name + " applied");
            var v = c["view"]!;
            if (dto.State == "noMatch")
            {
                Eq(S(v["emptyTitle"]), dto.EmptyTitle, name + " empty title");
                Eq(Norm(S(v["emptySub"])), dto.EmptyMessage, name + " empty sub");
                return;
            }
            Kpis(v["kpis"], dto.Kpis, name);
            Mix(v, dto.DecisionMix!, dto.Drivers!, name);
            EqList(Strings(v["tableTitles"]), new[] { dto.McRisk!.Title }, name + " table titles");
            Table(v["tables"]![0]!, dto.McRisk.Rows.Select(r => (new List<string> { r.Mc, r.Lines.ToString(), r.Retain.ToString(), r.Watch.ToString(), r.Cut.ToString(), r.ValueAtRiskText, Js.NumberToString(r.RiskPct) + "%" },
                (string?)null, (string?)null, (Dictionary<int, string?>?)new Dictionary<int, string?> { [6] = "font-family:var(--mono);font-weight:700;color:" + r.RiskColor })).ToList(), name + " mc");
            Recs(v, dto.Recommendations!, name);
        }

        // ── AI chat ───────────────────────────────────────────────────────────

        public static IEnumerable<object[]> ChatCases() => Names("chat");

        private static AiGatewayResult HarnessChatResponse(int n) => n switch
        {
            3 => new AiGatewayResult(503, JsonNode.Parse("{\"error\":\"AI chat is not enabled\"}")),
            4 => new AiGatewayResult(500, JsonNode.Parse("{\"error\":{\"message\":\"overloaded\"}}")),
            _ => new AiGatewayResult(200, JsonNode.Parse("{\"content\":[{\"type\":\"text\",\"text\":\" Answer " + n + "\"},{\"type\":\"tool_use\"},{\"type\":\"text\",\"text\":\"line2 \"}]}"))
        };

        [Theory]
        [MemberData(nameof(ChatCases))]
        public async Task Chat(int index, string name)
        {
            var c = Case("chat", index);
            var tool2 = S(c["role"]) == "planner";
            var reqs = Requests(c["reqIdx"]);
            var rows = Tool2Rows(c["rowIdx"]);
            var gw = new ScriptedGateway(HarnessChatResponse);
            var svc = new AiAssistantService(gw, new ListHistorySource(reqs), new ListRunSource(rows), new FixedClock(), new AiAssistantEngine());
            var history = new List<AiChatMessageDto>();
            var turns = c["turns"]!.AsArray();
            for (var t = 0; t < turns.Count; t++)
            {
                var et = turns[t]!;
                var res = await svc.ChatAsync(new AssistantChatRequest { Scope = tool2 ? "tool2" : "tool1", Question = S(et["question"])!, History = history }, tool2 ? Staff : Buyer);
                var call = gw.ChatCalls[^1];
                Eq(S(et["system"]), call.System, name + " turn " + t + " system");
                Eq(1000, call.MaxTokens, name + " max tokens");
                var em = et["messages"]!.AsArray();
                Eq(em.Count, call.Messages.Count, name + " turn " + t + " messages");
                for (var m = 0; m < em.Count; m++)
                {
                    Eq(S(em[m]!["role"]), call.Messages[m].Role, name + " turn " + t + " msg " + m + " role");
                    Eq(S(em[m]!["content"]), call.Messages[m].Content, name + " turn " + t + " msg " + m + " content");
                }
                Eq(S(et["lastBubble"]), res.Ok ? res.Answer : res.Error, name + " turn " + t + " bubble");
                history = res.History;
            }
            var ctxs = c["contexts"]!.AsArray();
            for (var t = 0; t < ctxs.Count; t++)
            {
                var q = S(turns[t]!["question"]);
                var (summary, sample) = tool2 ? AiChatContext.Tool2(rows, q) : AiChatContext.Tool1(reqs, q);
                Eq(S(ctxs[t]!["summary"]), AnalyticsJs.Stringify(summary), name + " ctx summary " + t);
                Eq(S(ctxs[t]!["sample"]), AnalyticsJs.Stringify(sample), name + " ctx sample " + t);
            }
        }

        // ── AI Insights ───────────────────────────────────────────────────────

        public static IEnumerable<object[]> InsightCases() => Names("insights");

        [Theory]
        [MemberData(nameof(InsightCases))]
        public async Task Insights(int index, string name)
        {
            var c = Case("insights", index);
            var rows = Tool2Rows(c["rowIdx"]);
            var focus = S(c["focus"]);
            var summary = AiInsightsBuilder.BuildSummary(rows);
            var es = c["summary"];
            if (es == null) Assert.Null(summary);
            else
            {
                Assert.NotNull(summary);
                EqList(Strings(es["lines"]), summary!.Lines, name + " lines");
                Eq(es["totalLines"]!.GetValue<int>(), summary.TotalLines, name + " totalLines");
                Eq(es["flaggedCount"]!.GetValue<int>(), summary.FlaggedCount, name + " flagged");
                Eq(es["totalExcess"]!.GetValue<double>(), summary.TotalExcess, name + " totalExcess");
                foreach (var (key, list) in new[] { ("topVendors", summary.TopVendors), ("topDeliveryMonths", summary.TopDeliveryMonths), ("topMRPs", summary.TopMRPs), ("topMCs", summary.TopMCs) })
                {
                    var e = es[key]!.AsArray();
                    Eq(e.Count, list.Count, name + " " + key);
                    for (var i = 0; i < e.Count; i++)
                    {
                        Eq(S(e[i]!["key"]), list[i].Key, name + " " + key + " key " + i);
                        Eq(e[i]!["count"]!.GetValue<int>(), list[i].Count, name + " " + key + " count " + i);
                        Eq(e[i]!["excess"]!.GetValue<double>(), list[i].Excess, name + " " + key + " excess " + i);
                    }
                }
                Eq(es["byDecision"]!.ToJsonString(), AiInsightsBuilder.ByDecisionJson(summary), name + " byDecision");
                Eq(S(c["prompt"]), AiInsightsBuilder.PromptFor(string.IsNullOrEmpty(focus) ? "risk" : focus, summary), name + " prompt");
            }

            var resp = c["response"]!;
            var gw = new ScriptedGateway(insights: new AiGatewayResult(resp["status"]!.GetValue<int>(), resp["data"]?.DeepClone()));
            var svc = new AiAssistantService(gw, new ListHistorySource(new()), new ListRunSource(rows), new FixedClock(), new AiAssistantEngine());
            var dto = await svc.InsightsAsync(new AiInsightsRequest { Focus = focus }, Staff);
            EqList(Strings(c["toasts"]), dto.Toast == null ? new List<string>() : new List<string> { dto.Toast }, name + " toasts");
            if (summary == null) return;
            Eq(c["prompt"]!.GetValue<string>(), gw.Prompts.Single(), name + " sent prompt");
            var ek = c["kpis"]!.AsArray();
            Eq(ek.Count, dto.Kpis.Count, name + " kpis");
            for (var i = 0; i < ek.Count; i++) EqList(Strings(ek[i]), new[] { dto.Kpis[i].Color, dto.Kpis[i].Value, dto.Kpis[i].Label }, name + " kpi " + i);
            var ec = c["cards"]!.AsArray();
            Eq(ec.Count, dto.Findings.Count, name + " cards");
            for (var i = 0; i < ec.Count; i++)
            {
                var card = dto.Findings[i];
                Eq(S(ec[i]![0]), card.Icon + " " + card.Title, name + " card title " + i);
                Eq(S(ec[i]![1]), card.ImpactQtyText, name + " card qty " + i);
                Eq(S(ec[i]![2]), card.Insight, name + " card insight " + i);
            }
            var cal = c["callout"];
            if (cal == null) Assert.Null(dto.Action);
            else EqList(Strings(cal), new[] { dto.Action!.Icon + " " + dto.Action.Label, dto.Action.Title, dto.Action.Detail }, name + " callout");
            Eq(S(c["meta"]), dto.Meta, name + " meta");
            Eq(S(c["fallback"]), dto.State is "fallback" or "error" ? dto.FallbackText : null, name + " fallback");
        }

        // ── How it works ──────────────────────────────────────────────────────

        [Fact]
        public void HelpValues()
        {
            var svc = new HelpContentEngine();
            var cases = Expected["help"]!.AsArray();
            Assert.NotEmpty(cases);
            foreach (var c in cases)
            {
                var p = c!["params"]!.AsObject();
                double P(string k, double dflt) => p[k] == null ? dflt : p[k]!.GetValue<double>();
                var ep = new EngineParams { T1 = P("T1", 0), T2 = P("T2", 0), D = P("D", 0), Mix = P("mix", 0), N = P("N", 0), MixWindowDays = P("mixWindowDays", 0), StrBuffer = P("strBuffer", 0) };
                var dto = svc.Compute(ep);
                var map = new Dictionary<string, string>
                {
                    ["hwb-D"] = dto.D, ["hwb2-D"] = dto.D, ["hwb-T"] = dto.Tool1.T, ["hwb2-T"] = dto.Tool2.T, ["hwb-buf"] = dto.Buffer, ["hwb2-buf"] = dto.Buffer,
                    ["hwb-effT"] = dto.Tool1.EffT, ["hwb2-effT"] = dto.Tool2.EffT, ["hwb-mix"] = dto.Mix, ["hwb2-mix"] = dto.Mix, ["hwb-N"] = dto.N, ["hwb2-N"] = dto.N,
                    ["hwb-mwd"] = dto.MixWindowDays, ["hwb2-mwd"] = dto.MixWindowDays, ["hwb-maxperm-calc"] = dto.Tool1.Calc, ["hwb2-maxperm-calc"] = dto.Tool2.Calc,
                    ["hwb-maxperm-result"] = dto.Tool1.Result, ["hwb2-maxperm-result"] = dto.Tool2.Result
                };
                var vals = c["values"]!.AsObject();
                Eq(vals.Count, map.Count, "help value count");
                foreach (var kv in vals)
                {
                    var exp = kv.Value!.GetValueKind() == JsonValueKind.Number ? Js.NumberToString(kv.Value.GetValue<double>()) : kv.Value.GetValue<string>();
                    Eq(exp, map[kv.Key], "help " + p.ToJsonString() + " " + kv.Key);
                }
            }
        }
    }
}
