using DecisionEngine.Common;
using DecisionEngine.Model;
using DecisionEngine.Results.Tool1;
using DecisionEngine.Sap;
using DecisionEngine.Tool1;
using DecisionEngine.Tool2;

namespace POGovernance.Tests.Tool1
{
    /// <summary>
    /// Multi concept rule (articles not in the Universe Master): peers = same Brand, MC, MRP and Flag (Step 1); bands since
    /// Step 3 — top 20% → Allow, 20–30% → Exception, below 30% → Block, no Watch List; BOBs: the worst band of the other colours.
    /// The parity tests check the same rule on the shared fixture against tests/parity/rules/multi-concept.js;
    /// these cases cover what that fixture does not (several brands, a Q1 below 1 %/day, BOBs without a peer pool).
    /// </summary>
    public class MultiConceptStep1Tests
    {
        private static readonly Tool1EngineA Engine = new();

        internal static SheetRow R(params (string K, object? V)[] cells) => new(cells.Select(c => new KeyValuePair<string, object?>(c.K, c.V)));

        /// <summary>BSR row (Brand Type only when given) + its Article Master row (brand, season AW26).</summary>
        internal sealed class Builder
        {
            public readonly List<SheetRow> Bsr = new(), Am = new();
            /// <summary>The Entry / Mid / Fashion mapping (Step 2): MC 100 / MRP 499 / Normal is Mid (1.5 %/day) for both brands.</summary>
            public readonly List<SheetRow> Emf = new()
            {
                R(("Brand Type", "NON-PL"), ("MC CD", "100"), ("Flag", "Normal"), ("MRP", "499"), ("EMF", "Mid")),
                R(("Brand Type", "BELONG"), ("MC CD", "100"), ("Flag", "Normal"), ("MRP", "499"), ("EMF", "Mid"))
            };

            public Builder Add(string gm, string colour, double pdstr, double wof = 8, string brand = "NON-PL", bool brandInBsr = true,
                               string mc = "100", string mrp = "499", string flag = "Normal", string season = "AW26")
            {
                var bsr = new List<(string, object?)> { ("Generic Material", gm), ("Color", colour), ("MC CD", mc), ("MRP", mrp), ("Article Type", flag),
                                                        ("WEEK ON FLOOR", wof), ("AVG PERDAY SELL THRU%", pdstr) };
                if (brandInBsr) bsr.Add(("Brand Type", brand));
                Bsr.Add(R(bsr.ToArray()));
                if (!Am.Any(r => r.Col("Generic Material") == gm))
                    Am.Add(R(("Generic Material", gm), ("Brand Type", brand), ("MC CD", mc), ("MRP", mrp), ("Flag", flag), ("Season", season)));
                return this;
            }

            /// <summary>Eight NON-PL peers selling 1 … 8 %/day.</summary>
            public Builder Peers(string brand = "NON-PL", double start = 0.01, double step = 0.01, bool brandInBsr = true, string prefix = "P")
            {
                for (var i = 0; i < 8; i++) Add(prefix + brand + i, "BLK", start + step * i, brand: brand, brandInBsr: brandInBsr);
                return this;
            }

            public EngineData Data() => new(new Dictionary<string, IReadOnlyList<SheetRow>?>
            {
                [DatasetKeys.Bsr] = Bsr, [DatasetKeys.ArtMaster] = Am, [DatasetKeys.Um] = new List<SheetRow>(), [DatasetKeys.Emf] = Emf
            }, new EngineParams { Wof = 4, Peer = 5, SeasonCurrent = new() { "AW26" }, SeasonFallback = new() }, 1, 1);
        }

        internal static Tool1EngineAResult Run(EngineData d, string gm, string colour)
        {
            var (engine, option) = ResolveIndex.RouteRequest(d, gm, colour);
            Assert.Equal("A", engine);
            var g = ResolveIndex.ResolveGm(d, gm)!;
            var info = new Tool1InfoDto { Brand = g.Brand, McCd = g.McCd, Mrp = g.Mrp, Flag = g.Flag, Season = g.Season };
            return Engine.Run(d, gm, colour, option!, info);
        }

        internal static SapOutcomePath Sap(Tool1EngineAResult a) =>
            SapVerdictMap.Classify(new Tool1Outcome { Engine = "A", OptionType = a.OptionType, A = a, Decision = a.Decision, DecisionReason = a.DecisionReason });

        // ── True Repeat ─────────────────────────────────────────────────────

