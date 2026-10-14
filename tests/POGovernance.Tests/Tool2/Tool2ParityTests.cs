using System.Text.Json;
using System.Text.Json.Nodes;
using Application.DTOs.Tool2;
using Application.Services.V1.Tool2;
using Domain.Entities.Common;

namespace POGovernance.Tests.Tool2
{
    /// <summary>
    /// Tool 2 parity: the C# port against the original v1.1.4 JavaScript run on the same fixture
    /// (Tool2/Fixtures/expected.json from tests/parity/tool2/run-parity.js).
    /// </summary>
    public class Tool2ParityTests
    {
        public static IEnumerable<object[]> Scenarios() => Tool2Fixture.ScenarioNames();

        private static Dictionary<int, Tool2LineDto> Lines(Tool2Workspace ws) => ws.Results.Select(ws.ToDto).ToDictionary(l => l.Idx);

        [Theory, MemberData(nameof(Scenarios))]
        public void Batch_results_and_saved_payload_match(string sc)
        {
            var ws = Tool2Fixture.Workspace(sc);
            var exp = Tool2Fixture.Result(sc);
            Cmp.Check(exp["saved"]!["packed"], Tool2RunPayload.Pack(ws.Results), sc + ".packed");
            var (counts, atRisk, value) = Tool2Service.RunSummary(ws.Results);
            Cmp.Check(exp["summary"], new JsonObject { ["counts"] = counts, ["atRisk"] = atRisk, ["valueAtRisk"] = value }, sc + ".summary");
            Assert.Equal(exp["status"]!.GetValue<string>(), ws.BuildView().StatusText);
        }

        private static void CheckLineTexts(string sc, Tool2Workspace ws)
        {
            var exp = Tool2Fixture.Result(sc)["lines"]!.AsArray();
            Assert.Equal(exp.Count, ws.Results.Count);
            for (var i = 0; i < exp.Count; i++)
            {
                var e = exp[i]!.AsObject();
                var r = ws.Results[i];
                var w = $"{sc} line {r.Idx} ({r.Gm}/{r.Colour})";
                Assert.Equal(e["tooltip"]!.GetValue<string>(), ws.Tooltip(r));
                Cmp.Tokens(e["reason"], Tool2Tokens.Reason(ws.DetailedReason(r)), w + " reason");
                Assert.Equal(e["recommendedActionA"]!.GetValue<string>(), ws.RecommendedActionA(r));
                Assert.Equal(Cmp.Strings(e["rankedOptions"]), ws.RankedOptionsAny(r, 4));
                var rc = ws.ReduceCancelOptions(r, 3);
                Assert.Equal(Cmp.Strings(e["rcOptions"]), rc);
                Assert.Equal(e["rcRecommendation"]!.GetValue<string>(), ws.ReduceCancelRecommendation(r, rc));
                Assert.Equal(e["poStatus"]!.GetValue<string>(), Tool2Workspace.PoStatusFlag(r));
                Cmp.Check(e["q1PeerAvg"], Cmp.Node(ws.Q1PeerAvgPdstr(r)), w + " q1PeerAvg");
                Assert.Equal(e["allSiblings"]!.GetValue<string>(), ws.AllSiblingsWithPdstr(r));
                Assert.Equal(Cmp.Strings(e["bobsQ1"]), ws.BobsQ1OptionsArray(r, 3));
                Assert.Equal(e["trueRepeatRec"]!.GetValue<string>(), ws.TrueRepeatRecommendation(r));
                Cmp.Check(e["bucket"], Cmp.Node(Tool2Workspace.BucketFor(r)), w + " bucket");
                Assert.Equal(e["colourOptionsText"]!.GetValue<string>(),
                    Tool2Workspace.FormatColourOptions(ws.RankedColoursB(r, 3), "These colours in this MC currently have healthy sell-through: "));
                Assert.Equal(e["driver"]!.GetValue<string>(), Tool2Workspace.DriverBucket(r.DecisionReason));
                Assert.Equal(e["catHead"]!.GetValue<string>(), ws.CatHeadFor(r));
            }
        }

