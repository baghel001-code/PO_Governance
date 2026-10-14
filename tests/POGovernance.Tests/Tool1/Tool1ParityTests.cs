using System.Text.Json.Nodes;
using Application.DTOs.Tool1;
using Application.Services.V1.Recommendation;
using Application.Services.V1.Tool1;
using Domain.Exceptions;
using Domain.Records;
using POGovernance.Tests.Recommendation;

namespace POGovernance.Tests.Tool1
{
    /// <summary>
    /// Runs the C# Tool 1 service on the parity fixture and compares it with the original v1.1.4 code
    /// (index.html formSubmit / runEngineB / runEngineA / renderResult / renderCatHistory + shared.js
    /// saveRequest, run by tests/parity/tool1/run-parity.js): the stored request field by field, the
    /// confirm pop-up, toasts, the 409 re-check, the result card texts and the Today's Requests rows.
    /// The README relabel (Tool1Settings.ReducedLabel) is off here — the shipped JS does not apply it.
    /// </summary>
    public class Tool1ParityTests
    {
        public static IEnumerable<object[]> SubmitCases() => Tool1Fixture.Cases("submit");
        public static IEnumerable<object[]> OptionCases() => Tool1Fixture.Cases("options");

        private static readonly JsonObject Me = Tool1Fixture.Expected["me"]!.AsObject();
        private static AppUserContext User => new(Me["email"]!.GetValue<string>(), Me["name"]!.GetValue<string>(), new[] { "planner" }, false);

        private static (Tool1Service Svc, FakeRequestLedger Ledger) Service(string scenario, bool reducedLabel = false)
        {
            var sc = Tool1Fixture.Scenario(scenario);
            var today = sc["today"]!.GetValue<string>();
            var ledger = new FakeRequestLedger { Today = today, SavedAt = Tool1Fixture.Expected["savedAt"]!.GetValue<string>() };
            var engB = new Tool1EngineB();
            var svc = new Tool1Service(new FakeDatasetCache { Data = Tool1Fixture.Data(scenario) }, ledger, new FakeAppClock { TodayValue = today },
                new Tool1Flow(engB, new Tool1EngineA(), new RecommendationEngine()), engB, new Tool1FormOptions(), new Tool1Settings { ReducedLabel = reducedLabel });
            return (svc, ledger);
        }

        [Fact]
        public void Months_match()
        {
            foreach (var sc in Tool1Fixture.Expected["scenarios"]!.AsArray())
            {
                var exp = sc!["results"]!.AsArray().First(r => r!["kind"]!.GetValue<string>() == "months")!["result"]!.AsArray();
                var act = Tool1FormOptions.Months(sc["today"]!.GetValue<string>());
                Assert.Equal(exp.Count - 1, act.Count);
                for (var i = 0; i < act.Count; i++)
                {
                    Assert.Equal(exp[i + 1]![0]!.GetValue<string>(), act[i].Value);
                    Assert.Equal(exp[i + 1]![1]!.GetValue<string>(), act[i].Label);
                }
            }
        }

        [Theory]
        [MemberData(nameof(OptionCases))]
        public void Form_options_match(string scenario, string caseName)
        {
            var c = Tool1Fixture.Case(scenario, caseName);
            var exp = c["result"]!.AsObject();
            var o = new Tool1FormOptions().Build(Tool1Fixture.Data(scenario), c["args"]!["gm"]!.GetValue<string>(), Tool1Fixture.Scenario(scenario)["today"]!.GetValue<string>());
            Assert.Equal(exp["show"]!.GetValue<bool>(), o.ShowStrip);
            if (o.ShowStrip)
                Assert.Equal(exp["strip"]!.AsArray().Select(x => x!.GetValue<string>()), new[] { o.Strip!.Brand, o.Strip.Div, o.Strip.McCd, o.Strip.McDesc, o.Strip.Mrp, o.Strip.Flag });
            var opts = new List<string[]> { new[] { "", o.ColourPlaceholder } };
            opts.AddRange(o.Colours.Select(x => new[] { x.Value, x.Label }));
            if (o.NewColourLabel != null) opts.Add(new[] { "__new__", o.NewColourLabel });
            var expOpts = exp["options"]!.AsArray().Select(x => new[] { x![0]!.GetValue<string>(), x[1]!.GetValue<string>() }).ToList();
            Assert.Equal(expOpts.Count, opts.Count);
            for (var i = 0; i < opts.Count; i++) Assert.Equal(expOpts[i], opts[i]);
            Assert.Equal(exp["manual"]!.GetValue<string>() == "block", o.ShowManualColour);
        }