        [Fact]
        public void The_top_20_percent_is_allowed()
        {
            var d = new Builder().Peers().Add("T1", "BLU", 0.09).Data();
            var a = Run(d, "T1", "BLU");
            Assert.Equal("Top 20%", a.Quartile);
            Assert.Equal(Decisions.Allow, a.Decision);
            Assert.Equal("True Repeat: ranks in the top 20% (100 percentile of 9 styles, Brand + MC + MRP + Flag, CY BSR (AW26)). Sells 9.00%/day — meets the Mid minimum of 1.50%/day. Allow.", a.DecisionReason);
            Assert.Equal(SapOutcomePath.PeerQ1, Sap(a));
        }

        [Theory]
        [InlineData(0.055)]   // 6 of 9 at or below → 67th pct
        [InlineData(0.035)]
        [InlineData(0.015)]
        public void Below_the_top_30_percent_is_blocked_and_never_a_Watch_List(double pdstr)
        {
            var d = new Builder().Peers().Add("T1", "BLU", pdstr).Data();
            var a = Run(d, "T1", "BLU");
            Assert.Equal("Below 30%", a.Quartile);
            Assert.Equal(Decisions.Block, a.Decision);
            Assert.StartsWith("True Repeat: ranks below the top 30% (", a.DecisionReason);
            Assert.EndsWith("%/day). Block. Do not raise PO.", a.DecisionReason);
            Assert.Null(a.InBorderZone);            // no border zone any more
            Assert.Null(a.BorderTolUsed);
            Assert.Equal(SapOutcomePath.BlockBelowQ1, Sap(a));
            Assert.Equal(Decisions.Cancel, Tool2Engine.MapA(a.Decision));
        }

        [Fact]
        public void Peers_are_the_same_brand_from_the_BSR_or_the_Article_Master()
        {
            // eight BELONG options sell far better (20 … 27 %/day): with them, T1 (9 %/day) would rank below the top 30%.
            // Four have Brand Type in the BSR, four only in the Article Master — both are left out of a NON-PL article's peers.
            var b = new Builder().Peers().Add("T1", "BLU", 0.09);
            for (var i = 0; i < 8; i++) b.Add("B" + i, "RED", 0.20 + 0.01 * i, brand: "BELONG", brandInBsr: i % 2 == 0);
            var a = Run(b.Data(), "T1", "BLU");
            Assert.Equal(9, a.PeerPool.Count);
            Assert.All(a.PeerPool, r => Assert.Equal("NON-PL", Tool1EngineA.PeerBrand(r, ResolveIndex.ResolveMap(b.Data()))));
            Assert.Equal("Top 20%", a.Quartile);
            Assert.Equal(Decisions.Allow, a.Decision);

            // the BELONG article is ranked among BELONG options only
            var belong = Run(b.Data(), "B7", "RED");
            Assert.Equal(8, belong.PeerPool.Count);
            Assert.Equal("Top 20%", belong.Quartile);
        }

        [Fact]
        public void Brand_match_ignores_case_and_a_peer_without_a_known_brand_is_kept()
        {
            var b = new Builder().Peers().Add("T1", "BLU", 0.09);
            b.Bsr.Add(R(("Generic Material", "NOBRAND"), ("Color", "BLK"), ("MC CD", "100"), ("MRP", "499"), ("Article Type", "Normal"),
                        ("WEEK ON FLOOR", 8), ("AVG PERDAY SELL THRU%", 0.5), ("Brand Type", "non-pl")));
            b.Am.Add(R(("Generic Material", "NOBRAND"), ("Season", "AW26")));
            foreach (var (gm, pd) in new[] { ("UNK1", 0.6), ("UNK2", 0.7) })   // no brand in the BSR nor in the Article Master
            {
                b.Bsr.Add(R(("Generic Material", gm), ("Color", "BLK"), ("MC CD", "100"), ("MRP", "499"), ("Article Type", "Normal"),
                            ("WEEK ON FLOOR", 8), ("AVG PERDAY SELL THRU%", pd)));
                b.Am.Add(R(("Generic Material", gm), ("Season", "AW26")));
            }
            var a = Run(b.Data(), "T1", "BLU");
            Assert.Equal(12, a.PeerPool.Count);      // the lower-case "non-pl" peer and the two without a brand count
            Assert.Equal("20–30%", a.Quartile);      // 9 of 12 at or below = 75th pct (top 20% without them)
            Assert.Equal(Decisions.Exception, a.Decision);
        }

        [Fact]
        public void The_top_20_percent_below_1_percent_per_day_is_still_an_absolute_block()
        {
            var d = new Builder().Peers(start: 0.001, step: 0.001).Add("T1", "BLU", 0.009).Data();
            var a = Run(d, "T1", "BLU");
            Assert.Equal("Top 20%", a.Quartile);
            Assert.Equal(Decisions.Block, a.Decision);
            Assert.StartsWith("Absolute Block override", a.DecisionReason);
            Assert.Equal(SapOutcomePath.BlockAbsolute, Sap(a));
        }

