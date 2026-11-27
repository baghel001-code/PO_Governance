using DecisionEngine.Common;
using DecisionEngine.Model;
using DecisionEngine.Sap;
using DecisionEngine.Tool1;
using DecisionEngine.Tool2;
using static POGovernance.Tests.Tool1.MultiConceptStep1Tests;

namespace POGovernance.Tests.Tool1
{
    /// <summary>
    /// Multi concept rule, Step 3: percentile bands instead of Q1–Q4 — top 20% and the Entry / Mid / Fashion minimum →
    /// Allow; 20–30% and the minimum → Exception to the B&amp;M Head (MRA Level 8); below the top 30% → Block. BOBs: the worst band of the
    /// other colours decides.
    /// </summary>
    public class MultiConceptStep3Tests
    {
        /// <summary>Nine peers (1 … 8 %/day and 10 %/day): with the option the pool has 10, so each option step is 10 points.</summary>
        private static Builder Ten() => new Builder().Peers().Add("PX", "BLK", 0.10);

        [Theory]
        [InlineData(0.085, "Top 20%", Decisions.Allow)]       // 9 of 10 at or below = 90th pct
        [InlineData(0.075, "20–30%", Decisions.Exception)]    // 8 of 10 = exactly 80% → the lower band
        [InlineData(0.065, "Below 30%", Decisions.Block)]     // 7 of 10 = exactly 70% → the lower band
        public void The_band_is_the_share_of_the_pool_at_or_below_the_option(double pdstr, string band, string decision)
        {
            var a = Run(Ten().Add("T1", "BLU", pdstr).Data(), "T1", "BLU");
            Assert.Equal(10, a.CleanPool.Count);
            Assert.Equal(band, a.Quartile);
            Assert.Equal(decision, a.Decision);
            Assert.Equal(band, BsrPeerRank.Band(pdstr, a.CleanPool));
        }

        [Fact]
        public void The_20_to_30_percent_band_meeting_its_minimum_is_an_exception_for_the_BM_head()
        {
            var a = Run(Ten().Add("T1", "BLU", 0.075).Data(), "T1", "BLU");
            Assert.Equal(Decisions.Exception, a.Decision);
            Assert.Equal("True Repeat: ranks 20–30% (80 percentile of 10 styles, Brand + MC + MRP + Flag, CY BSR (AW26)). Sells 7.50%/day — " +
                         "meets the Mid minimum of 1.50%/day. Not in the top 20%: raise an Exception at Level 8 to the B&M Head with your reason. " +
                         "Counts as Block until approved.", a.DecisionReason);
            Assert.Equal(EmfMapping.CheckOk, a.EmfCheck);
            Assert.Equal("this option", a.EmfOn);
            Assert.Equal(SapOutcomePath.PeerException, MultiConceptStep1Tests.Sap(a));
            Assert.Equal(Decisions.Exception, Tool2Engine.MapA(a.Decision));          // PO Assessment action
            var rule = SapVerdictMap.Rule(SapOutcomePath.PeerException);
            Assert.Equal(("HOLD", "PEER_EXCEPTION", "EXC-L8"), (rule.Verdict, rule.ReasonCode, rule.RuleId));

            // the reference view: "Step 3 — Rank in peer group (… percentile — … styles, Brand + MC + MRP + Flag)", orange, Step 4 box
            var card = Card(a);
            Assert.Equal("80 percentile — 20–30%", card.A!.Step3!.RankText);
            Assert.Equal("Step 3 — Rank in peer group (80 percentile — 10 styles, Brand + MC + MRP + Flag)", card.A.Step3.Heading);
            Assert.Equal("Rank within peer group of 10 styles", card.A.Step3.PoolText);
            Assert.Equal("#C2410C", card.DecisionColour);
            Assert.Equal("Step 4 — EMF minimum (Entry 2.0% · Mid 1.5% · Fashion 1.5% per day)", card.A.Minimum!.Heading);
            Assert.Equal("✓ MEETS THE MINIMUM", card.A.Minimum.Lines[^1].Value);
            Assert.Equal("WOF ≥ 4w\n(Peer Pool · Brand+MC+MRP+Flag)", card.A.Step1[1].Key);
        }

        /// <summary>Eight peers at 0.1 … 0.8 %/day and three at 2, 3, 4 %/day: an option at 1.2 %/day ranks 9 of 12 = 75th (20–30%).</summary>
        private static Builder LowBand() => new Builder().Peers(start: 0.001, step: 0.001).Add("PX", "BLK", 0.02).Add("PY", "BLK", 0.03).Add("PZ", "BLK", 0.04);

        [Fact]
        public void The_20_to_30_percent_band_below_its_minimum_is_blocked_before_level_8()
        {
            var a = Run(LowBand().Add("T1", "BLU", 0.012).Data(), "T1", "BLU");
            Assert.Equal("20–30%", a.Quartile);
            Assert.Equal(Decisions.Block, a.Decision);
            Assert.Equal(EmfMapping.CheckBelow, a.EmfCheck);
            Assert.EndsWith("Sells 1.20%/day — below the Mid minimum of 1.50%/day. Block. Do not raise PO.", a.DecisionReason);
            Assert.Equal(SapOutcomePath.BlockBelowEmfMinimum, MultiConceptStep1Tests.Sap(a));
            Assert.Equal("✗ BELOW THE MINIMUM", Card(a).A!.Minimum!.Lines[^1].Value);
        }