        [Theory]
        [MemberData(nameof(SubmitCases))]
        public async Task Submit_matches_original(string scenario, string caseName)
        {
            var sc = Tool1Fixture.Scenario(scenario);
            var c = Tool1Fixture.Case(scenario, caseName);
            var a = c["args"]!.AsObject();
            var exp = c["result"]!.AsObject();
            Assert.False(exp.ContainsKey("__error"), "JS threw: " + exp["__error"]);
            var seqBefore = exp["seqBefore"]!.GetValue<int>();
            var (svc, ledger) = Service(scenario);
            ledger.Reqs = sc["ledger"]!.AsArray().Select(x => x!.AsObject()).Where(x => x["seq"]!.GetValue<int>() <= seqBefore).Select(x => x.DeepClone().AsObject()).ToList();
            ledger.Seq = seqBefore;

            var qtyTxt = a["qty"]!.GetValue<string>();
            var input = new Tool1RequestInputDto
            {
                Gm = a["gm"]!.GetValue<string>(), Colour = a["colour"]?.GetValue<string>(), ColourText = a["colourText"]?.GetValue<string>(),
                Month = a["month"]!.GetValue<string>(), Qty = qtyTxt.Length == 0 ? null : double.Parse(qtyTxt, System.Globalization.CultureInfo.InvariantCulture)
            };
            var toasts = exp["toasts"]!.AsArray().Select(x => x!.GetValue<string>()).ToList();
            if (exp["saved"] == null && exp["card"] == null)
            {
                var ex = await Assert.ThrowsAsync<AppValidationException>(() => svc.ValidateAsync(input, User));
                Assert.Equal(toasts.Single(), ex.Message);
                return;
            }

            var v = await svc.ValidateAsync(input, User);
            var asked = exp["asked"] as JsonObject;
            Assert.Equal(asked != null, v.NeedsConfirmation);
            var submit = new Tool1SubmitInputDto { Gm = input.Gm, Colour = input.Colour, ColourText = input.ColourText, Month = input.Month, Qty = input.Qty, SeenSeq = v.SeenSeq };
            if (asked != null)
            {
                Assert.Equal(asked["proposed"]!.GetValue<double>(), v.ProposedQty);
                Assert.Equal(asked["max"]!.GetValue<double>(), v.MaxPermissibleQty);
                if (exp["parallel"] is JsonObject par) { ledger.Reqs.Add(par.DeepClone().AsObject()); ledger.Seq = par["seq"]!.GetValue<int>(); }
                var conf = a["confirm"]!.GetValue<string>();
                if (conf == "decline") submit.Declined = true;
                else submit.ConfirmedQty = conf == "max" ? v.MaxPermissibleQty
                    : double.TryParse(conf, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var d) ? d : double.NaN;
            }
            var res = await svc.SubmitAsync(submit, User);

            Assert.Equal(toasts, res.Toasts);
            Assert.Equal(exp["posts"]!.AsArray().Select(p => (p!["seenSeq"]!.GetValue<int>(), p["force"]!.GetValue<bool>())), ledger.Posts);
            var saved = exp["saved"]!.DeepClone().AsObject();
            saved.Remove("createdAt");
            var mine = res.Request!.DeepClone().AsObject();
            mine.Remove("createdAt");
            JsonParity.Check(saved, mine, caseName + ".saved");
            Assert.Equal("saved", res.Status);

            CompareCard(exp["card"]!.AsObject(), res.Card!, saved, caseName);
            if (exp["pools"] is JsonObject pools) ComparePools(pools, scenario, saved);
            if (exp["history"] is JsonArray hist && exp["parallel"] == null)
            {
                var today = await svc.GetTodayAsync(User, false);
                var rows = today.Rows.Select(r => new[] { r.RequestId, r.Gm, r.Colour, r.McDesc, r.Mrp, r.Month, r.Qty, r.EngineTag, r.OptionType,
                    r.Decision + (r.ApprovalBadge?.Text ?? ""), r.ApprovedQty, r.By }).ToList();
                Assert.Equal(hist.Count, rows.Count);
                for (var i = 0; i < rows.Count; i++)
                    Assert.Equal(hist[i]!.AsArray().Select(x => x!.GetValue<string>()), rows[i]);
            }
        }

