using Admin.Components.Shared.DataSetup;
using Application.Services.V1.Tool1;
using DecisionEngine.Common;
using DecisionEngine.Explain;
using DecisionEngine.Model;
using DecisionEngine.Recommendation;
using DecisionEngine.Results.Tool1;
using DecisionEngine.Tool1;
using Domain.Constants;
using Domain.Exceptions;
using Domain.Records;

namespace POGovernance.Tests.Tool1
{
    /// <summary>
    /// v1.1.6 rule changes (legacy/v1.1.6/RELEASE-NOTES-v1.1.6.txt) as small unit tests next to the parity fixtures:
    /// D1 no plan window, D2 days left count today, late PO note, Article Master stop, "Why introduce this colour?",
    /// the cut-off day check and the explain workbook references.
    /// </summary>
    public class V116RuleTests
    {
        private static readonly AppUserContext Buyer = new("b@vmm.test", "Buyer One", new[] { Roles.Buyer }, false);

        [Fact]
        public void D2_days_left_in_the_month_count_today()
        {
            // release notes item 3: on 4 Nov (30-day month) 27 days are counted instead of 26
            Assert.Equal(27, MonthMath.DaysLeft(30, 4));
            Assert.Equal(1, MonthMath.DaysLeft(31, 31));
            Assert.Equal(0, MonthMath.DaysLeft(30, 31));
        }

        [Fact]
        public void D1_requirement_window_and_reason()
        {
            // D days from the 1st of the month after delivery: Nov 30 + Dec 31 = 61 < 75 → Jan too
            Assert.Equal(new[] { "11.2026", "12.2026", "01.2027" }, NoPlan.WindowMonths("10.2026", 75));
            Assert.Equal(new[] { "01.2027" }, NoPlan.WindowMonths("12.2026", 31));
            Assert.Empty(NoPlan.WindowMonths("13.2026", 75));
            Assert.Empty(NoPlan.WindowMonths("2026-11", 75));
            var plan = new Dictionary<string, double> { ["11.2026"] = 0, ["02.2027"] = 500 };
            double F(string k) => plan.TryGetValue(k, out var v) ? v : 0;
            var ks = NoPlan.Check(F, "10.2026", true, "2026-11-04", 75);
            Assert.Equal(new[] { "11.2026", "12.2026", "01.2027" }, ks);
            Assert.Null(NoPlan.Check(F, "11.2026", true, "2026-11-04", 75));     // Dec – Feb has a plan
            // PO Assessment: a PO two or more months late is checked from last month (Oct → Nov – Jan, no plan)
            Assert.Equal(new[] { "11.2026", "12.2026", "01.2027" }, NoPlan.Check(F, "07.2026", false, "2026-11-04", 75));
            // … the New PO request does not shift (Aug – Oct: no plan either, but other months)
            Assert.Equal(new[] { "08.2026", "09.2026", "10.2026" }, NoPlan.Check(F, "07.2026", true, "2026-11-04", 75));
            Assert.Equal("No Plan Available: there is no F4 plan for NON-PL · MC 1101 · ₹399 · NON-BOGO in the requirement window (Nov 2026 – Jan 2027). " +
                         "Nothing else was checked. Ask the planning team to add the plan.", NoPlan.Reason("NON-PL", "1101", "399", "NON-BOGO", ks!));
        }

        [Fact]
        public void Late_PO_note_names_the_window_start()
        {
            var note = PrevMonthCarry.LateNote(new CarryOver(8400, 5, "10.2026", "11.2026"));
            Assert.Equal(" [Includes 8,400 units of previous month's (10.2026) pending PO, counted in the stock at the window start (11.2026) — Day-of-month cut-off = 5.]", note);
            Assert.Equal("", PrevMonthCarry.LateNote(new CarryOver(0, 5, "10.2026", "11.2026")));
        }

        [Theory]
        [InlineData("5", true)]
        [InlineData("1", true)]
        [InlineData("28", true)]
        [InlineData("0", false)]
        [InlineData("29", false)]
        [InlineData("5.5", false)]
        [InlineData("5abc", false)]
        [InlineData("-3", false)]
        public void Cut_off_day_is_a_whole_day_from_1_to_28(string raw, bool ok) => Assert.Equal(ok, ParametersPanel.ValidCutoffDay(raw));

        private static Tool1Service Service(EngineData data, FakeRequestLedger ledger) =>
            new(new FakeDatasetCache { Data = data }, ledger, new FakeAppClock { TodayValue = "2026-10-03" }, new Tool1DecisionEngine(new RecommendationEngine()));

