using System.Text.Json.Nodes;
using Application.Services.V1.Tool1;
using DecisionEngine.Model;
using DecisionEngine.Recommendation;
using DecisionEngine.Results.Tool1;
using DecisionEngine.Tool1;
using Domain.Constants;
using Domain.Exceptions;
using Domain.Records;

namespace POGovernance.Tests.Tool1
{
    /// <summary>Tool1Service with a fake IRequestLedger: the server-side flow around the parity-tested engines.</summary>
    public class Tool1ServiceTests
    {
        private const string Scenario = "S1-early-month";
        private static readonly AppUserContext Buyer = new("b@vmm.test", "Buyer One", new[] { Roles.Buyer }, false);
        private static readonly AppUserContext Planner = new("p@vmm.test", "Planner One", new[] { Roles.Planner }, false);

        private static (Tool1Service Svc, FakeRequestLedger Ledger) Make(bool reducedLabel = true)
        {
            var sc = Tool1Fixture.Scenario(Scenario);
            var initial = sc["initialSeq"]!.GetValue<int>();
            var ledger = new FakeRequestLedger
            {
                Today = "2026-10-03",
                Reqs = sc["ledger"]!.AsArray().Select(x => x!.AsObject()).Where(x => x["seq"]!.GetValue<int>() <= initial).Select(x => x.DeepClone().AsObject()).ToList(),
                Seq = initial
            };
            var svc = new Tool1Service(new FakeDatasetCache { Data = Tool1Fixture.Data(Scenario) }, ledger, new FakeAppClock { TodayValue = "2026-10-03" },
                new Tool1DecisionEngine(new RecommendationEngine()), new Tool1Settings { ReducedLabel = reducedLabel });
            return (svc, ledger);
        }

        private static Tool1SubmitInputDto In(string gm, string colour, string month, double qty) =>
            new() { Gm = gm, Colour = colour, Month = month, Qty = qty };

        [Fact]
        public async Task Reduce_is_labelled_and_kept_after_confirmation()
        {
            var (svc, ledger) = Make();
            var input = In("1101000101", "WHT", "11.2026", 500000);
            var v = await svc.ValidateAsync(input, Buyer);
            Assert.True(v.NeedsConfirmation);
            Assert.Equal(Decisions.ReduceRecommended, v.Decision);
            Assert.True(v.MaxPermissibleQty > 0 && v.MaxPermissibleQty < 500000);
            Assert.Equal(500000, v.ProposedQty);
            Assert.Equal("needsConfirmation", v.Status);
            Assert.Empty(ledger.Posts);

            input.SeenSeq = v.SeenSeq;
            input.ConfirmedQty = v.MaxPermissibleQty! - 100;
            var s = await svc.SubmitAsync(input, Buyer);
            Assert.Equal("saved", s.Status);
            Assert.Equal(Decisions.ReduceRecommended, s.Decision);
            Assert.Equal(v.MaxPermissibleQty - 100, s.FinalApprovedQty);
            Assert.Equal(s.FinalApprovedQty, s.ApprovedQty);
            var stored = ledger.Reqs.Last();
            Assert.Equal(Decisions.ReduceRecommended, stored["decision"]!.GetValue<string>());
            Assert.Equal(500000, stored["qty"]!.GetValue<double>());
            Assert.Equal("non-pl|1101|399|non-bogo|wht", stored["colourKey"]!.GetValue<string>());
            Assert.Equal("Buyer One", stored["requestedBy"]!.GetValue<string>());
            Assert.False(stored.ContainsKey("peerPool"));
            Assert.Equal("REQ-20261003-" + ledger.Seq.ToString("0000"), s.ReqId);
            // card: Raised Qty chip, remaining headroom KPI, README Step 5 formula with the exact threshold
            Assert.Contains(s.Card!.Chips, c => c.Key == "Raised Qty");
            Assert.Equal("100", s.Card.B!.Kpis.Last().Value);
            Assert.StartsWith("= MAX(0, 78.75 × ", s.Card.B.Step5.Formula[1]);
        }