        private static List<string> Strs(JsonNode? n) => n == null ? new() : n.AsArray().Select(x => x!.GetValue<string>()).ToList();
        private static List<List<string>> Rows(JsonNode? n) => n == null ? new() : n.AsArray().Select(r => Strs(r)).ToList();

        private static void CompareCard(JsonObject e, Tool1CardDto c, JsonObject saved, string name)
        {
            Assert.Equal(e["decision"]!.GetValue<string>(), c.Decision);
            Assert.Equal(e["num"]!.GetValue<string>(), c.Id.ToString());
            var title = new List<string> { c.Gm, c.Colour, c.EngineLabel };
            if (c.OptionTypeChip != null) title.Add(c.OptionTypeChip);
            Assert.Equal(Strs(e["titleSpans"]), title);
            Assert.Equal(e["sub"]!.GetValue<string>(), c.SubLine);
            Assert.Equal(Rows(e["chips"]), c.Chips.Select(x => new List<string> { x.Key, x.Value }).ToList());
            Assert.Equal(e["mixBanner"]?.GetValue<string>(), c.B?.MixBanner);
            Assert.Equal(Rows(e["kpis"]), (c.B?.Kpis ?? new()).Select(x => new List<string> { x.Key, x.Value, x.Colour! }).ToList());
            if (e["step1"] != null)
            {
                var b = c.B!;
                var s1 = new List<string>(); if (b.Step1.OpStock != null) s1.Add(b.Step1.OpStock); s1.Add(b.Step1.Key3);
                Assert.Equal(Strs(e["step1"]), s1);
                Assert.Equal(Strs(e["step2"]), new List<string> { b.Step2.ColAvg, b.Step2.McAvg, b.Step2.MixPct, b.Step2.Threshold });
                Assert.Equal(Strs(e["step2Labels"]), new List<string> { "Colour Avg Sales/" + b.Step2.Per, "MC Total Avg Sales/" + b.Step2.Per });
                Assert.Equal(e["step2Basis"]!.GetValue<string>(), b.Step2.Basis);
                Assert.Equal(e["step3Proposed"]!.GetValue<string>(), b.Step3.ProposedQty);
                Assert.Equal(e["step3Empty"]!.GetValue<bool>(), b.Step3.Rows.Count == 0 && saved["chain"] is JsonArray { Count: 0 });
                var s3 = Rows(e["step3"]).Where(r => r.Count > 1).ToList();
                var m3 = b.Step3.Rows.Select(r => new List<string> { r.Background, r.Tag != null ? r.Month + " " + r.Tag : r.Month, r.OpenStock, r.ExistingPo, r.Proposed,
                    r.DemandNote != null ? r.Demand + " " + r.DemandNote : r.Demand, r.CloseStock, r.Note }).ToList();
                Assert.Equal(s3, m3);
                var s4 = Rows(e["step4"]).Where(r => r.Count > 1 && r[0] != "#7C3AED11").ToList();
                Assert.Equal(s4, b.Step4.Rows.Select(r => new List<string> { r.Background, r.Month, r.Demand, r.DaysInMonth, r.DaysTaken, r.PeriodDemand }).ToList());
                var tot = Rows(e["step4Total"]);
                if (tot.Count > 0) Assert.Equal(tot[0], new List<string> { b.Step4.TotalLabel!, "", b.Step4.D, b.Step4.D, b.Step4.TotalDemand! });
                else Assert.Null(b.Step4.TotalLabel);
                Assert.Equal(Strs(e["step4Formula"]), b.Step4.Formula);
                // Step 5: lines 2–3 follow README item 2 (exact threshold, engine stock before this PO) — compare the rest
                var f5 = Strs(e["step5Formula"]);
                Assert.Equal(f5[0], b.Step5.Formula[0]);
                Assert.Equal(f5[^1], b.Step5.Formula[^1]);
                Assert.Equal(e["step5Verdict"]!.GetValue<string>(), b.Step5.VerdictLead + " " + b.Step5.VerdictText);
                Assert.Equal(e["step5Header"]!.GetValue<string>(), b.Step5.HeaderColour);
            }
            if (e["aStep1"] != null)
                Assert.Equal(Rows(e["aStep1"]), c.A!.Step1.Select(x => new List<string> { x.Key, x.Value, x.Colour! }).ToList());
            Assert.Equal(Rows(e["aStep2"]), (c.A?.Step2 ?? new()).Select(x => new List<string> { x.Key, x.Value, x.Colour! }).ToList());
            if (e["aStep3"] != null)
            {
                var q = c.A!.Step3!;
                Assert.Equal(Strs(e["aStep3"]), new List<string> { q.Heading, q.PoolText, q.RankText, Domain.Entities.Common.Js.NumberToString(q.Position) });
            }
            else Assert.Null(c.A?.Step3);
            if (e["aStep4"] != null) Assert.Equal(Strs(e["aStep4"]), c.A!.Step4!.Lines.Concat(new[] { c.A.Step4.Verdict }).ToList());
            else Assert.Null(c.A?.Step4);
            Assert.Equal(e["absNote"]?.GetValue<string>(), c.A?.AbsoluteBlockNote);
            Assert.Equal(e["reasonTitle"]!.GetValue<string>(), c.ReasonTitle);
            Assert.Equal(saved["decisionReason"]!.GetValue<string>(), c.Reason);
            if (!c.Reason.Contains('<')) Assert.Equal(e["reason"]!.GetValue<string>(), System.Text.RegularExpressions.Regex.Replace(c.Reason, @"\s+", " ").Trim());
            Assert.Equal(e["footer"]!.GetValue<string>(), c.FooterHint);
            Assert.Equal(Strs(e["caution"]), (c.StrCaution?.Items ?? new()).Select(i => i.Text).ToList());
        }

