using System.Text.Json.Nodes;
using Application.DTOs.Recommendation;
using Application.Services.V1.Engines.Common;
using Application.Services.V1.Recommendation;
using Domain.Exceptions;

namespace POGovernance.Tests.Recommendation
{
    /// <summary>
    /// Runs the C# recommendation engine on the parity fixture and compares every result with the
    /// original v1.1.4 JavaScript output (Fixtures/expected.json, produced by
    /// tests/parity/recommendation/run-parity.js from legacy/python-app/app/index.html).
    /// </summary>
    public class RecommendationParityTests
    {
        private static readonly RecommendationEngine Engine = new();

        public static IEnumerable<object[]> Cases() => RecommendationFixture.Cases();

        [Theory]
        [MemberData(nameof(Cases))]
        public void Matches_original_js(string scenario, string caseName)
        {
            var ctx = RecommendationFixture.Context(scenario);
            var c = RecommendationFixture.Case(scenario, caseName);
            var a = c["args"]!.AsObject();
            var expected = c["result"];
            string S(string k) => a[k]?.GetValue<string>() ?? "";
            List<string> Months() => a["months"]!.AsArray().Select(x => x!.GetValue<string>()).ToList();
            double Dbl(string k) => a[k]!.GetValue<double>();

            Assert.False(expected is JsonObject eo && eo.ContainsKey("__error"), "JS threw: " + expected?.ToJsonString());

            switch (c["kind"]!.GetValue<string>())
            {
                case "buildMonths":
                    JsonParity.Check(expected, Engine.BuildMonths(ctx.Data, S("mc"), S("mrp"), S("brand"), S("flag")), caseName);
                    break;
                case "runB":
                    JsonParity.Check(expected, Engine.RunB(ctx, S("mc"), S("mrp"), S("brand"), S("flag"), Months()), caseName);
                    break;
                case "runA":
                    JsonParity.Check(expected, Engine.RunA(ctx, S("mc"), S("mrp"), S("brand"), S("flag"), Months(), a["dim"]?.GetValue<string>()), caseName);
                    break;
                case "runBCached":
                {
                    var cache = new RecommendationRunCache();
                    var r1 = Engine.RunBCached(ctx, S("mc"), S("mrp"), S("brand"), S("flag"), Months(), cache);
                    JsonParity.Check(expected![0], r1, caseName);
                    Assert.Same(r1, Engine.RunBCached(ctx, S("mc"), S("mrp"), S("brand"), S("flag"), Months(), cache));
                    break;
                }
                case "runACached":
                {
                    var cache = new RecommendationRunCache();
                    var r1 = Engine.RunACached(ctx, S("mc"), S("mrp"), S("brand"), S("flag"), Months(), cache);
                    JsonParity.Check(expected![0], r1, caseName);
                    Assert.Same(r1, Engine.RunACached(ctx, S("mc"), S("mrp"), S("brand"), S("flag"), Months(), cache));
                    break;
                }
                case "strCaution":
                {
                    var r = Engine.StrCaution(ctx, S("mc"), S("mrp"), S("brand"), S("flag"), S("colour"), S("gm"), S("month"));
                    JsonParity.Check(expected!["res"], r, caseName);
                    if (r != null)
                    {
                        JsonParity.Check(expected["box"]!["texts"], r.Items.Select(i => i.Text).ToList(), caseName + ".texts");
                        JsonParity.Check(expected["box"]!["footers"], r.Items.Select(i => i.Footer).ToList(), caseName + ".footers");
                    }
                    break;
                }
                case "bsrQ":
                    JsonParity.Check(expected, Engine.BsrQ(ctx, S("gm"), S("colour"), S("mc"), S("mrp"), S("flag")), caseName);
                    break;
                case "gmsForColour":
                    JsonParity.Check(expected, Engine.GetGMsForColour(ctx, S("mc"), S("mrp"), S("brand"), S("flag"), S("colour")), caseName);
                    break;
                case "recQty":
                    JsonParity.Check(expected, Engine.RecQty(ctx.P, Dbl("stock"), Dbl("pending"), Dbl("f4Total"), S("month"), Dbl("sales45"), Dbl("totalSales45")), caseName);
                    break;
                case "f2ByCol":
                    JsonParity.Check(expected, RecommendationData.F2ByCol(ctx.Data, S("mc"), S("mrp"), S("brand"), S("flag"), S("month")).ToDictionary(), caseName);
                    break;
                case "f4":
                    JsonParity.Check(expected, RecommendationData.F4(ctx.Data, S("mc"), S("mrp"), S("brand"), S("flag"), S("month")), caseName);
                    break;
                case "cumPO":
                    JsonParity.Check(expected, RecommendationData.CumPO(Map(a["map"]), S("month")), caseName);
                    break;
                case "carry":
                {
                    var map = Map(a["map"]);
                    var k = PrevMonthCarry.Compute(map, (int)Dbl("y"), (int)Dbl("m"), (int)Dbl("d"), ctx.P);
                    var actual = new JsonObject
                    {
                        ["carry"] = new JsonObject { ["qty"] = k.Qty, ["cutoff"] = k.Cutoff, ["prevKey"] = k.PrevKey, ["currKey"] = k.CurrKey },
                        ["note"] = PrevMonthCarry.Note(k),
                        ["ePO"] = new JsonArray(Months().Select(m => (JsonNode)PrevMonthCarry.EPOWithCarry(map, m, k)).ToArray())
                    };
                    JsonParity.Check(expected, actual, caseName);
                    break;
                }
                case "openRec":
                    CheckOpenRec(ctx, a, expected!.AsObject(), caseName);
                    break;
                default:
                    throw new InvalidOperationException("unknown case kind");
            }
        }