        [Fact]
        public async Task Without_relabel_the_v114_labels_are_kept()
        {
            var (svc, _) = Make(reducedLabel: false);
            var v = await svc.ValidateAsync(In("1101000101", "WHT", "11.2026", 500000), Buyer);
            Assert.Equal(Decisions.Hold, v.Decision);
            var s = await svc.SubmitAsync(new Tool1SubmitInputDto { Gm = "1101000101", Colour = "WHT", Month = "11.2026", Qty = 500000, SeenSeq = v.SeenSeq, ConfirmedQty = 10 }, Buyer);
            Assert.Equal(Decisions.AutoAllow, s.Decision);
        }

        [Fact]
        public async Task Declined_counts_zero_and_stays_reduced()
        {
            var (svc, ledger) = Make();
            var v = await svc.ValidateAsync(In("1101000101", "WHT", "11.2026", 500000), Buyer);
            var s = await svc.SubmitAsync(new Tool1SubmitInputDto { Gm = "1101000101", Colour = "WHT", Month = "11.2026", Qty = 500000, SeenSeq = v.SeenSeq, Declined = true }, Buyer);
            Assert.Equal(Decisions.ReduceRecommended, s.Decision);
            Assert.Equal(0, s.FinalApprovedQty);
            Assert.Equal(0, ledger.Reqs.Last()["finalApprovedQty"]!.GetValue<double>());
        }

        [Fact]
        public async Task Submit_without_answer_asks_for_confirmation_and_does_not_save()
        {
            var (svc, ledger) = Make();
            var s = await svc.SubmitAsync(In("1101000101", "WHT", "11.2026", 300), Buyer);
            Assert.Equal("needsConfirmation", s.Status);
            Assert.Equal(Decisions.AutoAllow, s.Decision);
            Assert.Empty(ledger.Posts);
        }

        [Fact]
        public async Task Parallel_approval_at_save_caps_the_quantity()
        {
            var (svc, ledger) = Make();
            var v = await svc.ValidateAsync(In("1101000101", "WHT", "11.2026", 300), Buyer);
            ledger.BeforeSave = l => l.Store(new JsonObject
            {
                ["gm"] = "1101000103", ["colour"] = "WHT", ["month"] = "11.2026", ["qty"] = 999999, ["engine"] = "B", ["decision"] = "Auto Allow",
                ["finalApprovedQty"] = 999999, ["colourKey"] = "non-pl|1101|399|non-bogo|wht",
                ["info"] = new JsonObject { ["brand"] = "NON-PL", ["mcCd"] = "1101", ["mrp"] = "399", ["flag"] = "NON-BOGO" }
            }, "Other Buyer", "o@vmm.test");
            var s = await svc.SubmitAsync(new Tool1SubmitInputDto { Gm = "1101000101", Colour = "WHT", Month = "11.2026", Qty = 300, SeenSeq = v.SeenSeq, ConfirmedQty = 300 }, Buyer);
            Assert.True(s.HeadroomChanged);
            Assert.Equal("Other Buyer", s.ConflictByOthers);
            Assert.Equal(0, s.FinalApprovedQty);
            Assert.Equal(Decisions.Hold, s.Decision);
            Assert.StartsWith("Headroom changed while you were confirming — Other Buyer approved a request for this colour moments earlier. Approved qty capped from 300 to 0 units. ", s.DecisionReason);
            Assert.Equal("⚠ Another request for this colour was approved first — your approved qty was capped to 0", s.Toasts[0]);
            Assert.Equal(2, ledger.Posts.Count);
            Assert.True(ledger.Posts[1].Force);
        }

        private sealed class ForbiddenLedger : Application.Interfaces.V1.IRequestLedger
        {
            public Task<(List<JsonObject> Requests, int Seq)> GetDayAsync(string date, CancellationToken ct = default) => Task.FromResult((new List<JsonObject>(), 0));
            public Task<JsonObject> SaveAsync(JsonObject request, int seenSeq, bool force, AppUserContext user, CancellationToken ct = default) =>
                throw new ForbiddenException("This Brand / MC / MRP is not mapped to you in the SCR products file. Ask the administrator.");
        }