        private static void ComparePools(JsonObject e, string scenario, JsonObject saved)
        {
            var data = Tool1Fixture.Data(scenario);
            var info = saved["info"] is JsonObject i ? System.Text.Json.JsonSerializer.Deserialize<Tool1InfoDto>(i.ToJsonString()) : null;
            var r = new Tool1EngineA().Run(data, saved["gm"]!.GetValue<string>(), saved["colour"]!.GetValue<string>(), saved["optionType"]!.GetValue<string>(), info);
            string Id(Domain.Entities.Common.SheetRow x) => x.Str("Generic Material") + "|" + x.Str("Color");
            Assert.Equal(Strs(e["peerPool"]), r.PeerPool.Select(Id).ToList());
            Assert.Equal(Strs(e["cleanPool"]), r.CleanPool.Select(Id).ToList());
            Assert.Equal(Strs(e["siblings"]), r.Siblings.Select(Id).ToList());
            Assert.Equal(Strs(e["qualSibs"]), r.QualSibs.Select(Id).ToList());
            var sr = e["sibRanked"]!.AsArray();
            Assert.Equal(sr.Count, r.SibRanked.Count);
            for (var k = 0; k < sr.Count; k++)
            {
                Assert.Equal(sr[k]!["id"]!.GetValue<string>(), Id(r.SibRanked[k].Row));
                Assert.Equal(sr[k]!["q"]!.GetValue<string>(), r.SibRanked[k].Q);
                Assert.Equal(sr[k]!["pdstr"]!.GetValue<double>(), r.SibRanked[k].Pdstr, 12);
            }
            Assert.Equal(e["repSibling"]?.GetValue<string>(), r.RepSibling == null ? null : Id(r.RepSibling));
        }
    }
}
