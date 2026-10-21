using System.Text.Json;
using System.Text.Json.Nodes;
using DecisionEngine.Model;
using DecisionEngine.Sap;
using DecisionEngine.Tool1;

namespace POGovernance.Tests.Sap
{
    /// <summary>SAP input normalisation (GM, colour, delivery month) on the Tool 1 parity fixture.</summary>
    public class SapInputNormalizerTests
    {
        private static readonly EngineData Data = SapKit.Data;

        [Theory]
        [InlineData("1101000101", null, "1101000101", "exact")]
        [InlineData("000000001101000101", null, "1101000101", "no-leading-zeros")]       // SAP internal format, 18 digits
        [InlineData("  000000002203000201 ", null, "2203000201", "no-leading-zeros")]     // BSR GM
        [InlineData(null, "1101000101", "1101000101", "article")]                        // single article: no generic
        [InlineData(null, "00001101000101", "1101000101", "article-no-leading-zeros")]
        [InlineData("000000009999999999", "000000009999999999", null, "unknown")]
        [InlineData("", "", null, "unknown")]
        public void Generic_article_is_matched_exactly_then_without_leading_zeros(string? generic, string? article, string? gm, string how)
        {
            var (g, h) = SapInputNormalizer.ResolveGm(Data, generic, article);
            Assert.Equal(gm, g);
            Assert.Equal(how, h);
        }

        [Theory]
        [InlineData("WHT", "WHT", "master-code")]
        [InlineData("wht", "WHT", "master-code")]
        [InlineData("WHT DESC", "WHT", "master-desc")]          // the Colour Master description
        [InlineData(" wht desc ", "WHT", "master-desc")]
        [InlineData("zzz", "ZZZ", "as-sent")]                   // unknown: typed colour, upper-cased (formGetColour)
        [InlineData(null, null, "missing")]
        [InlineData("  ", null, "missing")]
        public void Colour_is_matched_by_code_then_description(string? sent, string? colour, string how)
        {
            var (c, h) = SapInputNormalizer.ResolveColour(Data, "1101000101", sent);
            Assert.Equal(colour, c);
            Assert.Equal(how, h);
        }

        [Fact]
        public void A_colour_that_only_the_data_holds_is_returned_in_the_data_spelling()
        {
            var files = new Dictionary<string, IReadOnlyList<SheetRow>?>
            {
                [DatasetKeys.F1] = new[] { Row(("Generic Material", "G1"), ("Color", "Navy Blue"), ("OP STK", "5")) },
                [DatasetKeys.ColMaster] = new[] { Row(("Colour", "BLK"), ("Colour Desc", "BLACK")) },
            };
            var d = new EngineData(files, new EngineParams(), 1, 1);
            Assert.Equal(("Navy Blue", "data"), SapInputNormalizer.ResolveColour(d, "G1", "NAVY BLUE"));
            Assert.Equal(("BLK", "master-desc"), SapInputNormalizer.ResolveColour(d, "G1", "black"));
            Assert.Equal(("NAVY", "as-sent"), SapInputNormalizer.ResolveColour(d, "G2", "navy"));
        }

        private static SheetRow Row(params (string K, string V)[] kv) =>
            SheetRow.FromJson(JsonDocument.Parse(JsonSerializer.Serialize(kv.ToDictionary(x => x.K, x => x.V))).RootElement);

        [Theory]
        [InlineData("2026-11-15", "11.2026")]
        [InlineData("2027-01-31", "01.2027")]
        [InlineData("2026-10-21", "10.2026")]
        [InlineData("2026-03-01", "10.2026")]   // overdue delivery: the current month
        [InlineData("2026-11-31", null)]
        [InlineData("15.11.2026", null)]
        [InlineData(null, null)]
        public void Delivery_month_is_MM_YYYY_of_the_delivery_date(string? date, string? month) =>
            Assert.Equal(month, SapInputNormalizer.DeliveryMonth(date, SapKit.Today));

        [Theory]
        [InlineData("000000000100234500", "100234500")]
        [InlineData("100234500", "100234500")]
        [InlineData("0000", "0")]
        [InlineData("", "")]
        public void Leading_zeros(string s, string expected) => Assert.Equal(expected, SapInputNormalizer.StripLeadingZeros(s));
    }

