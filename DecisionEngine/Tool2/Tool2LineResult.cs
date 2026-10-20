using DecisionEngine.Common;
using DecisionEngine.Model;
using DecisionEngine.Results.Recommendation;

namespace DecisionEngine.Tool2
{
    /// <summary>One month of the Tool 2 Single Concept rolling chain (runEngineB2Fast <c>chain[]</c>).</summary>
    public sealed class Tool2ChainStep
    {
        public int Y { get; set; }
        public int M { get; set; }
        public string MKey { get; set; } = "";
        public double OpenStock { get; set; }
        public double EPO { get; set; }
        public double ColPlan { get; set; }
        public bool IsCurrMonth { get; set; }
        public double DemandDays { get; set; }
        public double TotalDays { get; set; }
        public double Fc { get; set; }
        public double CloseStock { get; set; }
        public double? Str { get; set; }
        public string? Status { get; set; }
    }

    /// <summary>One <c>sibRanked</c> entry of runEngineA (BOBs): {row, q, pdstr}.</summary>
    public sealed class SibRank
    {
        public SheetRow Row { get; set; } = new();
        public string Q { get; set; } = "";
        public double Pdstr { get; set; }
    }

    /// <summary>
    /// One assessed F2 line — the object index.html <c>runAssessment</c> / <c>processChunk</c> pushes
    /// into <c>batchResults</c> (line ~5480), field for field. This is what a saved run stores
    /// (see <see cref="Tool2RunPayload"/>) and what every view / export of the run is built from.
    /// JS <c>null</c>/<c>undefined</c> are null here.
    /// </summary>
    public sealed class Tool2LineResult
    {
        public int Idx { get; set; }
        public string Gm { get; set; } = "";
        public string Colour { get; set; } = "";
        public string Dm { get; set; } = "";
        public double RelQty { get; set; }
        public string PoNum { get; set; } = "—";
        /// <summary>"A", "B", "NEW" or "?" (row error).</summary>
        public string Engine { get; set; } = "";
        public string Decision { get; set; } = "";
        public string DecisionReason { get; set; } = "";
        /// <summary>The STR caution of a Multi Concept Q1/Q2 Retain (JS <c>''</c> when none → null).</summary>
        public StrCautionDto? StrCaution { get; set; }
        public double? ArtStr { get; set; }
        public double RecQty { get; set; }
        public double ExcessQty { get; set; }
        public double MixPct { get; set; }
        public double OpStock { get; set; }
        public string Div { get; set; } = "—";
        public string McDesc { get; set; } = "—";
        public string Mrp { get; set; } = "—";
        public string Vendor { get; set; } = "—";
        public GmInfo? Info { get; set; }
        public double? PoValue { get; set; }
        public double? PoRate { get; set; }
        // Multi Concept MRP fields
        public string? OptionType { get; set; }
        public string? Quartile { get; set; }
        public double? Percentile { get; set; }
        public double? Pdstr { get; set; }
        public double? PeerAvg { get; set; }
        public double? GapPct { get; set; }
        public bool InBorderZone { get; set; }
        public double? BorderTolUsed { get; set; }
        public double? Wof { get; set; }
        public double? TotalBsr { get; set; }
        public List<SheetRow>? PeerPool { get; set; }
        public List<SheetRow>? CleanPool { get; set; }
        public string? BsrLabel { get; set; }
        public List<SheetRow>? Siblings { get; set; }
        public List<SheetRow>? QualSibs { get; set; }
        public List<SibRank>? SibRanked { get; set; }
        // Single Concept MRP fields
        public double? WindowDailyRate { get; set; }
        public double? WinStockStart { get; set; }
        public List<Tool2ChainStep>? Chain { get; set; }
        /// <summary>True for the reduced object of the per-row catch block (fewer fields).</summary>
        public bool IsErrorRow { get; set; }

        /// <summary><c>(r.info&amp;&amp;r.info.brand)?r.info.brand:'NON-PL'</c>.</summary>
        public string BrandOrNonPl => Info != null && Info.Brand.Length > 0 ? Info.Brand : "NON-PL";

        /// <summary><c>r.excessQty!=null?r.excessQty:r.relQty</c> — the cut quantity.</summary>
        public double CutQty => ExcessQty;
    }
}
