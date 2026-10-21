using System.Text.RegularExpressions;
using DecisionEngine.Model;
using DecisionEngine.Results.Tool1;
using DecisionEngine.Sap;
using DecisionEngine.Tool1;

namespace POGovernance.Tests.Sap
{
    /// <summary>
    /// The tool → SAP verdict mapping (handover §8 item 2, owner-approved): every Tool 1 engine outcome maps to its SAP verdict,
    /// reason code and rule id; Block → HOLD (reason BLOCK) by default and → BLOCK with SapInterface:BlockVerdict = BLOCK.
    /// </summary>
    public class SapVerdictMapTests
    {
        private static Tool1Outcome B(string decision, double? maxPerm = null, double? windowDemand = 100, string reason = "") => new()
        {
            Engine = "B", OptionType = "—", Decision = decision,
            BFirst = new Tool1EngineBResult { Decision = decision, MaxPermQty = maxPerm, WindowDemand = windowDemand, DecisionReason = reason }
        };

        private static Tool1Outcome A(string decision, string? quartile, string reason, string optionType = "True Repeat") => new()
        {
            Engine = "A", OptionType = optionType, Decision = decision,
            A = new Tool1EngineAResult { Decision = decision, Quartile = quartile, DecisionReason = reason, OptionType = optionType }
        };