    /// <summary>The SAP decision engine on the parity fixture: one case per verdict path, grouping, policies, rule hooks.</summary>
    public class SapDecisionEngineTests
    {
        private static readonly SapDecisionEngine Engine = new(new Tool1DecisionEngine());
        private static readonly IReadOnlyList<JsonObject> NoLedger = Array.Empty<JsonObject>();

        private static SapItemInput Item(int poItem, string gm, string? colour, double qty, string? date = "2026-11-15", double? netValue = null) => new()
        {
            PoItem = poItem, Article = "0000000" + gm + "01", GenericArticle = "00000000" + gm, Colour = colour, OrderQty = qty, Uom = "PC",
            DeliveryDate = date, NetValue = netValue
        };

        private static SapDecisionResult Decide(SapDecisionOptions? o, params SapItemInput[] items) =>
            Engine.Decide(SapKit.Data, NoLedger, SapKit.Today, items, o ?? new SapDecisionOptions());

        [Theory]
        [InlineData("1101000101", "WHT", 100, "ALLOW", "COVER_WITHIN_NORM", "COVER-00", "NONE")]
        [InlineData("1101000101", "WHT", 50000, "REDUCE", "COVER_ABOVE_NORM", "COVER-01", "BUYER")]
        [InlineData("1101000101", "RED", 100, "HOLD", "COVER_NO_HEADROOM", "COVER-02", "CATHEAD")]
        [InlineData("1101000101", "GRY", 100, "WATCH", "MIX_BELOW_MIN", "MIX-01", "CATHEAD")]
        [InlineData("2203000201", "OWH", 100, "HOLD", "BLOCK", "BLOCK-Q4", "CATHEAD")]
        [InlineData("2203000201", "BLU", 100, "ALLOW", "PEER_Q1", "PEER-Q1", "NONE")]
        [InlineData("2203000201", "BEG", 100, "ALLOW", "PEER_Q2", "PEER-Q2", "NONE")]
        [InlineData("2203000202", "BLK", 100, "WATCH", "PEER_Q3", "PEER-Q3-GAP", "CATHEAD")]
        [InlineData("2203009106", "BLU", 100, "ALLOW", "PEER_Q3", "PEER-Q3-OK", "NONE")]
        [InlineData("2203000201", "ZZZ", 100, "WATCH", "BOBS_SIBLING_LOW", "BOBS-02", "CATHEAD")]
        [InlineData("2203000203", "ZZZ", 100, "ALLOW", "BOBS_SIBLINGS_OK", "BOBS-01", "NONE")]
        [InlineData("2203000204", "BLK", 100, "ALLOW", "LOW_HISTORY", "WOF-00", "NONE")]
        [InlineData("8800000001", "BLK", 100, "ALLOW", "NO_PLAN_DATA", "PLAN-00", "NONE")]
        [InlineData("5509000501", "RED", 100, "ALLOW", "NO_PEER_POOL", "PEER-00", "NONE")]
        [InlineData("5509000502", "ZZZ", 100, "ALLOW", "NEW_ARTICLE", "NEW-00", "NONE")]
        public void Each_verdict_path_on_the_fixture(string gm, string colour, double qty, string verdict, string reason, string rule, string route)
        {
            var r = Decide(null, Item(10, gm, colour, qty));
            Assert.False(r.Rejected, r.RejectMessage);
            var i = Assert.Single(r.Items);
            Assert.Equal(verdict, i.Verdict);
            Assert.Equal(SapVerdicts.Code(verdict), i.VerdictCode);
            Assert.Equal(reason, i.ReasonCode);
            Assert.Equal(rule, i.RuleId);
            Assert.Equal(verdict, r.OverallVerdict);
            Assert.Equal(route, r.RouteTo);
            Assert.False(r.BmHeadRequired);
            Assert.InRange(i.ReasonText.Length, 1, SapDecisionEngine.ReasonTextMax);
            Assert.InRange(r.SummaryText.Length, 1, SapDecisionEngine.SummaryTextMax);
            Assert.Equal(verdict == "REDUCE", i.MaxQty != null);
        }