        [Fact]
        public void Less_than_4_weeks_on_floor_and_no_peer_pool_are_allowed_as_before()
        {
            var d = new Builder().Peers().Add("T1", "BLU", 0.001, wof: 3).Add("LONE", "BLU", 0.02, mc: "999", season: "SS25").Data();   // not a current season: no peers at all
            var young = Run(d, "T1", "BLU");
            Assert.Equal(Decisions.Allow, young.Decision);
            Assert.Equal(SapOutcomePath.LowHistory, Sap(young));
            var lone = Run(d, "LONE", "BLU");
            Assert.Equal(Decisions.Allow, lone.Decision);
            Assert.Equal("No peer pool available. Auto Allow.", lone.DecisionReason);
        }

        // ── BOBs (new colour of an existing option) ─────────────────────────

        [Fact]
        public void BOBs_is_allowed_when_every_other_colour_is_in_the_top_20_percent()
        {
            var d = new Builder().Peers().Add("T1", "BLU", 0.085, wof: 6).Add("T1", "RED", 0.09, wof: 8).Add("T1", "GRN", 0.02, wof: 2).Data();
            var a = Run(d, "T1", "NEW");                       // GRN has < 4 weeks on floor: not ranked
            Assert.Equal("BOBs", a.OptionType);
            Assert.Equal(2, a.QualSibs.Count);
            Assert.All(a.SibRanked, s => Assert.Equal("Top 20%", s.Q));
            Assert.Equal(Decisions.Allow, a.Decision);
            Assert.StartsWith("BOBs: All 2 other colour(s) with WOF ≥ 4w rank in the top 20% (best: ", a.DecisionReason);
            Assert.EndsWith("). Oldest colour RED (8w) sells 9.00%/day — meets the Mid minimum of 1.50%/day. New colour permitted.", a.DecisionReason);
            Assert.Equal("oldest other colour RED (8w)", a.EmfOn);
            Assert.Equal(SapOutcomePath.BobsSiblingsOk, Sap(a));
        }

        [Fact]
        public void BOBs_is_blocked_when_another_colour_is_below_the_top_30_percent()
        {
            var d = new Builder().Peers().Add("T1", "BLU", 0.09, wof: 6).Add("T1", "RED", 0.055, wof: 8).Data();
            var a = Run(d, "T1", "NEW");
            Assert.Equal(Decisions.Block, a.Decision);
            Assert.StartsWith("BOBs: other colour RED (8w) ranks below the top 30% (", a.DecisionReason);
            Assert.EndsWith("). Every other colour must be in the top 20% (Allow band) — new colour blocked.", a.DecisionReason);
            Assert.StartsWith("Worst other colour: RED (8w) at ", a.RankNote);
            Assert.Equal("Below 30%", a.Quartile);             // the worst-ranked colour drives the decision
            Assert.Equal(SapOutcomePath.BlockBobsSibling, Sap(a));
        }

        [Fact]
        public void BOBs_without_colours_on_floor_4_weeks_or_without_peers_is_allowed()
        {
            var d = new Builder().Peers().Add("T1", "BLU", 0.001, wof: 3).Add("LONE", "BLU", 0.02, wof: 6, mc: "999", season: "SS25").Data();
            var young = Run(d, "T1", "NEW");
            Assert.Equal(Decisions.Allow, young.Decision);
            Assert.Contains("none qualify on WOF ≥ 4w", young.DecisionReason);
            Assert.Equal(SapOutcomePath.LowHistory, Sap(young));

            var lone = Run(d, "LONE", "NEW");
            Assert.Equal(Decisions.Allow, lone.Decision);
            Assert.Equal("BOBs: 1 sibling colourway(s) with WOF ≥ 4w found, but no peer pool to rank them against. Auto Allow.", lone.DecisionReason);
            Assert.Equal(SapOutcomePath.NoPeerPool, Sap(lone));
        }

        // ── Tool 2 uses the same engine (with the brand) ───────────────────

        [Fact]
        public void Tool2_ranks_with_the_brand_too()
        {
            var b = new Builder().Peers().Add("T1", "BLU", 0.09);
            for (var i = 0; i < 8; i++) b.Add("B" + i, "RED", 0.20 + 0.01 * i, brand: "BELONG");
            var d = b.Data();
            var line = Tool2EngineA.Run(d, "T1", "BLU", "True Repeat", ResolveIndex.ResolveGm(d, "T1"));
            Assert.Equal("Top 20%", line.Quartile);
            Assert.Equal(Decisions.Retain, Tool2Engine.MapA(line.Decision));
        }
    }
}