        [Fact]
        public async Task Save_refused_is_reported_like_shared_js()
        {
            var svc = new Tool1Service(new FakeDatasetCache { Data = Tool1Fixture.Data(Scenario) }, new ForbiddenLedger(), new FakeAppClock { TodayValue = "2026-10-03" },
                new Tool1DecisionEngine(new RecommendationEngine()));
            var s = await svc.SubmitAsync(In("ZZ0000001", "BLK", "11.2026", 10), Buyer);
            Assert.Equal("notSaved", s.Status);
            Assert.Equal(new[] { "⚠ This Brand / MC / MRP is not mapped to you in the SCR products file. Ask the administrator.", "⚠ Request decided but NOT saved to server (HTTP 403)" }, s.Toasts);
            Assert.Equal("New Product", s.Card!.EngineLabel);
        }

        [Fact]
        public async Task Input_checks()
        {
            var (svc, _) = Make();
            var e = await Assert.ThrowsAsync<AppValidationException>(() => svc.ValidateAsync(In("1101000101", "", "11.2026", 10), Buyer));
            Assert.Equal("Please fill all 4 fields.", e.Message);
            var manual = await svc.ValidateAsync(new Tool1RequestInputDto { Gm = " 1101000101 ", Colour = "__new__", ColourText = " owh ", Month = "11.2026", Qty = 10.7 }, Buyer);
            Assert.Equal("OWH", manual.Request!["colour"]!.GetValue<string>());
            Assert.Equal(10, manual.Request["qty"]!.GetValue<double>());
        }

        [Fact]
        public async Task Today_list_visibility_and_badges()
        {
            var (svc, ledger) = Make();
            var v = await svc.ValidateAsync(In("2203000205", "NEWC", "11.2026", 400), Buyer);
            Assert.Equal(Decisions.WatchList, v.Decision);
            var s = await svc.SubmitAsync(new Tool1SubmitInputDto { Gm = "2203000205", Colour = "NEWC", Month = "11.2026", Qty = 400, SeenSeq = v.SeenSeq }, Buyer);
            Assert.Equal("Watch List — sent to Cat Head for approval", s.Toasts.Single());
            var mine = await svc.GetTodayAsync(Buyer, false);
            var row = Assert.Single(mine.Rows);
            Assert.Equal("⏳ Awaiting Cat Head", row.ApprovalBadge!.Text);
            Assert.Equal("dp-watch", row.DecisionClass);
            Assert.Equal("—", row.ApprovedQty);
            var all = await svc.GetTodayAsync(Planner, false);
            Assert.Equal(ledger.Reqs.Count, all.Rows.Count);
            Assert.Empty((await svc.GetTodayAsync(Planner, true)).Rows);
            var approver = new AppUserContext("ch@vmm.test", "Cat Head", new[] { Roles.CatHead }, false);
            Assert.Single((await svc.GetTodayAsync(approver, false)).Rows);
            Assert.Equal("✓ Approved by Ann · 1,200", Tool1Requests.Badge(new JsonObject { ["status"] = "approved", ["byName"] = "Ann", ["qty"] = 1200 })!.Text);
        }

        [Theory]
        [InlineData(250.0, false, 1000, 250)]
        [InlineData(250.9, false, 1000, 250)]
        [InlineData(5000.0, false, 1000, 1000)]
        [InlineData(-3.0, false, 1000, 0)]
        [InlineData(double.NaN, false, 1000, 0)]
        [InlineData(500.0, true, 1000, 0)]
        public void Confirmation_clamp(double value, bool declined, double max, double expected) =>
            Assert.Equal(expected, new Tool1Confirmation(value, declined).Resolve(max));

        [Fact]
        public async Task Form_options_route_and_missing_files()
        {
            var (svc, _) = Make();
            var o = await svc.GetFormOptionsAsync("1101000101");
            Assert.Equal("B", o.EngineRoute);
            Assert.Equal("₹399", o.Strip!.Mrp);
            Assert.Equal(18, o.Months.Count);
            Assert.Empty(o.MissingFiles);
            Assert.Equal("A", (await svc.GetFormOptionsAsync("2203000201")).EngineRoute);
            Assert.Equal("— enter material first —", (await svc.GetFormOptionsAsync("ab")).ColourPlaceholder);
        }
    }
}