        [Fact]
        public void REDUCE_carries_Max_Permissible_and_the_engine_metrics()
        {
            var r = Decide(null, Item(10, "1101000101", "WHT", 50000));
            var i = r.Items[0];
            var direct = new Tool1DecisionEngine().Decide(SapKit.Data, NoLedger, SapKit.Today, 0, "1101000101", "WHT", "11.2026", 50000, null);
            Assert.Equal(direct.BFirst!.MaxPermQty, i.MaxQty);
            Assert.Equal(direct.BFirst.MaxPermQty, i.Metrics.HeadroomQty);
            Assert.Equal(direct.BFirst.OpStock, i.Metrics.StockQty);
            Assert.Equal(direct.BFirst.ExistingPO!.Values.Sum(), i.Metrics.OpenPoQty);
            Assert.NotNull(i.Metrics.PlanQty);
            Assert.NotNull(i.Metrics.CoverDays);
            Assert.Null(i.Metrics.SellThroughPct);
            Assert.StartsWith("Item 10: reduce to " + DecisionEngine.Common.Js.Fmt(i.MaxQty!.Value), r.SummaryText);
            Assert.Equal("B", i.Engine);
            Assert.Equal(Decisions.Hold, i.EngineDecision);
        }

        [Fact]
        public void Multi_concept_items_report_sell_through()
        {
            var i = Decide(null, Item(10, "2203000201", "BLU", 100)).Items[0];
            Assert.NotNull(i.Metrics.SellThroughPct);
            Assert.Null(i.Metrics.StockQty);
        }

        [Fact]
        public void Size_variants_of_one_GM_colour_month_are_decided_together_and_max_qty_is_split()
        {
            var alone = Decide(null, Item(10, "1101000101", "WHT", 50000)).Items[0].MaxQty!.Value;
            var r = Decide(null, Item(10, "1101000101", "WHT", 30000), Item(20, "1101000101", "WHT", 20000, "2026-11-28"), Item(30, "2203000201", "BLU", 10));
            Assert.Equal("REDUCE", r.OverallVerdict);
            var (a, b, c) = (r.Items[0], r.Items[1], r.Items[2]);
            Assert.Equal(50000, a.GroupQty);
            Assert.Equal(50000, b.GroupQty);
            Assert.Equal(alone, a.MaxQty + b.MaxQty);               // same decision as one item of 50,000
            Assert.True(a.MaxQty > b.MaxQty);
            Assert.Equal("ALLOW", c.Verdict);
            Assert.Null(c.MaxQty);
        }

        [Theory]
        [InlineData(1000, new double[] { 600, 400 }, new double[] { 600, 400 })]
        [InlineData(1000, new double[] { 1, 1, 1 }, new double[] { 1, 1, 1 })]
        [InlineData(10, new double[] { 1, 1, 1 }, new double[] { 1, 1, 1 })]
        [InlineData(2, new double[] { 3, 3, 3 }, new double[] { 1, 1, 0 })]
        [InlineData(7, new double[] { 5, 5 }, new double[] { 4, 3 })]
        [InlineData(0, new double[] { 5, 5 }, new double[] { 0, 0 })]
        [InlineData(500, new double[] { 1200 }, new double[] { 500 })]
        public void SplitMax_is_proportional_with_largest_remainders(double max, double[] qtys, double[] expected) =>
            Assert.Equal(expected, SapDecisionEngine.SplitMax(max, qtys));

        [Fact]
        public void Unknown_article_rejects_the_request_by_default()
        {
            var r = Decide(null, Item(10, "1101000101", "WHT", 100), Item(20, "9999999999", "BLK", 100));
            Assert.True(r.Rejected);
            Assert.Contains("Item 20", r.RejectMessage);
            Assert.Contains("unknown to the tool", r.RejectMessage);
        }

        [Fact]
        public void UnknownItemPolicy_WATCH_decides_the_rest_and_watches_the_unknown_items()
        {
            var r = Decide(new SapDecisionOptions { UnknownItemPolicy = "WATCH" }, Item(10, "1101000101", "WHT", 100), Item(20, "9999999999", "BLK", 100));
            Assert.False(r.Rejected);
            Assert.Equal("ALLOW", r.Items[0].Verdict);
            Assert.Equal("WATCH", r.Items[1].Verdict);
            Assert.Equal("CANNOT_VALIDATE", r.Items[1].ReasonCode);
            Assert.Equal("WATCH", r.OverallVerdict);
            Assert.Equal("CATHEAD", r.RouteTo);
        }