        [Theory, MemberData(nameof(Scenarios))]
        public void Line_texts_match(string sc) => CheckLineTexts(sc, Tool2Fixture.Workspace(sc));

        [Theory, MemberData(nameof(Scenarios))]
        public void Reopened_v114_run_shows_the_same(string sc) => CheckLineTexts(sc, Tool2Fixture.ReopenedJsRun(sc));

        [Theory, MemberData(nameof(Scenarios))]
        public void Dashboard_matches(string sc)
        {
            var ws = Tool2Fixture.Workspace(sc);
            var exp = Tool2Fixture.Result(sc);
            Cmp.Tokens(exp["kpi"], Tool2Tokens.Kpis(ws.Kpis()), "kpi");
            var div = ws.DivisionChart();
            Cmp.Tokens(exp["divChart"]!["tokens"], Tool2Tokens.DivChart(div), "divChart");
            Cmp.Check(exp["divChart"]!["widths"], Cmp.Node(div.SelectMany(b => b.Widths).ToList()), "divChart.widths");
            var mon = ws.MonthChart();
            Cmp.Tokens(exp["monthChart"]!["tokens"], Tool2Tokens.MonthChart(mon), "monthChart");
            Cmp.Check(exp["monthChart"]!["widths"], Cmp.Node(mon.SelectMany(b => b.Widths).ToList()), "monthChart.widths");
            var es = ws.EngineSplit();
            Cmp.Tokens(exp["engineSplit"]!["tokens"], es.SelectMany(e => new[] { e.Label, e.Count.ToString() }).ToList(), "engineSplit");
            Cmp.Check(exp["engineSplit"]!["widths"], Cmp.Node(es.Select(e => e.Width).ToList()), "engineSplit.widths");

            var fo = exp["filterOptions"]!.AsObject();
            var d = ws.DetailFilters(); var ro = ws.RollupFilters();
            Assert.Equal(Cmp.Strings(fo["ass-filter-brand"]), d.Brands);
            Assert.Equal(Cmp.Strings(fo["ass-filter-div"]), d.Divisions);
            Assert.Equal(Cmp.Strings(fo["ass-filter-mc"]), d.Mcs);
            Assert.Equal(Cmp.Strings(fo["ass-filter-mrp"]), d.Mrps);
            Assert.Equal(Cmp.Strings(fo["ass-filter-mon"]), d.Months);
            Assert.Equal(Cmp.Strings(fo["rollup-filter-div"]), ro.Divisions);
            Assert.Equal(Cmp.Strings(fo["rollup-filter-mc"]), ro.Mcs);
            Assert.Equal(Cmp.Strings(fo["rollup-filter-mrp"]), ro.Mrps);
            Assert.Equal(Cmp.Strings(fo["rollup-filter-mon"]), ro.Months);
            Assert.Equal(Cmp.Strings(exp["catHeads"]), ws.AllCatHeadNames());

            // tree
            JsonObject Node(Tool2TreeNodeDto n)
            {
                var o = new JsonObject
                {
                    ["label"] = n.Key, ["total"] = n.Total, ["retain"] = n.Retain, ["reduce"] = n.Reduce, ["cancel"] = n.Cancel, ["watch"] = n.Watch, ["atRisk"] = n.AtRisk
                };
                var kids = new JsonArray(n.Children.Select(c => (JsonNode)Node(c)).ToArray());
                if (n.Level == 0) o["divs"] = kids; else if (n.Level == 1) o["mcs"] = kids; else if (n.Level == 2) o["mrps"] = kids;
                else o["rows"] = new JsonArray(n.Rows!.Select(i => (JsonNode)JsonValue.Create(i - 1)!).ToArray());
                return o;
            }
            Cmp.Check(exp["tree"], new JsonArray(ws.Tree().Select(n => (JsonNode)Node(n)).ToArray()), "tree");

            // Single Concept colour groups
            Cmp.Check(exp["groupsB"], Cmp.Node(ws.BuildEngineBGroups(ws.Results.Where(r => r.Engine == "B")).ToList()), "groupsB");
        }