        [Fact]
        public async Task Article_not_in_the_Article_Master_stops_the_request()
        {
            var data = Tool1Fixture.Data("S1-early-month");
            var ledger = new FakeRequestLedger { Today = "2026-10-03" };
            var e = await Assert.ThrowsAsync<AppValidationException>(() => Service(data, ledger).ValidateAsync(new() { Gm = "ZZ0000001", Colour = "BLK", Month = "11.2026", Qty = 10 }, Buyer));
            Assert.Equal("Article ZZ0000001 is not in the Article Master. Ask the planning team to add it; this request cannot proceed. Nothing is saved.", e.Message);
            Assert.True(Tool1Flow.IsArticleMasterMessage(e.Message));
            Assert.Empty(ledger.Posts);
            // multi concept too, and a GM written with ".0" is found
            Assert.True(Tool1Flow.InArticleMaster(data, "2203000201.0"));
            Assert.False(Tool1Flow.IsArticleMasterMessage("Please fill all 4 fields."));
        }

        [Fact]
        public async Task Watch_List_needs_the_buyers_reason_before_it_is_saved()
        {
            var data = Tool1Fixture.Data("S1-early-month");
            var ledger = new FakeRequestLedger { Today = "2026-10-03" };
            var svc = Service(data, ledger);
            var input = new Tool1SubmitInputDto { Gm = "1101000101", Colour = "GRY", Month = "12.2026", Qty = 100 };
            var v = await svc.ValidateAsync(input, Buyer);
            Assert.Equal(Decisions.WatchList, v.Decision);
            Assert.True(v.NeedsColourReason);
            Assert.Equal("Not sent: nothing was saved for 1101000101 GRY.", v.NotSentMessage);

            input.SeenSeq = v.SeenSeq;
            input.ColourReason = "   ";
            var none = await svc.SubmitAsync(input, Buyer);
            Assert.Equal("needsColourReason", none.Status);
            Assert.Empty(ledger.Posts);

            input.ColourReason = "  Festive launch  " + new string('x', 1200);
            var s = await svc.SubmitAsync(input, Buyer);
            Assert.Equal("saved", s.Status);
            var why = ledger.Reqs.Last()["colourReason"]!.GetValue<string>();
            Assert.StartsWith("Festive launch", why);
            Assert.Equal(1000, why.Length);
            Assert.Equal(why, s.Card!.ColourReason);
            Assert.False(s.NeedsColourReason);
        }

        [Fact]
        public async Task Multi_concept_Watch_List_is_sent_without_a_reason()
        {
            var data = Tool1Fixture.Data("S1-early-month");
            var ledger = new FakeRequestLedger { Today = "2026-10-03" };
            var svc = Service(data, ledger);
            var v = await svc.ValidateAsync(new() { Gm = "2203000202", Colour = "BLK", Month = "11.2026", Qty = 100 }, Buyer);
            Assert.Equal(Decisions.WatchList, v.Decision);
            Assert.Equal("A", v.Engine);
            Assert.False(v.NeedsColourReason);
        }

        [Fact]
        public async Task Explain_panel_and_workbook_of_a_request()
        {
            var data = Tool1Fixture.Data("S1-early-month");
            var ledger = new FakeRequestLedger { Today = "2026-10-03" };
            var v = await Service(data, ledger).ValidateAsync(new() { Gm = "8800000003", Colour = "BLU", Month = "11.2026", Qty = 100 }, Buyer);
            var x = v.Card!.Explain!;
            Assert.StartsWith("BLU · delivery Nov 2026 · Max Permissible ", x.Title);
            Assert.Equal(v.MaxPermissibleQty, double.Parse(x.Title.Split(' ').Last(), System.Globalization.NumberStyles.AllowThousands, System.Globalization.CultureInfo.InvariantCulture));
            Assert.Null(x.Warn);   // the steps give the engine's own Max Permissible
            Assert.Equal(new[] { "Steps", "Inputs", "Month by month" }, x.Book!.Sheets.Select(s => s.Name));
            var steps = x.Book.Sheets[0];
            Assert.Equal("MAX(0,ROUNDDOWN(D10-D6,0))", steps.Rows.Last().Cells[3]!.Formula);
            Assert.Equal("'Inputs'!B5", steps.Rows[0].Cells[3]!.Formula);
            Assert.StartsWith("SUM('Month by month'!K", steps.Rows[2].Cells[3]!.Formula);
            // the Watch List of a no-sale MC (D3) has no quantity steps
            var w = await Service(data, ledger).ValidateAsync(new() { Gm = "8800000002", Colour = "WHT", Month = "11.2026", Qty = 100 }, Buyer);
            Assert.Empty(w.Card!.Explain!.Steps);
            Assert.StartsWith("No sale in F3", w.Card.Explain.WarnFirst);
            Assert.Null(w.Card.Explain.Book);
        }

