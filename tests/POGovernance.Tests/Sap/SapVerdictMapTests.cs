using System.Text.RegularExpressions;
using DecisionEngine.Model;
using DecisionEngine.Results.Tool1;
using DecisionEngine.Sap;
using DecisionEngine.Tool1;

namespace POGovernance.Tests.Sap
{
    /// <summary>
    /// The tool → SAP verdict mapping (handover §8 item 2, owner-approved): every Tool 1 engine outcome maps to its SAP verdict,
    /// reason code and rule id; Block → BLOCK (reason BLOCK) by default and → HOLD with SapInterface:BlockVerdict = HOLD.
    /// </summary>
    public class SapVerdictMapTests
    {
        private static Tool1Outcome B(string decision, double? maxPerm = null, double? windowDemand = 100, string reason = "") => new()
        {
            Engine = "B", OptionType = "—", Decision = decision,
            BFirst = new Tool1EngineBResult { Decision = decision, MaxPermQty = maxPerm, WindowDemand = windowDemand, DecisionReason = reason }
        };

        private static Tool1Outcome BNoPlan() => new()
        {
            Engine = "B", OptionType = "—", Decision = Decisions.Hold,
            BFirst = new Tool1EngineBResult { Decision = Decisions.Hold, NoPlan = true, MaxPermQty = 0, DecisionReason = "No Plan Available: there is no F4 plan for …" }
        };

        private static Tool1Outcome A(string decision, string? quartile, string reason, string optionType = "True Repeat", string? emfCheck = null) => new()
        {
            Engine = "A", OptionType = optionType, Decision = decision,
            A = new Tool1EngineAResult { Decision = decision, Quartile = quartile, DecisionReason = reason, OptionType = optionType, EmfCheck = emfCheck }
        };