        [Theory, MemberData(nameof(Scenarios))]
        public void Detail_views_match(string sc)
        {
            var ws = Tool2Fixture.Workspace(sc);
            var lines = Lines(ws);
            var exp = Tool2Fixture.Result(sc)["detail"]!.AsArray();
            var cases = Tool2Fixture.Scenario(sc)["detailCases"]!.AsArray();
            for (var i = 0; i < exp.Count; i++)
            {
                var c = cases[i]!.AsObject(); var f = c["f"]?.AsObject();
                string? F(string k) => f?[k]?.GetValue<string>();
                var v = ws.Detail(new Tool2DetailFilterDto { Tab = c["tab"]!.GetValue<string>(), Brand = F("brand"), Div = F("div"), Mc = F("mc"), Mrp = F("mrp"), Mon = F("mon"), Dec = F("dec") });
                var e = exp[i]!.AsObject(); var name = sc + " " + c["name"];
                Assert.Equal(e["count"]!.GetValue<string>(), v.CountText);
                Assert.Equal(e["noAction"]!.GetValue<string>() == "block", v.Empty);
                Cmp.Tokens(e["header"], v.Header.Select(h => h.Label).ToList(), name + " header");
                Assert.Equal(string.Join(" ", Cmp.Strings(e["banner"])).Replace(" ,", ","), v.BannerTitle + " — " + v.BannerText);
                if (!v.Empty) Cmp.Tokens(e["body"], Tool2Tokens.Detail(v, lines), name + " body");
            }
        }

        [Theory, MemberData(nameof(Scenarios))]
        public void Rollup_tables_match(string sc)
        {
            var ws = Tool2Fixture.Workspace(sc);
            var exp = Tool2Fixture.Result(sc)["rollup"]!.AsArray();
            var cases = Tool2Fixture.Scenario(sc)["rollupCases"]!.AsArray();
            for (var i = 0; i < exp.Count; i++)
            {
                var f = cases[i]!["f"]?.AsObject();
                string? F(string k) => f?[k]?.GetValue<string>();
                var d = ws.Rollup(new Tool2RollupFilterDto { Div = F("div"), Mc = F("mc"), Mrp = F("mrp"), Mon = F("mon"), Dec = F("dec") });
                Cmp.Tokens(exp[i]!["vendor"], Tool2Tokens.Vendors(d), sc + " vendor " + cases[i]!["name"]);
                Cmp.Tokens(exp[i]!["divmc"], Tool2Tokens.DivMc(d), sc + " divmc " + cases[i]!["name"]);
            }
        }

        [Theory, MemberData(nameof(Scenarios))]
        public void Csv_matches(string sc)
        {
            var ws = Tool2Fixture.Workspace(sc);
            var exp = Tool2Fixture.Result(sc)["csv"]!.AsObject();
            var csv = ws.Csv();
            var el = exp["text"]!.GetValue<string>().Split('\n'); var al = csv.Split('\n');
            Assert.Equal(el.Length, al.Length);
            for (var i = 0; i < el.Length; i++) Assert.Equal(el[i], al[i]);
            Assert.Equal(exp["file"]!.GetValue<string>(), "VMM_RaisedPO_Assessment_" + Tool2Workspace.FileDate(Tool2Fixture.Now(sc)) + ".csv");
        }

        [Theory, MemberData(nameof(Scenarios))]
        public void Excel_workbooks_match(string sc)
        {
            var ws = Tool2Fixture.Workspace(sc);
            foreach (var x in Tool2Fixture.Result(sc)["xlsx"]!.AsArray())
            {
                var sel = Cmp.Strings(x!["selection"]);
                var what = sc + " [" + string.Join(",", sel) + "]";
                if (x["sheets"] == null)
                {
                    var ex = Assert.Throws<Domain.Exceptions.AppValidationException>(() => ws.BuildWorkbook(sel, Tool2Fixture.Now(sc)));
                    Assert.Equal(x["toast"]!.GetValue<string>(), ex.Message);
                    continue;
                }
                var wb = ws.BuildWorkbook(sel, Tool2Fixture.Now(sc));
                Assert.Equal(x["file"]!.GetValue<string>(), wb.FileName);
                Assert.Equal(x["toast"]!.GetValue<string>(), wb.Message);
                var es = x["sheets"]!.AsArray();
                Assert.Equal(es.Select(s => s!["name"]!.GetValue<string>()).ToList(), wb.Sheets.Select(s => s.Name).ToList());
                for (var i = 0; i < es.Count; i++) CompareSheet(es[i]!.AsObject(), wb.Sheets[i], what + " " + wb.Sheets[i].Name);
            }
        }

