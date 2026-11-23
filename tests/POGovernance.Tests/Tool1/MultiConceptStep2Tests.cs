using DecisionEngine.Common;
using DecisionEngine.Model;
using DecisionEngine.Sap;
using DecisionEngine.Tool2;
using static POGovernance.Tests.Tool1.MultiConceptStep1Tests;

namespace POGovernance.Tests.Tool1
{
    /// <summary>
    /// Multi concept rule, Step 2: a Q1 is allowed only when its per-day sell-thru (first 3 weeks) is at least the minimum of
    /// the option's Entry / Mid / Fashion type (Entry 2.0 %, Mid 1.5 %, Fashion 1.5 %/day); below → Block; the Brand × MC ×
    /// Flag × MRP not in the mapping → Hold "Planner has not maintained this MC-MRP in the master." BOBs: the minimum is
    /// checked on the oldest other colour.
    /// </summary>
    public class MultiConceptStep2Tests
    {
        /// <summary>Eight NON-PL peers at 0.1 … 0.8 %/day, so an option at 1 %/day or more ranks Q1 (and the absolute block does not apply).</summary>
        private static Builder LowPeers(string tag = "Mid")
        {
            var b = new Builder().Peers(start: 0.001, step: 0.001);
            b.Emf.Clear();
            b.Emf.Add(R(("Brand Type", "NON-PL"), ("MC CD", "100"), ("Flag", "Normal"), ("MRP", "499"), ("EMF", tag)));
            return b;
        }

        [Theory]
        [InlineData("Entry", 0.0199, "Block")]
        [InlineData("Entry", 0.02, "Allow")]      // "at least": equal is allowed
        [InlineData("Mid", 0.0149, "Block")]
        [InlineData("Mid", 0.015, "Allow")]
        [InlineData("Fashion", 0.0149, "Block")]
        [InlineData("Fashion", 0.015, "Allow")]
        public void A_Q1_needs_the_minimum_of_its_type(string tag, double pdstr, string decision)
        {
            var a = Run(LowPeers(tag).Add("T1", "BLU", pdstr).Data(), "T1", "BLU");
            Assert.Equal("Top 20%", a.Quartile);
            Assert.Equal(decision, a.Decision);
            Assert.Equal(tag, a.EmfTag);
            Assert.Equal(EmfMapping.MinPdstr(tag), a.EmfMin);
            var min = tag == "Entry" ? "2.0" : "1.5";
            if (decision == "Allow")
            {
                Assert.Equal(EmfMapping.CheckOk, a.EmfCheck);
                Assert.EndsWith("— top 20% and at least the " + tag + " minimum " + min + "%/day. Allow.", a.DecisionReason);
                Assert.Equal(SapOutcomePath.PeerQ1, MultiConceptStep1Tests.Sap(a));
            }
            else
            {
                Assert.Equal(EmfMapping.CheckBelow, a.EmfCheck);
                Assert.EndsWith("— top 20% but below the " + tag + " minimum " + min + "%/day (first 3 weeks). Block. Do not raise PO.", a.DecisionReason);
                Assert.Equal(SapOutcomePath.BlockBelowEmfMinimum, MultiConceptStep1Tests.Sap(a));
                Assert.Equal(Decisions.Cancel, Tool2Engine.MapA(a.Decision));
            }
        }

        [Fact]
        public void A_Q1_whose_MC_MRP_is_not_in_the_mapping_is_a_Hold_with_the_planner_message()
        {
            var b = LowPeers().Add("T1", "BLU", 0.05, mrp: "599");      // only MRP 499 is mapped
            for (var i = 0; i < 8; i++) b.Add("Q" + i, "BLK", 0.001 * (i + 1), mrp: "599");
            var a = Run(b.Data(), "T1", "BLU");
            Assert.Equal("Top 20%", a.Quartile);
            Assert.Equal(Decisions.Hold, a.Decision);
            Assert.StartsWith("Planner has not maintained this MC-MRP in the master. (NON-PL · MC 100 · Flag Normal · MRP 599 is not in the Entry / Mid / Fashion mapping.", a.DecisionReason);
            Assert.Equal(EmfMapping.CheckNotMapped, a.EmfCheck);
            Assert.Null(a.EmfTag);

            Assert.Equal(SapOutcomePath.EmfNotMaintained, MultiConceptStep1Tests.Sap(a));
            var rule = SapVerdictMap.Rule(SapOutcomePath.EmfNotMaintained);
            Assert.Equal(("HOLD", "EMF_NOT_MAINTAINED", "EMF-00"), (rule.Verdict, rule.ReasonCode, rule.RuleId));

            // PO Assessment: not Cancel — the line cannot be judged until the planner maintains the mapping
            Assert.Equal(EmfMapping.CheckNotMapped, Tool2EngineA.Run(b.Data(), "T1", "BLU", "True Repeat", ResolveIndex.ResolveGm(b.Data(), "T1")).EmfCheck);
        }

        [Fact]
        public void Without_any_mapping_uploaded_every_Q1_is_a_Hold()
        {
            var b = LowPeers().Add("T1", "BLU", 0.05);
            b.Emf.Clear();
            Assert.Equal(Decisions.Hold, Run(b.Data(), "T1", "BLU").Decision);
        }