        private static JsObjectMap<double>? Map(JsonNode? n)
        {
            if (n is not JsonObject o) return null;
            var m = new JsObjectMap<double>();
            foreach (var kv in o) m[kv.Key] = kv.Value!.GetValue<double>();
            return m;
        }

        private static void CheckOpenRec(RecommendationContext ctx, JsonObject a, JsonObject exp, string name)
        {
            var req = new RecommendationRequestDto
            {
                Gm = a["gm"]!.GetValue<string>(),
                Months = a["months"]?.AsArray().Select(x => x!.GetValue<string>()).ToList(),
                DimOverride = a["dim"]?.GetValue<string>()
            };
            var view = RecommendationService.Build(Engine, ctx, req);
            Assert.Equal(exp["engine"]!.GetValue<string>(), view.Engine);
            var alert = exp["alert"]?.GetValue<string>();
            Assert.Equal(alert, view.Error);
            if (alert != null) return;

            JsonParity.Check(exp["allMonths"], view.AllMonths, name + ".allMonths");
            JsonParity.Check(exp["months"], view.Months, name + ".months");
            Assert.Equal(exp["title"]!.GetValue<string>(), view.Title);
            Assert.Equal(exp["sub"]!.GetValue<string>(), view.Subtitle);
            object? res = view.RenderEngine == "B" ? view.B : view.A;
            JsonParity.Check(exp["res"], res, name + ".res");

            var html = exp["html"]!.AsObject();
            var p = view.Paint;
            string? Str(string k) => html[k]?.GetValue<string>();
            Assert.Equal(Str("summary"), p.Summary);
            Assert.Equal(Str("heading"), p.Heading);
            Assert.Equal(Str("step2Heading"), p.Step2Heading);
            var empty = p.EmptyMessage != null ? (p.EmptyIsError ? "⚠ " : "") + p.EmptyMessage : p.NoRecsMessage;
            Assert.Equal(Str("empty"), empty);
            if (view.RenderEngine == "B" && view.B != null)
            {
                JsonParity.Check(html["tips"], view.B.Rows.SelectMany(r => r.MData).Where(m => m.Tip != null).Select(m => m.Tip).ToList(), name + ".tips");
                JsonParity.Check(html["watchTips"], view.B.Rows.SelectMany(r => r.MData.Where(m => m.Action == "Watch List").Select(_ => r.WatchReason)).ToList(), name + ".watchTips");
            }
            if (view.RenderEngine == "A" && view.A?.Recs != null && view.A.Recs.Count > 0)
            {
                JsonParity.Check(html["formulas"], view.A.Recs.SelectMany(r => r.MData).Select(m => m.Formula).ToList(), name + ".formulas");
                JsonParity.Check(html["traceHeaders"], p.RecLayout.Select(l => l.TraceHeader).ToList(), name + ".traceHeaders");
                JsonParity.Check(html["qtyHeaders"], p.RecLayout.Select(l => l.QtyHeader).ToList(), name + ".qtyHeaders");
                JsonParity.Check(html["sections"], p.RecLayout.Where(l => l.SectionHeader != null).Select(l => l.SectionHeader).ToList(), name + ".sections");
            }
            if (view.RenderEngine == "A" && view.A?.Err == null)
                JsonParity.Check(html["options"], view.DimOptions, name + ".options");

            if (exp["export"] is JsonObject ex)
            {
                var toast = ex["toast"]?.GetValue<string>();
                if (view.RenderEngine != "A" || view.A?.Recs == null || view.A.Recs.Count == 0)
                {
                    Assert.Equal("No recommendation rows to export", toast);
                    if (view.A != null && view.A.Err == null && view.A.DimMode == null)
                        Assert.Throws<AppValidationException>(() => Engine.BuildExport(ctx, view.A, ctx.Today));
                    return;
                }
                var file = ex["file"]!.GetValue<string>();
                var export = Engine.BuildExport(ctx, view.A, ctx.Today);
                Assert.Equal(file, export.FileName);
                JsonParity.Check(ex["sheets"], export.Sheets, name + ".export");
            }
        }
    }
}