        public static IEnumerable<object[]> Cases() => new[]
        {
            // Single Concept (runEngineB)
            new object[] { "B Auto Allow (cover within norm)", B(Decisions.AutoAllow, 500), SapOutcomePath.CoverWithinNorm, "ALLOW", "ALLOW", "COVER_WITHIN_NORM", "COVER-00" },
            new object[] { "B Auto Allow (no plan)", B(Decisions.AutoAllow, 0, 0, "No forecast data found for MC CD 1101 MRP 399 in F4."), SapOutcomePath.NoPlanData, "ALLOW", "ALLOW", "NO_PLAN_DATA", "PLAN-00" },
            new object[] { "B Hold, Max Permissible > 0", B(Decisions.Hold, 500), SapOutcomePath.CoverAboveNorm, "REDUCE", "REDUCE", "COVER_ABOVE_NORM", "COVER-01" },
            new object[] { "B Recommend Reduced Qty (README relabel)", B(Decisions.ReduceRecommended, 320), SapOutcomePath.CoverAboveNorm, "REDUCE", "REDUCE", "COVER_ABOVE_NORM", "COVER-01" },
            new object[] { "B Hold, Max Permissible 0", B(Decisions.Hold, 0), SapOutcomePath.CoverNoHeadroom, "HOLD", "HOLD", "COVER_NO_HEADROOM", "COVER-02" },
            new object[] { "B Hold, Max Permissible NaN", B(Decisions.Hold, double.NaN), SapOutcomePath.CoverNoHeadroom, "HOLD", "HOLD", "COVER_NO_HEADROOM", "COVER-02" },
            new object[] { "B Watch List (mix gate)", B(Decisions.WatchList), SapOutcomePath.MixBelowMin, "WATCH", "WATCH", "MIX_BELOW_MIN", "MIX-01" },
            new object[] { "B Cannot Validate", B(Decisions.CannotValidate), SapOutcomePath.CannotValidate, "WATCH", "WATCH", "CANNOT_VALIDATE", "CV-00" },
            // Multi Concept (runEngineA) — True Repeat
            new object[] { "A Allow Q1", A(Decisions.Allow, "Q1", "True Repeat: ranks Q1 (90th pct) …"), SapOutcomePath.PeerQ1, "ALLOW", "ALLOW", "PEER_Q1", "PEER-Q1" },
            new object[] { "A Allow Q2", A(Decisions.Allow, "Q2", "True Repeat: ranks Q2 (60th pct) …"), SapOutcomePath.PeerQ2, "ALLOW", "ALLOW", "PEER_Q2", "PEER-Q2" },
            new object[] { "A Allow Q3 (border zone)", A(Decisions.Allow, "Q3", "True Repeat: ranks Q3 … within border zone"), SapOutcomePath.PeerQ3WithinBorder, "ALLOW", "ALLOW", "PEER_Q3", "PEER-Q3-OK" },
            new object[] { "A Watch List Q3", A(Decisions.WatchList, "Q3", "True Repeat: ranks Q3 … gap exceeds border zone"), SapOutcomePath.PeerQ3BeyondBorder, "WATCH", "WATCH", "PEER_Q3", "PEER-Q3-GAP" },
            new object[] { "A Watch List Q4 (softened)", A(Decisions.WatchList, "Q4", "True Repeat: ranks Q4 … Softened from Block to Watch List."), SapOutcomePath.PeerQ4Softened, "WATCH", "WATCH", "PEER_Q4", "PEER-Q4-WATCH" },
            new object[] { "A Block Q4", A(Decisions.Block, "Q4", "True Repeat: ranks Q4 … Confirmed slow seller. Do not raise PO."), SapOutcomePath.BlockQ4, "HOLD", "BLOCK", "BLOCK", "BLOCK-Q4" },
            new object[] { "A Absolute Block", A(Decisions.Block, null, "Absolute Block override: GM+Colour (…) has AVG PERDAY SELL THRU% = 0.5% < 1%"), SapOutcomePath.BlockAbsolute, "HOLD", "BLOCK", "BLOCK", "BLOCK-ABS" },
            new object[] { "A Allow, WOF below gate", A(Decisions.Allow, null, "WOF = 2w < 4w gate. Insufficient selling history — Auto Allow."), SapOutcomePath.LowHistory, "ALLOW", "ALLOW", "LOW_HISTORY", "WOF-00" },
            new object[] { "A Allow, no peer pool", A(Decisions.Allow, null, "No peer pool available. Auto Allow."), SapOutcomePath.NoPeerPool, "ALLOW", "ALLOW", "NO_PEER_POOL", "PEER-00" },
            // Multi Concept — BOBs
            new object[] { "A BOBs siblings Q1/Q2", A(Decisions.Allow, "Q1", "BOBs: All 2 qualifying sibling colourway(s) rank Q2/Q1 — …", "BOBs"), SapOutcomePath.BobsSiblingsOk, "ALLOW", "ALLOW", "BOBS_SIBLINGS_OK", "BOBS-01" },
            new object[] { "A BOBs sibling Q3/Q4", A(Decisions.WatchList, "Q4", "BOBs: 3 sibling colourway(s) with WOF ≥ 4w found. At least one ranks Q4/Q3 — …", "BOBs"), SapOutcomePath.BobsSiblingLow, "WATCH", "WATCH", "BOBS_SIBLING_LOW", "BOBS-02" },
            new object[] { "A BOBs no siblings", A(Decisions.Allow, null, "BOBs: No sibling colourways found in CY BSR. Auto Allow (new colourway of untracked parent).", "BOBs"), SapOutcomePath.LowHistory, "ALLOW", "ALLOW", "LOW_HISTORY", "WOF-00" },
            new object[] { "A BOBs fallthrough parent Q2", A(Decisions.Allow, "Q2", "BOBs fallthrough: parent style ranks Q2 (60th pct, CY BSR (AW26)) — healthy.", "BOBs"), SapOutcomePath.PeerQ2, "ALLOW", "ALLOW", "PEER_Q2", "PEER-Q2" },
            new object[] { "A BOBs fallthrough Block", A(Decisions.Block, "Q4", "BOBs fallthrough: parent ranks Q4 … Confirmed slow. Do not raise.", "BOBs"), SapOutcomePath.BlockQ4, "HOLD", "BLOCK", "BLOCK", "BLOCK-Q4" },
            // NEW article
            new object[] { "NEW", new Tool1Outcome { Engine = "NEW", Decision = Decisions.AutoAllow, DecisionReason = "Not in Universe Master or CY BSR. New article." }, SapOutcomePath.NewArticle, "ALLOW", "ALLOW", "NEW_ARTICLE", "NEW-00" },
        };

        [Theory]
        [MemberData(nameof(Cases))]
        public void Every_engine_outcome_maps_to_its_SAP_verdict(string name, Tool1Outcome o, SapOutcomePath path, string verdictHold, string verdictBlock, string reasonCode, string ruleId)
        {
            Assert.True(SapVerdictMap.Classify(o) == path, name);
            var rule = SapVerdictMap.Rule(path);
            Assert.Equal(reasonCode, rule.ReasonCode);
            Assert.Equal(ruleId, rule.RuleId);
            Assert.Equal(verdictHold, SapVerdictMap.VerdictFor(rule, "HOLD"));
            Assert.Equal(verdictHold, SapVerdictMap.VerdictFor(rule, null));            // default
            Assert.Equal(verdictBlock, SapVerdictMap.VerdictFor(rule, "BLOCK"));
            Assert.Equal(verdictBlock, SapVerdictMap.VerdictFor(rule, " block "));
        }