        private static void CompareSheet(JsonObject e, Tool2SheetDto a, string what)
        {
            // rows: NaN cells (parseFloat('—')) are null in the JS JSON
            var rows = new JsonArray(a.Rows.Select(r => (JsonNode)new JsonArray(r.Select(v => v switch
            {
                null => null, double d when !double.IsFinite(d) => null, double d => JsonValue.Create(d), string s => (JsonNode?)JsonValue.Create(s), _ => JsonValue.Create(v.ToString())
            }).ToArray())).ToArray());
            Cmp.Check(e["rows"], rows, what + ".rows");
            if (e["cols"] is JsonArray cols)
                Cmp.Check(cols, new JsonArray(a.Cols!.Select(c =>
                {
                    var o = new JsonObject { ["wch"] = c.Width };
                    if (c.Hidden != null) o["hidden"] = c.Hidden;
                    return (JsonNode)o;
                }).ToArray()), what + ".cols");
            else Assert.Null(a.Cols);
            var merges = e["merges"] is JsonArray m
                ? m.Select(x => Tool2Workspace.Addr(x!["s"]!["r"]!.GetValue<int>(), x["s"]!["c"]!.GetValue<int>()) + ":" + Tool2Workspace.Addr(x["e"]!["r"]!.GetValue<int>(), x["e"]!["c"]!.GetValue<int>())).ToList()
                : null;
            Assert.Equal(merges, a.Merges);
            Assert.Equal(e["autofilter"]?["ref"]?.GetValue<string>(), a.AutoFilter);
            var styled = new HashSet<string>();
            foreach (var (addr, cellNode) in e["cells"]!.AsObject())
            {
                var c = cellNode!.AsObject();
                if (c["s"] == null && c["z"] == null && c["l"] == null) continue;
                styled.Add(addr);
                Assert.True(a.Styles.TryGetValue(addr, out var st), $"{what} {addr}: no style");
                var s = c["s"]?.AsObject();
                var exp = new JsonObject
                {
                    ["fontColor"] = s?["font"]?["color"]?["rgb"]?.DeepClone(), ["bold"] = s?["font"]?["bold"]?.DeepClone(), ["italic"] = s?["font"]?["italic"]?.DeepClone(),
                    ["underline"] = s?["font"]?["underline"]?.DeepClone(), ["size"] = s?["font"]?["sz"]?.DeepClone(), ["fill"] = s?["fill"]?["fgColor"]?["rgb"]?.DeepClone(),
                    ["hAlign"] = s?["alignment"]?["horizontal"]?.DeepClone(), ["vAlign"] = s?["alignment"]?["vertical"]?.DeepClone(), ["wrap"] = s?["alignment"]?["wrapText"]?.DeepClone(),
                    ["indent"] = s?["alignment"]?["indent"]?.DeepClone(), ["border"] = s?["border"]?["top"]?["color"]?["rgb"]?.DeepClone(),
                    ["numFmt"] = c["z"]?.DeepClone(), ["link"] = c["l"]?["Target"]?.DeepClone(), ["linkTooltip"] = c["l"]?["Tooltip"]?.DeepClone()
                };
                var act = Cmp.Node(st)!.AsObject();
                foreach (var kv in exp) Assert.True(JsonNode.DeepEquals(kv.Value, act[kv.Key]), $"{what} {addr}.{kv.Key}: expected {kv.Value?.ToJsonString()} got {act[kv.Key]?.ToJsonString()}");
            }
            foreach (var addr in a.Styles.Keys) Assert.True(styled.Contains(addr), $"{what} {addr}: styled in C# but not in JS");
        }
    }
}