        [Fact]
        public void The_20_to_30_percent_band_not_in_the_mapping_is_a_Hold()
        {
            var b = Ten().Add("T1", "BLU", 0.075);
            b.Emf.Clear();
            var a = Run(b.Data(), "T1", "BLU");
            Assert.Equal(("20–30%", Decisions.Hold), (a.Quartile, a.Decision));
            Assert.StartsWith(EmfMapping.NotMaintainedMessage, a.DecisionReason);
            Assert.Equal(SapOutcomePath.EmfNotMaintained, MultiConceptStep1Tests.Sap(a));
        }

        [Fact]
        public void Below_1_percent_per_day_the_exception_band_is_still_an_absolute_block()
        {
            var a = Run(new Builder().Peers(start: 0.001, step: 0.001).Add("PX", "BLK", 0.0095).Add("T1", "BLU", 0.0075).Data(), "T1", "BLU");
            Assert.Equal("20–30%", a.Quartile);
            Assert.Equal(Decisions.Block, a.Decision);
            Assert.StartsWith("Absolute Block override", a.DecisionReason);
        }

        // ── BOBs: the worst band of the other colours ───────────────────────

        [Fact]
        public void BOBs_with_another_colour_in_the_20_to_30_percent_band_is_an_exception()
        {
            var a = Run(Ten().Add("T1", "RED", 0.075, wof: 8).Add("T1", "BLU", 0.09, wof: 6).Data(), "T1", "NEW");
            Assert.Equal(Decisions.Exception, a.Decision);
            Assert.StartsWith("BOBs: other colour RED (8w) ranks 20–30% (", a.DecisionReason);
            Assert.EndsWith("). Oldest colour RED (8w) sells 7.50%/day — meets the Mid minimum of 1.50%/day. Not all colours are in the top 20%: " +
                            "raise an Exception at Level 8 to the B&M Head with your reason. Counts as Block until approved.", a.DecisionReason);
            Assert.Equal("20–30%", a.Quartile);                 // the colour that drove it
            Assert.StartsWith("Worst other colour: RED (8w) at ", a.RankNote);
            Assert.EndsWith(" (20–30%). Shown: worst colour", a.RankNote);
            Assert.Equal(a.RankNote, Card(a, "NEW").A!.Step3!.PoolText);
            Assert.Equal(SapOutcomePath.PeerException, MultiConceptStep1Tests.Sap(a));
        }

        [Fact]
        public void BOBs_in_the_20_to_30_percent_band_whose_oldest_colour_is_below_its_minimum_is_blocked()
        {
            // the other colour RED ranks 20–30% but, as the oldest colour, sells below the Mid minimum: no exception
            var low = Run(LowBand().Add("T1", "RED", 0.012, wof: 10).Data(), "T1", "NEW");
            Assert.Equal(Decisions.Block, low.Decision);
            Assert.EndsWith("Oldest colour RED (10w) sells 1.20%/day — below the Mid minimum of 1.50%/day. New colour blocked.", low.DecisionReason);
            Assert.Equal(SapOutcomePath.BlockBelowEmfMinimum, MultiConceptStep1Tests.Sap(low));
        }

        [Fact]
        public void BOBs_with_any_other_colour_below_the_top_30_percent_is_blocked()
        {
            var a = Run(Ten().Add("T1", "RED", 0.075, wof: 8).Add("T1", "BLU", 0.09, wof: 6).Add("T1", "GRN", 0.02, wof: 9).Data(), "T1", "NEW");
            Assert.Equal(Decisions.Block, a.Decision);
            Assert.StartsWith("BOBs: other colour GRN (9w) ranks below the top 30% (", a.DecisionReason);
            Assert.Equal(SapOutcomePath.BlockBobsSibling, MultiConceptStep1Tests.Sap(a));
        }

        private static DecisionEngine.Results.Tool1.Tool1CardDto Card(DecisionEngine.Results.Tool1.Tool1EngineAResult a, string colour = "BLU") =>
            Tool1CardBuilder.Build(new Tool1Outcome
            {
                Engine = "A", OptionType = a.OptionType, Gm = "T1", Colour = colour, Month = "12.2026", Qty = 100, A = a,
                Decision = a.Decision, DecisionReason = a.DecisionReason
            }, new EngineParams(), "2026-11-27");

        [Fact]
        public void The_STR_caution_and_the_sibling_list_follow_the_bands()
        {
            Assert.Equal(BsrPeerRank.BandTop, Tool1EngineA.AllowBand);
            Assert.Equal(new[] { 2, 1, 0, -1 }, new[] { "Top 20%", "20–30%", "Below 30%", "Q1" }.Select(BsrPeerRank.BandOrder));
        }
    }
}
