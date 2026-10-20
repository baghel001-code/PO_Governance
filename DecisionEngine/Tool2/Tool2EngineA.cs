using DecisionEngine.Common;
using DecisionEngine.Model;
using DecisionEngine.Results.Tool1;
using DecisionEngine.Tool1;

namespace DecisionEngine.Tool2
{
    /// <summary>Raw result of runEngineA (before Tool 2 maps the decision).</summary>
    public sealed class EngineAResult
    {
        public string Decision { get; set; } = "";
        public string DecisionReason { get; set; } = "";
        public string? OptionType { get; set; }
        public string BsrLabel { get; set; } = "";
        public List<SheetRow> PeerPool { get; set; } = new();
        public List<SheetRow> CleanPool { get; set; } = new();
        public double TotalBsr { get; set; }
        public double PeerAvg { get; set; }
        public List<SheetRow> Siblings { get; set; } = new();
        public List<SheetRow> QualSibs { get; set; } = new();
        public List<SibRank> SibRanked { get; set; } = new();
        public double? Wof { get; set; }
        public double? Pdstr { get; set; }
        public string? Quartile { get; set; }
        public double? Percentile { get; set; }
        public double? GapPct { get; set; }
        public bool? InBorderZone { get; set; }
        public double? BorderTolUsed { get; set; }
        public SheetRow? RepSibling { get; set; }
    }

    /// <summary>
    /// index.html <c>runEngineA</c> (line 4270) and <c>absBlock</c> (line 4257) for Tool 2 — the Multi Concept
    /// MRP peer-ranking decision (True Repeat / BOBs) that Tool 2 maps to Retain / Cancel / Watch List.
    /// Tool 1 and Tool 2 call the same JS function, so there is ONE implementation: <see cref="Tool1EngineA"/>.
    /// This adapter only converts Tool 2's inputs (<see cref="GmInfo"/>, nullable option type) and maps the
    /// result to Tool 2's line model (<see cref="EngineAResult"/> / <see cref="SibRank"/>). Every return path
    /// of runEngineA sets <c>wof</c>, so <see cref="EngineAResult.Wof"/> is always filled, as before.
    /// </summary>
    public static class Tool2EngineA
    {
        private static readonly Tool1EngineA Engine = new();

        /// <summary>Port of <c>runEngineA(gm, colour, optionType, info)</c> — runs <see cref="Tool1EngineA.Run"/>.</summary>
        public static EngineAResult Run(EngineData data, string gm, string colour, string? optionType, GmInfo? info)
        {
            // runEngineA reads info.mcCd / info.mrp / info.flag only.
            var t1Info = info == null ? null : new Tool1InfoDto { McCd = info.McCd, Mrp = info.Mrp, Flag = info.Flag };
            var r = Engine.Run(data, gm, colour, optionType ?? "", t1Info);
            return new EngineAResult
            {
                Decision = r.Decision, DecisionReason = r.DecisionReason, OptionType = optionType, BsrLabel = r.BsrLabel,
                PeerPool = r.PeerPool, CleanPool = r.CleanPool, TotalBsr = r.TotalBSR, PeerAvg = r.PeerAvg,
                Siblings = r.Siblings, QualSibs = r.QualSibs,
                SibRanked = r.SibRanked.Select(s => new SibRank { Row = s.Row, Q = s.Q, Pdstr = s.Pdstr }).ToList(),
                Wof = r.Wof, Pdstr = r.Pdstr, Quartile = r.Quartile, Percentile = r.Percentile,
                GapPct = r.GapPct, InBorderZone = r.InBorderZone, BorderTolUsed = r.BorderTolUsed, RepSibling = r.RepSibling
            };
        }
    }
}
