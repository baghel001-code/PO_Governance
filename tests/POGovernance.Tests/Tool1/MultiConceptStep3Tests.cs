using DecisionEngine.Common;
using DecisionEngine.Model;
using DecisionEngine.Sap;
using DecisionEngine.Tool1;
using DecisionEngine.Tool2;
using static POGovernance.Tests.Tool1.MultiConceptStep1Tests;

namespace POGovernance.Tests.Tool1
{
    /// <summary>
    /// Multi concept rule, Step 3: percentile bands instead of Q1–Q4 — top 20% (and the Entry / Mid / Fashion minimum) →
    /// Allow; 20–30% → Exception to the B&amp;M Head (MRA Level 8); below the top 30% → Block. BOBs: the worst band of the
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
        public void The_20_to_30_percent_band_is_an_exception_for_the_BM_head_without_a_minimum_check()
        {
            var b = Ten().Add("T1", "BLU", 0.075);
            b.Emf.Clear();                                     // not in the mapping: no Hold — the exception does not need it
            var a = Run(b.Data(), "T1", "BLU");
            Assert.Equal(Decisions.Exception, a.Decision);
            Assert.Equal("True Repeat: 80th pct (20–30%) in peer group of 10 styles (CY BSR (AW26)). PDSTR = 7.5%/day — in the 20–30% band. " +
                         "Exception: raise it to the B&M Head with a reason (MRA Level 8), or do not raise.", a.DecisionReason);
            Assert.Null(a.EmfCheck);
            Assert.Equal(SapOutcomePath.PeerException, MultiConceptStep1Tests.Sap(a));
            Assert.Equal(Decisions.Exception, Tool2Engine.MapA(a.Decision));          // PO Assessment action
            var rule = SapVerdictMap.Rule(SapOutcomePath.PeerException);
            Assert.Equal(("HOLD", "PEER_EXCEPTION", "EXC-L8"), (rule.Verdict, rule.ReasonCode, rule.RuleId));

            var card = Tool1CardBuilder.Build(new Tool1Outcome
            {
                Engine = "A", OptionType = a.OptionType, Gm = "T1", Colour = "BLU", Month = "12.2026", Qty = 100, A = a,
                Decision = a.Decision, DecisionReason = a.DecisionReason
            }, new EngineParams(), "2026-11-23");
            Assert.Equal("80th percentile — 20–30% (Exception — B&M Head)", card.A!.Step3!.RankText);
            Assert.Equal("Step 3 — Percentile Band (80th pct — 10 styles)", card.A.Step3.Heading);
            Assert.Equal("#7C3AED", card.DecisionColour);
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
            Assert.Equal("BOBs: 1 of 2 sibling colourway(s) with WOF ≥ 4w are in the 20–30% band (RED), none below the top 30%. " +
                         "Exception: raise it to the B&M Head with a reason (MRA Level 8), or do not raise.", a.DecisionReason);
            Assert.Equal("20–30%", a.Quartile);                 // the colour that drove it
            Assert.Equal(SapOutcomePath.PeerException, MultiConceptStep1Tests.Sap(a));
        }

        [Fact]
        public void BOBs_with_any_other_colour_below_the_top_30_percent_is_blocked()
        {
            var a = Run(Ten().Add("T1", "RED", 0.075, wof: 8).Add("T1", "BLU", 0.09, wof: 6).Add("T1", "GRN", 0.02, wof: 9).Data(), "T1", "NEW");
            Assert.Equal(Decisions.Block, a.Decision);
            Assert.StartsWith("BOBs: 1 of 3 sibling colourway(s) with WOF ≥ 4w rank below the top 30% (GRN).", a.DecisionReason);
            Assert.Equal(SapOutcomePath.BlockBobsSibling, MultiConceptStep1Tests.Sap(a));
        }

        [Fact]
        public void The_STR_caution_and_the_sibling_list_follow_the_bands()
        {
            Assert.Equal(BsrPeerRank.BandTop, Tool1EngineA.AllowBand);
            Assert.Equal(new[] { 2, 1, 0, -1 }, new[] { "Top 20%", "20–30%", "Below 30%", "Q1" }.Select(BsrPeerRank.BandOrder));
        }
    }
}