        [Fact]
        public void Explain_numbers_follow_the_chain()
        {
            var c = new SingleExplainBuilder.Ctx
            {
                Colour = "BLU", Month = "11.2026", Stock = 2000, Mix = 0.6, T1 = 75, Buf = 5, D = 75, Today = "2026-11-04",
                Chain = new()
                {
                    new(2026, 11, "11.2026", 2000, 0, 1620, 380, 3000, 27, 30),
                    new(2026, 12, "12.2026", 380, 0, 1800, 0, 3000, 31, 31),
                    new(2027, 1, "01.2027", 0, 0, 1800, 0, 3000, 31, 31),
                    new(2027, 2, "02.2027", 0, 0, 1800, 0, 3000, 28, 28),
                }
            };
            var n = SingleExplainBuilder.SingleNumbers(c);
            Assert.Equal(380, n.WStart);
            Assert.Equal(1800 + 1800 + 1800 * 13 / 28.0, n.WDem, 9);
            Assert.Equal(Math.Floor(75 * 1.05 * n.WDem / 75 - 380), n.Can);
        }

        /// <summary>The workbooks' formulas, recalculated by ClosedXML, give the tool's own numbers (as explain.js' tests did).</summary>
        [Theory]
        [InlineData("8800000003", "BLU", "11.2026")]
        [InlineData("8800000003", "BLU", "01.2027")]
        [InlineData("1101000101", "WHT", "12.2026")]
        public async Task Explain_workbook_formulas_recalculate_to_the_tools_numbers(string gm, string colour, string month)
        {
            var data = Tool1Fixture.Data("S1-early-month");
            var v = await Service(data, new FakeRequestLedger { Today = "2026-10-03" }).ValidateAsync(new() { Gm = gm, Colour = colour, Month = month, Qty = 100 }, Buyer);
            AssertRecalculates(v.Card!.Explain!.Book!);

            // the single concept Recommendation: every colour × month and "all lines"
            var rec = new RecommendationEngine();
            var ctx = new RecommendationContext(data, Array.Empty<System.Text.Json.Nodes.JsonObject>(), "2026-10-03");
            var b = rec.RunB(ctx, "1101", "399", "non-pl", "non-bogo", new[] { "10.2026", "11.2026", "12.2026" });
            foreach (var r in b.Rows)
                foreach (var md in r.MData)
                {
                    var x = SingleExplainBuilder.ForRecommendation(r, md, data.Params, "NON-PL · M-TSHIRT RN · ₹399 · NON-BOGO", "2026-10-03");
                    if (md.Action is "Raise" or "Hold")
                    {
                        Assert.Null(x.Warn);   // the steps give the table's own Max Permissible
                        AssertRecalculates(x.Book!);
                    }
                }
            AssertRecalculates(SingleExplainBuilder.RecommendationAll(b, data.Params, "NON-PL", "1101", "2026-10-03"));
        }

        private static void AssertRecalculates(DecisionEngine.Results.Explain.ExplainBookDto book)
        {
            var bytes = Infrastructure.Services.V1.Excel.ExplainExcelWriter.Write(book, "test");
            using var wb = new ClosedXML.Excel.XLWorkbook(new MemoryStream(bytes));
            var n = 0;
            foreach (var sh in book.Sheets)
            {
                var ws = wb.Worksheet(sh.Name);
                for (var ri = 0; ri < sh.Rows.Count; ri++)
                    for (var ci = 0; ci < sh.Cols.Count; ci++)
                    {
                        var c = sh.Rows[ri].Cells[ci];
                        if (c?.Formula == null) continue;
                        var cell = ws.Cell(5 + ri, ci + 1);
                        Assert.Equal(c.Formula, cell.FormulaA1);
                        if (c.Value is double d) { Assert.Equal(d, cell.Value.GetNumber(), 6); n++; }
                        else if (c.Value is string s) Assert.Equal(s, cell.Value.GetText());
                    }
            }
            Assert.True(n > 0);
        }
    }
}
