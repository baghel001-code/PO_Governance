using System.Text.Json.Serialization;
using Domain.Entities.Common;

namespace Application.DTOs.Tool1
{
    // ─────────────────────────────────────────────────────────────────────────────
    // Tool 1 ("✏️ New Request") engine results. JSON names are the exact field names of
    // the objects index.html runEngineB / runEngineA return (and v1.1.4 stored in the
    // request log), so a request saved by this version reads like one saved by the
    // Python tool. Properties marked WhenWritingNull are fields that some JS return
    // paths leave out (undefined); the others are always present (null when JS wrote null).
    // Serialise with Tool1Json.Options (non-finite numbers → null, like JSON.stringify).
    // ─────────────────────────────────────────────────────────────────────────────

    /// <summary>Resolved GM attributes (index.html <c>_idx.resolveMap</c> value / request <c>info</c>).</summary>
    public sealed class Tool1InfoDto
    {
        [JsonPropertyName("brand")] public string Brand { get; set; } = "";
        [JsonPropertyName("div")] public string Div { get; set; } = "";
        [JsonPropertyName("mcCd")] public string McCd { get; set; } = "";
        [JsonPropertyName("mcDesc")] public string McDesc { get; set; } = "";
        [JsonPropertyName("mrp")] public string Mrp { get; set; } = "";
        [JsonPropertyName("flag")] public string Flag { get; set; } = "";
        [JsonPropertyName("season")] public string Season { get; set; } = "";
        [JsonPropertyName("artDesc")] public string ArtDesc { get; set; } = "";
        [JsonPropertyName("vendorName")] public string VendorName { get; set; } = "";
    }

    /// <summary>One month of the Single Concept forward chain (runEngineB <c>chain[]</c>).</summary>
    public sealed class Tool1ChainMonthDto
    {
        [JsonPropertyName("y")] public int Y { get; set; }
        [JsonPropertyName("m")] public int M { get; set; }
        [JsonPropertyName("mkey")] public string MKey { get; set; } = "";
        [JsonPropertyName("label")] public string Label { get; set; } = "";
        [JsonPropertyName("isDM")] public bool IsDM { get; set; }
        [JsonPropertyName("isD1")] public bool IsD1 { get; set; }
        [JsonPropertyName("openStock")] public double OpenStock { get; set; }
        [JsonPropertyName("ePO")] public double EPO { get; set; }
        [JsonPropertyName("proposed")] public double Proposed { get; set; }
        [JsonPropertyName("colPlan")] public double ColPlan { get; set; }
        [JsonPropertyName("isCurrMonth")] public bool IsCurrMonth { get; set; }
        [JsonPropertyName("demandDays")] public double DemandDays { get; set; }
        [JsonPropertyName("totalDays")] public double TotalDays { get; set; }
        [JsonPropertyName("fc")] public double Fc { get; set; }
        [JsonPropertyName("closeStock")] public double CloseStock { get; set; }
        /// <summary>Rolling-window STR (days) from this month's Day 1; null for the delivery month / no demand.</summary>
        [JsonPropertyName("str")] public double? Str { get; set; }
        /// <summary>"OK" / "Breach" / null.</summary>
        [JsonPropertyName("status")] public string? Status { get; set; }
    }

    /// <summary><c>_prevMonthCarry</c> result {qty, cutoff, prevKey, currKey}.</summary>
    public sealed class Tool1CarryDto
    {
        [JsonPropertyName("qty")] public double Qty { get; set; }
        [JsonPropertyName("cutoff")] public double Cutoff { get; set; }
        [JsonPropertyName("prevKey")] public string? PrevKey { get; set; }
        [JsonPropertyName("currKey")] public string CurrKey { get; set; } = "";
    }

    /// <summary>A later chain month above target ({month, str}) — informational only.</summary>
    public sealed class Tool1FutureRiskDto
    {
        [JsonPropertyName("month")] public string Month { get; set; } = "";
        [JsonPropertyName("str")] public double? Str { get; set; }
    }