        [Fact]
        public void Block_is_HOLD_with_reason_BLOCK_and_switches_to_BLOCK_by_config()
        {
            foreach (var r in SapVerdictMap.Table.Where(r => r.IsBlock))
            {
                Assert.Equal(Decisions.Block, r.EngineDecision);
                Assert.Equal("BLOCK", r.ReasonCode);
                Assert.Equal("HOLD", SapVerdictMap.VerdictFor(r, "HOLD"));
                Assert.Equal("BLOCK", SapVerdictMap.VerdictFor(r, "BLOCK"));
                Assert.Equal("B", SapVerdicts.Code(SapVerdictMap.VerdictFor(r, "BLOCK")));
            }
            Assert.Equal(2, SapVerdictMap.Table.Count(r => r.IsBlock));
            // nothing else changes with the switch
            foreach (var r in SapVerdictMap.Table.Where(r => !r.IsBlock))
                Assert.Equal(SapVerdictMap.VerdictFor(r, "HOLD"), SapVerdictMap.VerdictFor(r, "BLOCK"));
        }

        [Fact]
        public void The_table_has_one_row_per_path_and_only_spec_verdicts()
        {
            Assert.Equal(Enum.GetValues<SapOutcomePath>().OrderBy(x => x), SapVerdictMap.Table.Select(r => r.Path).OrderBy(x => x));
            Assert.All(SapVerdictMap.Table, r => Assert.Contains(r.Verdict, SapVerdicts.SpecValues));
            Assert.All(SapVerdictMap.Table, r => Assert.True(r.ReasonCode.Length <= 40 && r.RuleId.Length <= 30));
        }

        [Fact]
        public void Severity_codes_and_routing_follow_the_spec()
        {
            Assert.Equal(new[] { "A", "R", "W", "H", "B" }, new[] { "ALLOW", "REDUCE", "WATCH", "HOLD", "BLOCK" }.Select(SapVerdicts.Code));
            Assert.Equal("HOLD", SapVerdicts.MostSevere(new[] { "ALLOW", "HOLD", "WATCH", "REDUCE" }));
            Assert.Equal("WATCH", SapVerdicts.MostSevere(new[] { "REDUCE", "WATCH", "ALLOW" }));
            Assert.Equal("REDUCE", SapVerdicts.MostSevere(new[] { "ALLOW", "REDUCE" }));
            Assert.Equal("ALLOW", SapVerdicts.MostSevere(Array.Empty<string>()));
            Assert.Equal("BLOCK", SapVerdicts.MostSevere(new[] { "HOLD", "BLOCK" }));
            Assert.Equal("NONE", SapRoutes.For("ALLOW", false));
            Assert.Equal("BUYER", SapRoutes.For("REDUCE", false));
            Assert.Equal("CATHEAD", SapRoutes.For("WATCH", false));
            Assert.Equal("CATHEAD", SapRoutes.For("HOLD", false));
            Assert.Equal("BMHEAD", SapRoutes.For("HOLD", true));
            Assert.Equal("BMHEAD", SapRoutes.For("BLOCK", true));
            Assert.Equal("CATHEAD", SapRoutes.For("BLOCK", false));
        }

        [Fact]
        public void The_verdict_reason_codes_of_the_table_are_seeded_by_05_sap_interface_sql()
        {
            var sql = File.ReadAllText(RepoPath("database", "sqlserver", "05_sap_interface.sql"));
            var seeded = Regex.Matches(sql, @"\(N'([A-Z0-9_]+)', N'[^']*', N'([A-Z/|]+)', N'([A-Z0-9,\-]+)', \d+\)")
                              .ToDictionary(m => m.Groups[1].Value, m => (Verdict: m.Groups[2].Value, Rules: m.Groups[3].Value.Split(',')));
            var table = SapVerdictMap.Table.GroupBy(r => r.ReasonCode).ToDictionary(g => g.Key, g => g.Select(r => r.RuleId).OrderBy(x => x).ToArray());
            Assert.Equal(table.Keys.OrderBy(x => x), seeded.Keys.OrderBy(x => x));
            foreach (var (code, rules) in table) Assert.Equal(rules, seeded[code].Rules.OrderBy(x => x).ToArray());
            Assert.Equal("HOLD|BLOCK", seeded["BLOCK"].Verdict);
        }

        public static string RepoPath(params string[] parts)
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "POGovernance.sln"))) dir = dir.Parent;
            return Path.Combine(new[] { dir!.FullName }.Concat(parts).ToArray());
        }
    }
}