        public static IEnumerable<object[]> Cases() => new[]
        {
            // Single Concept (runEngineB)
            new object[] { "B Auto Allow (cover within norm)", B(Decisions.AutoAllow, 500), SapOutcomePath.CoverWithinNorm, "ALLOW", "ALLOW", "COVER_WITHIN_NORM", "COVER-00" },
            // v1.1.6 D1: "No Plan Available" Hold → HOLD NO_PLAN_DATA (was Auto Allow → ALLOW)
            new object[] { "B Hold, No Plan Available", BNoPlan(), SapOutcomePath.NoPlanData, "HOLD", "HOLD", "NO_PLAN_DATA", "PLAN-00" },
            new object[] { "B Watch List (no F3 sale, v1.1.6 D3)", B(Decisions.WatchList, 0, 0, "No sale in F3 for MC CD 1101 MRP 399 (last 45 days), so the colour share cannot be worked out."), SapOutcomePath.MixBelowMin, "WATCH", "WATCH", "MIX_BELOW_MIN", "MIX-01" },
            new object[] { "B Hold, Max Permissible > 0", B(Decisions.Hold, 500), SapOutcomePath.CoverAboveNorm, "REDUCE", "REDUCE", "COVER_ABOVE_NORM", "COVER-01" },
            new object[] { "B Recommend Reduced Qty (README relabel)", B(Decisions.ReduceRecommended, 320), SapOutcomePath.CoverAboveNorm, "REDUCE", "REDUCE", "COVER_ABOVE_NORM", "COVER-01" },
            new object[] { "B Hold, Max Permissible 0", B(Decisions.Hold, 0), SapOutcomePath.CoverNoHeadroom, "HOLD", "HOLD", "COVER_NO_HEADROOM", "COVER-02" },
            new object[] { "B Hold, Max Permissible NaN", B(Decisions.Hold, double.NaN), SapOutcomePath.CoverNoHeadroom, "HOLD", "HOLD", "COVER_NO_HEADROOM", "COVER-02" },
            new object[] { "B Watch List (mix gate)", B(Decisions.WatchList), SapOutcomePath.MixBelowMin, "WATCH", "WATCH", "MIX_BELOW_MIN", "MIX-01" },
            new object[] { "B Cannot Validate", B(Decisions.CannotValidate), SapOutcomePath.CannotValidate, "WATCH", "WATCH", "CANNOT_VALIDATE", "CV-00" },
            // Multi Concept (Tool1EngineA, rule Step 1: Q1 → Allow, Q2–Q4 → Block, no Watch List) — True Repeat
            new object[] { "A Allow top 20%", A(Decisions.Allow, "Top 20%", "True Repeat: 90th pct (Top 20%) … top 20% and at least the Mid minimum 1.5%/day. Allow."), SapOutcomePath.PeerQ1, "ALLOW", "ALLOW", "PEER_Q1", "PEER-Q1" },
            // Step 3: the 20–30% band → HOLD for the B&M Head (exception, MRA Level 8)
            new object[] { "A Exception 20–30%", A(Decisions.Exception, "20–30%", "True Repeat: 75th pct (20–30%) … Exception: raise it to the B&M Head …"), SapOutcomePath.PeerException, "HOLD", "HOLD", "PEER_EXCEPTION", "EXC-L8" },
            new object[] { "A BOBs Exception 20–30%", A(Decisions.Exception, "20–30%", "BOBs: 1 of 2 … are in the 20–30% band …", "BOBs"), SapOutcomePath.PeerException, "HOLD", "HOLD", "PEER_EXCEPTION", "EXC-L8" },
            new object[] { "A Block Q2", A(Decisions.Block, "Q2", "True Repeat: ranks Q2 (60th pct) … Only Q1 (top 25%) is allowed for multi concept — Block. Do not raise PO."), SapOutcomePath.BlockBelowQ1, "HOLD", "BLOCK", "BLOCK", "BLOCK-PEER" },
            new object[] { "A Block Q3", A(Decisions.Block, "Q3", "True Repeat: ranks Q3 (40th pct) … Block. Do not raise PO."), SapOutcomePath.BlockBelowQ1, "HOLD", "BLOCK", "BLOCK", "BLOCK-PEER" },
            new object[] { "A Block Q4", A(Decisions.Block, "Q4", "True Repeat: ranks Q4 (10th pct) … Block. Do not raise PO."), SapOutcomePath.BlockBelowQ1, "HOLD", "BLOCK", "BLOCK", "BLOCK-PEER" },
            new object[] { "A Absolute Block", A(Decisions.Block, null, "Absolute Block override: GM+Colour (…) has AVG PERDAY SELL THRU% = 0.5% < 1%"), SapOutcomePath.BlockAbsolute, "HOLD", "BLOCK", "BLOCK", "BLOCK-ABS" },
            new object[] { "A Absolute Block of a Q1 BOBs", A(Decisions.Block, "Q1", "Absolute Block override: best sibling colourway (…) has AVG PERDAY SELL THRU% = 0.5% < 1%", "BOBs"), SapOutcomePath.BlockAbsolute, "HOLD", "BLOCK", "BLOCK", "BLOCK-ABS" },
            // Step 2: the Entry / Mid / Fashion minimum of a Q1
            new object[] { "A Q1 below the minimum", A(Decisions.Block, "Q1", "True Repeat: ranks Q1 … — Q1 but below the Mid minimum 1.5%/day (first 3 weeks). Block.", emfCheck: "below"), SapOutcomePath.BlockBelowEmfMinimum, "HOLD", "BLOCK", "BLOCK", "BLOCK-EMF-MIN" },
            new object[] { "A Q1 not in the mapping", A(Decisions.Hold, "Q1", "Planner has not maintained this MC-MRP in the master. (…)", emfCheck: "notMapped"), SapOutcomePath.EmfNotMaintained, "HOLD", "HOLD", "EMF_NOT_MAINTAINED", "EMF-00" },
            new object[] { "A BOBs oldest colour below the minimum", A(Decisions.Block, "Q1", "BOBs: All 2 sibling colourway(s) … rank Q1 (top 25%), but the oldest colour …", "BOBs", "below"), SapOutcomePath.BlockBelowEmfMinimum, "HOLD", "BLOCK", "BLOCK", "BLOCK-EMF-MIN" },
            new object[] { "A BOBs not in the mapping", A(Decisions.Hold, "Q1", "Planner has not maintained this MC-MRP in the master. (…)", "BOBs", "notMapped"), SapOutcomePath.EmfNotMaintained, "HOLD", "HOLD", "EMF_NOT_MAINTAINED", "EMF-00" },
            new object[] { "A Allow, WOF below gate", A(Decisions.Allow, null, "WOF = 2w < 4w gate. Insufficient selling history — Auto Allow."), SapOutcomePath.LowHistory, "ALLOW", "ALLOW", "LOW_HISTORY", "WOF-00" },
            new object[] { "A Allow, no peer pool", A(Decisions.Allow, null, "No peer pool available. Auto Allow."), SapOutcomePath.NoPeerPool, "ALLOW", "ALLOW", "NO_PEER_POOL", "PEER-00" },
            // Multi Concept — BOBs: every other colour with WOF ≥ gate must rank Q1
            new object[] { "A BOBs all siblings Q1", A(Decisions.Allow, "Q1", "BOBs: All 2 sibling colourway(s) with WOF ≥ 4w rank Q1 (top 25%) — …", "BOBs"), SapOutcomePath.BobsSiblingsOk, "ALLOW", "ALLOW", "BOBS_SIBLINGS_OK", "BOBS-01" },
            new object[] { "A BOBs a sibling below Q1", A(Decisions.Block, "Q2", "BOBs: 1 of 3 sibling colourway(s) with WOF ≥ 4w rank below Q1 (RED Q2). …", "BOBs"), SapOutcomePath.BlockBobsSibling, "HOLD", "BLOCK", "BLOCK", "BLOCK-BOBS" },
            new object[] { "A BOBs siblings but no peer pool", A(Decisions.Allow, null, "BOBs: 2 sibling colourway(s) with WOF ≥ 4w found, but no peer pool to rank them against. Auto Allow.", "BOBs"), SapOutcomePath.NoPeerPool, "ALLOW", "ALLOW", "NO_PEER_POOL", "PEER-00" },
            new object[] { "A BOBs siblings below the WOF gate", A(Decisions.Allow, null, "BOBs: 2 sibling(s) found but none qualify on WOF ≥ 4w (best WOF=3w). Insufficient history — Auto Allow.", "BOBs"), SapOutcomePath.LowHistory, "ALLOW", "ALLOW", "LOW_HISTORY", "WOF-00" },
            new object[] { "A BOBs no siblings", A(Decisions.Allow, null, "BOBs: No sibling colourways found in CY BSR. Auto Allow (new colourway of untracked parent).", "BOBs"), SapOutcomePath.LowHistory, "ALLOW", "ALLOW", "LOW_HISTORY", "WOF-00" },
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
            Assert.Equal(verdictHold, SapVerdictMap.VerdictFor(rule, " hold "));
            Assert.Equal(verdictBlock, SapVerdictMap.VerdictFor(rule, "BLOCK"));
            Assert.Equal(verdictBlock, SapVerdictMap.VerdictFor(rule, null));           // default
            Assert.Equal(verdictBlock, SapVerdictMap.VerdictFor(rule, new SapDecisionOptions().BlockVerdict));
        }