        [Fact]
        public void Only_a_Q1_is_checked_Q2_to_Q4_stay_Block_and_the_allows_without_ranking_are_unchanged()
        {
            var b = new Builder().Peers().Add("T1", "BLU", 0.055).Add("YOUNG", "BLU", 0.001, wof: 3);
            b.Emf.Clear();                                              // nothing mapped
            var q2 = Run(b.Data(), "T1", "BLU");
            Assert.Equal(("Below 30%", Decisions.Block), (q2.Quartile, q2.Decision));
            Assert.Null(q2.EmfCheck);
            var young = Run(b.Data(), "YOUNG", "BLU");
            Assert.Equal(Decisions.Allow, young.Decision);             // less than 4 weeks on floor
            Assert.Null(young.EmfCheck);
        }

        [Fact]
        public void The_flag_NON_BOGO_is_the_mappings_Normal()
        {
            Assert.Equal(EmfMapping.Key("NON-PL", "100", "Normal", "499"), EmfMapping.Key("non-pl", "100.0", "NON-BOGO", "499"));
            Assert.NotEqual(EmfMapping.Key("NON-PL", "100", "Normal", "499"), EmfMapping.Key("NON-PL", "100", "BOGO", "499"));
            var a = Run(LowPeers().Add("T1", "BLU", 0.05, flag: "NON-BOGO").Data(), "T1", "BLU");
            Assert.Equal(("Mid", Decisions.Allow), (a.EmfTag, a.Decision));
        }

        [Fact]
        public void The_result_card_shows_the_minimum_check()
        {
            DecisionEngine.Results.Tool1.Tool1MinimumDto? Card(DecisionEngine.Results.Tool1.Tool1EngineAResult a) =>
                DecisionEngine.Tool1.Tool1CardBuilder.Build(new DecisionEngine.Tool1.Tool1Outcome
                {
                    Engine = "A", OptionType = a.OptionType, Gm = "T1", Colour = "BLU", Month = "12.2026", Qty = 100, A = a,
                    Decision = a.Decision, DecisionReason = a.DecisionReason
                }, new EngineParams(), "2026-11-20").A!.Minimum;

            var ok = Card(Run(LowPeers("Entry").Add("T1", "BLU", 0.046).Data(), "T1", "BLU"))!;
            Assert.Equal(("Step 4 — Minimum Sell-Thru (Entry ≥ 2.0%/day)", "4.6%/day ≥ 2.0%/day → Allow"), (ok.Heading, ok.Text));
            var below = Card(Run(LowPeers("Entry").Add("T1", "BLU", 0.019).Data(), "T1", "BLU"))!;
            Assert.Equal("1.9%/day < 2.0%/day → Block", below.Text);
            var b = LowPeers().Add("T1", "BLU", 0.05);
            b.Emf.Clear();
            var hold = Card(Run(b.Data(), "T1", "BLU"))!;
            Assert.Equal(("Step 4 — Minimum Sell-Thru (not in the master)", "Planner has not maintained this MC-MRP in the master. → Hold"), (hold.Heading, hold.Text));
            Assert.Null(Card(Run(new Builder().Peers().Add("T1", "BLU", 0.055).Data(), "T1", "BLU")));   // Q2: not checked
        }

        // ── BOBs: the minimum on the oldest other colour ───────────────────

        [Fact]
        public void BOBs_checks_the_oldest_colour_most_weeks_on_floor()
        {
            // both other colours rank Q1; RED is the oldest (10 weeks) and sells below the Mid minimum
            var blocked = Run(LowPeers().Add("T1", "RED", 0.014, wof: 10).Add("T1", "BLU", 0.09, wof: 6).Data(), "T1", "NEW");
            Assert.Equal(Decisions.Block, blocked.Decision);
            Assert.Equal("BOBs: All 2 sibling colourway(s) with WOF ≥ 4w are in the top 20% (Allow band), but the oldest colour RED (10w on floor) sells 1.4%/day — below the Mid minimum 1.5%/day. New colour blocked.", blocked.DecisionReason);
            Assert.Equal(0.014, blocked.EmfPdstr);
            Assert.Equal(SapOutcomePath.BlockBelowEmfMinimum, MultiConceptStep1Tests.Sap(blocked));

            // a younger colour below the minimum does not matter
            var allowed = Run(LowPeers().Add("T1", "RED", 0.02, wof: 10).Add("T1", "BLU", 0.012, wof: 6).Data(), "T1", "NEW");
            Assert.Equal(Decisions.Allow, allowed.Decision);
            Assert.Equal(SapOutcomePath.BobsSiblingsOk, MultiConceptStep1Tests.Sap(allowed));
        }

        [Fact]
        public void BOBs_of_an_MC_MRP_not_in_the_mapping_is_a_Hold()
        {
            var b = LowPeers().Add("T1", "RED", 0.05, wof: 10);
            b.Emf.Clear();
            b.Emf.Add(R(("Brand Type", "NON-PL"), ("MC CD", "999"), ("Flag", "Normal"), ("MRP", "499"), ("EMF", "Entry")));
            var a = Run(b.Data(), "T1", "NEW");
            Assert.Equal(Decisions.Hold, a.Decision);
            Assert.StartsWith(EmfMapping.NotMaintainedMessage, a.DecisionReason);
            Assert.Equal(SapOutcomePath.EmfNotMaintained, MultiConceptStep1Tests.Sap(a));
        }
    }
}