        [Fact]
        public void Nothing_decidable_is_always_rejected()
        {
            var o = new SapDecisionOptions { UnknownItemPolicy = "WATCH" };
            Assert.True(Decide(o, Item(10, "9999999999", "BLK", 100)).Rejected);
            Assert.True(Decide(o, Item(10, "1101000101", null, 100)).Rejected);                     // no colour
            Assert.True(Decide(o, Item(10, "1101000101", "WHT", 100, date: null)).Rejected);        // no delivery date
        }

        [Fact]
        public void BlockVerdict_BLOCK_sends_the_new_value()
        {
            var r = Decide(new SapDecisionOptions { BlockVerdict = "BLOCK" }, Item(10, "2203000201", "OWH", 100), Item(20, "1101000101", "RED", 100));
            Assert.Equal("BLOCK", r.Items[0].Verdict);
            Assert.Equal("B", r.Items[0].VerdictCode);
            Assert.Equal("HOLD", r.Items[1].Verdict);
            Assert.Equal("BLOCK", r.OverallVerdict);
            Assert.Equal("CATHEAD", r.RouteTo);
        }

        [Fact]
        public void BmHead_rule_hook_is_off_by_default_and_routes_to_BMHEAD_when_on()
        {
            var items = new[] { Item(10, "1101000101", "RED", 100, netValue: 900000) };
            Assert.False(Decide(null, items).BmHeadRequired);
            var on = new SapDecisionOptions { BmHeadRule = new SapBmHeadRule { Enabled = true, MinNetValue = 500000 } };
            var r = Decide(on, items);
            Assert.True(r.BmHeadRequired);
            Assert.Equal("BMHEAD", r.RouteTo);
            Assert.False(Decide(on, Item(10, "1101000101", "RED", 100, netValue: 1000)).BmHeadRequired);
            Assert.False(Decide(on, Item(10, "1101000101", "GRY", 100, netValue: 900000)).BmHeadRequired);   // WATCH: only with IncludeWatch
            on.BmHeadRule.IncludeWatch = true;
            Assert.True(Decide(on, Item(10, "1101000101", "GRY", 100, netValue: 900000)).BmHeadRequired);
        }

        [Fact]
        public void Todays_Tool1_ledger_is_counted_but_never_written()
        {
            var ledger = new List<JsonObject>
            {
                new()
                {
                    ["gm"] = "1101000101", ["colour"] = "WHT", ["month"] = "11.2026", ["qty"] = 5000, ["engine"] = "B", ["decision"] = "Auto Allow",
                    ["finalApprovedQty"] = 5000, ["reqDate"] = SapKit.Today,
                    ["info"] = new JsonObject { ["brand"] = "NON-PL", ["mcCd"] = "1101", ["mrp"] = "399", ["flag"] = "NON-BOGO" }
                }
            };
            var before = ledger[0].ToJsonString();
            var without = Decide(null, Item(10, "1101000101", "WHT", 50000)).Items[0].MaxQty!.Value;
            var with = Engine.Decide(SapKit.Data, ledger, SapKit.Today, new[] { Item(10, "1101000101", "WHT", 50000) }, new SapDecisionOptions()).Items[0].MaxQty!.Value;
            Assert.Equal(without - 5000, with);
            Assert.Single(ledger);
            Assert.Equal(before, ledger[0].ToJsonString());
        }

        [Fact]
        public void Missing_data_files_decide_nothing()
        {
            var empty = new EngineData(new Dictionary<string, IReadOnlyList<SheetRow>?>(), new EngineParams(), 1, 1);
            var r = Engine.Decide(empty, NoLedger, SapKit.Today, new[] { Item(10, "1101000101", "WHT", 100) }, new SapDecisionOptions());
            Assert.Equal(DatasetKeys.Required, r.MissingFiles);
            Assert.Empty(r.Items);
        }

        [Fact]
        public void Summary_lists_the_flagged_items_most_severe_first()
        {
            var r = Decide(null, Item(10, "1101000101", "WHT", 100), Item(20, "1101000101", "GRY", 100), Item(30, "1101000101", "RED", 100));
            Assert.Equal("HOLD", r.OverallVerdict);
            Assert.StartsWith("Item 30: hold - ", r.SummaryText);
            Assert.Contains("; Item 20: watch - ", r.SummaryText);
            Assert.DoesNotContain("Item 10", r.SummaryText);
            Assert.Equal("All 1 item(s) within the norms", Decide(null, Item(10, "1101000101", "WHT", 100)).SummaryText);
        }
    }
}