        [Fact]
        public void Block_is_BLOCK_with_reason_BLOCK_and_switches_to_HOLD_by_config()
        {
            foreach (var r in SapVerdictMap.Table.Where(r => r.IsBlock))
            {
                Assert.Equal(Decisions.Block, r.EngineDecision);
                Assert.Equal("BLOCK", r.ReasonCode);
                Assert.Equal("HOLD", SapVerdictMap.VerdictFor(r, "HOLD"));
                Assert.Equal("BLOCK", SapVerdictMap.VerdictFor(r, "BLOCK"));
                Assert.Equal("B", SapVerdicts.Code(SapVerdictMap.VerdictFor(r, "BLOCK")));
            }
            Assert.Equal(4, SapVerdictMap.Table.Count(r => r.IsBlock));   // below Q1, below the EMF minimum, BOBs sibling below Q1, absolute
            // nothing else changes with the switch
            foreach (var r in SapVerdictMap.Table.Where(r => !r.IsBlock))
                Assert.Equal(SapVerdictMap.VerdictFor(r, "HOLD"), SapVerdictMap.VerdictFor(r, "BLOCK"));
        }

        [Fact]
        public void The_table_has_one_row_per_path_and_only_known_verdicts()
        {
            Assert.Equal(Enum.GetValues<SapOutcomePath>().OrderBy(x => x), SapVerdictMap.Table.Select(r => r.Path).OrderBy(x => x));
            Assert.All(SapVerdictMap.Table, r => Assert.Contains(r.Verdict, SapVerdicts.Values));
            Assert.All(SapVerdictMap.Table, r => Assert.Equal(r.IsBlock, r.Verdict == SapVerdicts.Block));   // BLOCK only for the tool's Block
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
            Assert.Equal("COMMERCIAL", SapRoutes.For("REDUCE", false));
            Assert.Equal("CATHEAD", SapRoutes.For("WATCH", false));
            Assert.Equal("COMMERCIAL", SapRoutes.For("HOLD", false));
            Assert.Equal("BMHEAD", SapRoutes.For("HOLD", true));
            Assert.Equal("BMHEAD", SapRoutes.For("BLOCK", true));
            Assert.Equal("COMMERCIAL", SapRoutes.For("BLOCK", false));
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

        [Fact]
        public void Every_seeded_reason_code_fits_its_columns()
        {
            // SQL Server stops the whole MERGE with "String or binary data would be truncated" (Msg 8152) when one value is too long
            var sql = File.ReadAllText(RepoPath("database", "sqlserver", "05_sap_interface.sql"));
            var create = sql[sql.IndexOf("CREATE TABLE dbo.SAP_REASON_CODES", StringComparison.Ordinal)..];
            create = create[..create.IndexOf("CONSTRAINT PK_SAP_REASON_CODES", StringComparison.Ordinal)];
            int Size(string column) => int.Parse(Regex.Match(create, @"\b" + column + @"\s+NVARCHAR\((\d+)\)").Groups[1].Value);
            var (code, text, verdict, rules) = (Size("Code"), Size("Text"), Size("Verdict"), Size("Rule_Ids"));
            var rows = Regex.Matches(sql, @"\(N'([A-Z0-9_]+)', N'([^']*)', N'([A-Z/|]+)', N'([A-Z0-9,\-]+)', \d+\)");
            Assert.NotEmpty(rows);
            foreach (Match m in rows)
            {
                Assert.True(m.Groups[1].Length <= code, $"{m.Groups[1].Value}: code longer than {code}");
                Assert.True(m.Groups[2].Length <= text, $"{m.Groups[1].Value}: text is {m.Groups[2].Length} characters, the column holds {text}");
                Assert.True(m.Groups[3].Length <= verdict, $"{m.Groups[1].Value}: verdict longer than {verdict}");
                Assert.True(m.Groups[4].Length <= rules, $"{m.Groups[1].Value}: rule ids longer than {rules}");
            }
        }

        public static string RepoPath(params string[] parts)
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "POGovernance.sln"))) dir = dir.Parent;
            return Path.Combine(new[] { dir!.FullName }.Concat(parts).ToArray());
        }
    }
}