    /// <summary>Port of the object index.html <c>runEngineB</c> (line 3847) returns — Single Concept.</summary>
    public sealed class Tool1EngineBResult
    {
        [JsonPropertyName("decision")] public string Decision { get; set; } = "";
        [JsonPropertyName("decisionReason")] public string DecisionReason { get; set; } = "";
        [JsonPropertyName("mixPct")] public double MixPct { get; set; }
        [JsonPropertyName("colAvg"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public double? ColAvg { get; set; }
        [JsonPropertyName("mcAvg"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public double? McAvg { get; set; }
        [JsonPropertyName("opStock")] public double OpStock { get; set; }
        [JsonPropertyName("proposedQty"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public double? ProposedQty { get; set; }
        [JsonPropertyName("recQty")] public double? RecQty { get; set; }
        [JsonPropertyName("chain")] public List<Tool1ChainMonthDto> Chain { get; set; } = new();
        [JsonPropertyName("d1Idx"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public int? D1Idx { get; set; }
        [JsonPropertyName("winStockStart"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public double? WinStockStart { get; set; }
        [JsonPropertyName("windowDemand"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public double? WindowDemand { get; set; }
        [JsonPropertyName("windowDailyRate"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public double? WindowDailyRate { get; set; }
        [JsonPropertyName("artSTR")] public double? ArtSTR { get; set; }
        [JsonPropertyName("hasBreach"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public bool? HasBreach { get; set; }
        /// <summary>Max Permissible Qty (floored, ≥ 0). Null (JS undefined) on the Cannot Validate / Watch List paths.</summary>
        [JsonPropertyName("maxPermQty"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public double? MaxPermQty { get; set; }
        [JsonPropertyName("mixFailed")] public bool MixFailed { get; set; }
        [JsonPropertyName("hasDailyF3"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public bool? HasDailyF3 { get; set; }
        /// <summary>F4 forecast for the bucket, month → qty (JS object key order).</summary>
        [JsonPropertyName("forecastByMon"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public Dictionary<string, double>? ForecastByMon { get; set; }
        /// <summary>F2 pending PO + today's approved Tool 1 quantities for this colour, month → qty.</summary>
        [JsonPropertyName("existingPO"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public Dictionary<string, double>? ExistingPO { get; set; }
        [JsonPropertyName("prevMonthCarryNote"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? PrevMonthCarryNote { get; set; }
        [JsonPropertyName("prevMonthCarry"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public Tool1CarryDto? PrevMonthCarry { get; set; }
        [JsonPropertyName("futureRiskMonths"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public List<Tool1FutureRiskDto>? FutureRiskMonths { get; set; }

        /// <summary>
        /// Not a JS field: the engine's stock at the window start before this PO
        /// (<c>stockAtD1NoProp</c> = chain[0].openStock + ePO − colPlan, unclamped), used by the Step 5 formula.
        /// </summary>
        [JsonIgnore] public double? StockAtD1NoProp { get; set; }
        /// <summary>Not a JS field: T × (1 + buffer/100), unrounded.</summary>
        [JsonIgnore] public double? EffectiveT { get; set; }
    }

    /// <summary>One ranked BOBs sibling ({row, q, pdstr}).</summary>
    public sealed class Tool1SiblingRankDto
    {
        [JsonIgnore] public SheetRow Row { get; set; } = new();
        [JsonPropertyName("q")] public string Q { get; set; } = "";
        [JsonPropertyName("pdstr")] public double Pdstr { get; set; }
    }

    /// <summary>
    /// Port of the object index.html <c>runEngineA</c> (line 4270) returns — Multi Concept (True Repeat / BOBs).
    /// The BSR row sets (peerPool, cleanPool, siblings, qualSibs, sibRanked, repSibling) are the fields
    /// shared.js REQ_DROP strips before saving; they are kept here for the result card only.
    /// </summary>
    public sealed class Tool1EngineAResult
    {
        [JsonPropertyName("decision")] public string Decision { get; set; } = "";
        [JsonPropertyName("decisionReason")] public string DecisionReason { get; set; } = "";
        [JsonPropertyName("optionType")] public string OptionType { get; set; } = "";
        [JsonPropertyName("bsrLabel")] public string BsrLabel { get; set; } = "";
        [JsonIgnore] public List<SheetRow> PeerPool { get; set; } = new();
        [JsonIgnore] public List<SheetRow> CleanPool { get; set; } = new();
        [JsonPropertyName("totalBSR")] public double TotalBSR { get; set; }
        [JsonPropertyName("peerAvg")] public double PeerAvg { get; set; }
        [JsonIgnore] public List<SheetRow> Siblings { get; set; } = new();
        [JsonIgnore] public List<SheetRow> QualSibs { get; set; } = new();
        [JsonIgnore] public List<Tool1SiblingRankDto> SibRanked { get; set; } = new();
        [JsonPropertyName("wof")] public double Wof { get; set; }
        [JsonPropertyName("pdstr")] public double? Pdstr { get; set; }
        [JsonPropertyName("quartile_")] public string? Quartile { get; set; }
        [JsonPropertyName("percentile")] public double? Percentile { get; set; }
        [JsonIgnore] public SheetRow? RepSibling { get; set; }
        [JsonPropertyName("gapPct"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public double? GapPct { get; set; }
        [JsonPropertyName("inBorderZone"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public bool? InBorderZone { get; set; }
        [JsonPropertyName("borderTolUsed"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public double? BorderTolUsed { get; set; }
    }
}
