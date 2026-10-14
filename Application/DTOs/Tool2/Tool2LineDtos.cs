using System.Text.Json.Serialization;
using Application.DTOs.Recommendation;

namespace Application.DTOs.Tool2
{
    // ─────────────────────────────────────────────────────────────────────────────
    // Tool 2 ("📋 PO Assessment") line-level DTOs. Raw fields keep the JS batchResults
    // names (index.html runAssessment); display fields carry the exact texts / colours
    // the v1.1.4 screen built (buildDecisionTooltip, buildDetailedReason, decColor …).
    // See docs/ui/tool2.md.
    // ─────────────────────────────────────────────────────────────────────────────

    /// <summary>A piece of text; <see cref="Bold"/> = it was wrapped in &lt;strong&gt;.</summary>
    public sealed class TextSegDto
    {
        public TextSegDto() { }
        public TextSegDto(string text, bool bold = false, string? color = null) { Text = text; Bold = bold; Color = color; }
        [JsonPropertyName("text")] public string Text { get; set; } = "";
        [JsonPropertyName("bold")] public bool Bold { get; set; }
        /// <summary>Optional text colour (e.g. the STR / Max Permissible figures of the formula box).</summary>
        [JsonPropertyName("color")] public string? Color { get; set; }
    }

    /// <summary>A small "label / value" chip (<c>_kv(k, v)</c>).</summary>
    public sealed class KvDto
    {
        public KvDto() { }
        public KvDto(string label, string value) { Label = label; Value = value; }
        [JsonPropertyName("label")] public string Label { get; set; } = "";
        [JsonPropertyName("value")] public string Value { get; set; } = "";
    }

    /// <summary>A Step tile of the Multi Concept detailed computation (value over label, coloured value).</summary>
    public sealed class TileDto
    {
        public TileDto() { }
        public TileDto(string label, string value, string color) { Label = label; Value = value; Color = color; }
        [JsonPropertyName("label")] public string Label { get; set; } = "";
        [JsonPropertyName("value")] public string Value { get; set; } = "";
        [JsonPropertyName("color")] public string Color { get; set; } = "";
    }

    /// <summary>GM resolve info (index.html <c>_idx.resolveMap</c> entry).</summary>
    public sealed class Tool2InfoDto
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

    /// <summary>One month of the Single Concept rolling chain (runEngineB2Fast).</summary>
    public sealed class Tool2ChainStepDto
    {
        [JsonPropertyName("y")] public int Y { get; set; }
        [JsonPropertyName("m")] public int M { get; set; }
        [JsonPropertyName("mkey")] public string MKey { get; set; } = "";
        [JsonPropertyName("openStock")] public double OpenStock { get; set; }
        [JsonPropertyName("ePO")] public double EPO { get; set; }
        [JsonPropertyName("colPlan")] public double ColPlan { get; set; }
        [JsonPropertyName("isCurrMonth")] public bool IsCurrMonth { get; set; }
        [JsonPropertyName("demandDays")] public double DemandDays { get; set; }
        [JsonPropertyName("totalDays")] public double TotalDays { get; set; }
        [JsonPropertyName("fc")] public double Fc { get; set; }
        [JsonPropertyName("closeStock")] public double CloseStock { get; set; }
        [JsonPropertyName("str")] public double? Str { get; set; }
        /// <summary>"OK" / "Breach" / null.</summary>
        [JsonPropertyName("status")] public string? Status { get; set; }
    }

    /// <summary>Head × Type × Sub bucket of the Excel export (<c>_bucketFor</c>).</summary>
    public sealed class Tool2BucketDto
    {
        /// <summary>"WATCHLIST" or "REDUCE/CANCEL".</summary>
        [JsonPropertyName("type")] public string Type { get; set; } = "";
        /// <summary>"BoBs", "True Repeat", "Single Concept", "Multi Concept".</summary>
        [JsonPropertyName("sub")] public string Sub { get; set; } = "";
        /// <summary>"WL-BoBs", "WL-TR", "WL-SC", "RC-SC", "RC-MC".</summary>
        [JsonPropertyName("code")] public string Code { get; set; } = "";
    }

    /// <summary>
    /// The expandable "Detailed Computation" of a line (<c>buildDetailedReason(r)</c>, index.html line 6201),
    /// in display order: KV chips, "Why this action:" sentence, STR caution box, then either the
    /// Single Concept "STR Computation Steps" or the Multi Concept Step 1–4 cards.
    /// </summary>
    public sealed class Tool2ReasonDto
    {
        /// <summary>Header of the expander: "{decision} - Detailed Computation" (tree) / "{decision} — Detailed Computation" (table).</summary>
        [JsonPropertyName("kvs")] public List<KvDto> Kvs { get; set; } = new();
        /// <summary>The sentence after "Why this action: " (bold parts flagged).</summary>
        [JsonPropertyName("why")] public List<TextSegDto> Why { get; set; } = new();
        /// <summary>Amber "⚠ Caution — STR" box (<c>_cautionBoxHtml</c>), null when none.</summary>
        [JsonPropertyName("caution")] public StrCautionDto? Caution { get; set; }
        /// <summary>Single Concept with an STR: the 4 lines under "STR Computation Steps:".</summary>
        [JsonPropertyName("strSteps")] public List<List<TextSegDto>>? StrSteps { get; set; }
        /// <summary>Multi Concept with a quartile: Step 1–4 cards.</summary>
        [JsonPropertyName("steps")] public Tool2ReasonStepsDto? Steps { get; set; }
    }

    /// <summary>Step 1–4 of a ranked Multi Concept line.</summary>
    public sealed class Tool2ReasonStepsDto
    {
        /// <summary>Colour of the marker / article figures: red (Cancel/Block), amber (Watch List), green.</summary>
        [JsonPropertyName("decisionColor")] public string DecisionColor { get; set; } = "";
        /// <summary>"Step 1 — Article &amp; Peer Pool": 5 tiles.</summary>
        [JsonPropertyName("step1")] public List<TileDto> Step1 { get; set; } = new();
        /// <summary>"Step 2 — PDSTR vs Peer Average": 4 tiles (null when no PDSTR).</summary>
        [JsonPropertyName("step2")] public List<TileDto>? Step2 { get; set; }
        /// <summary>"Step 3 — Quartile Ranking (…)" heading; null when no percentile.</summary>
        [JsonPropertyName("step3Heading")] public string? Step3Heading { get; set; }
        /// <summary>Marker position on the Q4|Q3|Q2|Q1 bar in % (percentile clamped to 2–98).</summary>
        [JsonPropertyName("step3MarkerPos")] public double? Step3MarkerPos { get; set; }
        /// <summary>Step 4 border-zone check (Q4/Q3 below the peer average only).</summary>
        [JsonPropertyName("step4")] public Tool2BorderZoneDto? Step4 { get; set; }
        /// <summary>"⚡ Absolute Block Override: …" note, else null.</summary>
        [JsonPropertyName("absBlockNote")] public string? AbsBlockNote { get; set; }
    }

    public sealed class Tool2BorderZoneDto
    {
        /// <summary>Monospace box lines: "Threshold (Q4 &amp; Q3) : gap ≤ 10%", "This article gap : 12.3%".</summary>
        [JsonPropertyName("threshold")] public string Threshold { get; set; } = "";
        [JsonPropertyName("gap")] public string Gap { get; set; } = "";
        [JsonPropertyName("inZone")] public bool InZone { get; set; }
        /// <summary>"✓ IN BORDER ZONE" / "✗ OUTSIDE BORDER ZONE".</summary>
        [JsonPropertyName("result")] public string Result { get; set; } = "";
        /// <summary>The coloured verdict bar text.</summary>
        [JsonPropertyName("message")] public string Message { get; set; } = "";
        [JsonPropertyName("color")] public string Color { get; set; } = "";
        [JsonPropertyName("bg")] public string Bg { get; set; } = "";
    }

    /// <summary>One assessed F2 line with everything the screen, tooltips, expanders and exports show.</summary>
    public sealed class Tool2LineDto
    {
        // ── raw (batchResults) ──
        [JsonPropertyName("idx")] public int Idx { get; set; }
        [JsonPropertyName("gm")] public string Gm { get; set; } = "";
        [JsonPropertyName("colour")] public string Colour { get; set; } = "";
        [JsonPropertyName("dm")] public string Dm { get; set; } = "";
        [JsonPropertyName("relQty")] public double RelQty { get; set; }
        [JsonPropertyName("poNum")] public string PoNum { get; set; } = "";
        /// <summary>"A" (Multi Concept), "B" (Single Concept), "NEW", "?" (row error).</summary>
        [JsonPropertyName("engine")] public string Engine { get; set; } = "";
        [JsonPropertyName("decision")] public string Decision { get; set; } = "";
        [JsonPropertyName("decisionReason")] public string DecisionReason { get; set; } = "";
        [JsonPropertyName("strCaution")] public StrCautionDto? StrCaution { get; set; }
        [JsonPropertyName("artSTR")] public double? ArtStr { get; set; }
        /// <summary>Max Permissible / retain qty (recQty||maxRetainQty||0).</summary>
        [JsonPropertyName("recQty")] public double RecQty { get; set; }
        /// <summary>Cut qty for Reduce Qty / Cancel, else 0.</summary>
        [JsonPropertyName("excessQty")] public double ExcessQty { get; set; }
        [JsonPropertyName("mixPct")] public double MixPct { get; set; }
        [JsonPropertyName("opStock")] public double OpStock { get; set; }
        [JsonPropertyName("div")] public string Div { get; set; } = "";
        [JsonPropertyName("mcDesc")] public string McDesc { get; set; } = "";
        [JsonPropertyName("mrp")] public string Mrp { get; set; } = "";
        [JsonPropertyName("vendor")] public string Vendor { get; set; } = "";
        [JsonPropertyName("info")] public Tool2InfoDto? Info { get; set; }
        [JsonPropertyName("poValue")] public double? PoValue { get; set; }
        [JsonPropertyName("poRate")] public double? PoRate { get; set; }
        [JsonPropertyName("optionType")] public string? OptionType { get; set; }
        [JsonPropertyName("quartile_")] public string? Quartile { get; set; }
        [JsonPropertyName("percentile")] public double? Percentile { get; set; }
        [JsonPropertyName("pdstr")] public double? Pdstr { get; set; }
        [JsonPropertyName("peerAvg")] public double? PeerAvg { get; set; }
        [JsonPropertyName("gapPct")] public double? GapPct { get; set; }
        [JsonPropertyName("inBorderZone")] public bool InBorderZone { get; set; }
        [JsonPropertyName("borderTolUsed")] public double? BorderTolUsed { get; set; }
        [JsonPropertyName("wof")] public double? Wof { get; set; }
        [JsonPropertyName("totalBSR")] public double? TotalBsr { get; set; }
        [JsonPropertyName("bsrLabel")] public string? BsrLabel { get; set; }
        /// <summary>Sizes of the BSR row sets (the rows themselves stay in the saved run).</summary>
        [JsonPropertyName("peerPoolSize")] public int? PeerPoolSize { get; set; }
        [JsonPropertyName("cleanPoolSize")] public int? CleanPoolSize { get; set; }
        [JsonPropertyName("siblingsCount")] public int? SiblingsCount { get; set; }
        [JsonPropertyName("qualSibsCount")] public int? QualSibsCount { get; set; }
        [JsonPropertyName("windowDailyRate")] public double? WindowDailyRate { get; set; }
        [JsonPropertyName("winStockStart")] public double? WinStockStart { get; set; }
        [JsonPropertyName("chain")] public List<Tool2ChainStepDto>? Chain { get; set; }

        // ── display ──
        /// <summary>info.brand or "NON-PL".</summary>
        [JsonPropertyName("brand")] public string Brand { get; set; } = "";
        [JsonPropertyName("flag")] public string Flag { get; set; } = "";
        [JsonPropertyName("mcCd")] public string McCd { get; set; } = "";
        /// <summary>"Single Concept MRP" / "Multi Concept MRP" (CSV / Excel "Engine").</summary>
        [JsonPropertyName("engineLabel")] public string EngineLabel { get; set; } = "";
        /// <summary>Decision pill colours (<c>decColor</c> / <c>decBg</c>).</summary>
        [JsonPropertyName("decisionColor")] public string DecisionColor { get; set; } = "";
        [JsonPropertyName("decisionBg")] public string DecisionBg { get; set; } = "";
        /// <summary>"52d" or "—".</summary>
        [JsonPropertyName("strText")] public string StrText { get; set; } = "";
        /// <summary>STR above T2 × (1+buffer) → shown red.</summary>
        [JsonPropertyName("strBreach")] public bool StrBreach { get; set; }
        /// <summary>Hover text of the decision pill (<c>buildDecisionTooltip</c>).</summary>
        [JsonPropertyName("tooltip")] public string Tooltip { get; set; } = "";
        [JsonPropertyName("reason")] public Tool2ReasonDto Reason { get; set; } = new();
        /// <summary>Category Head display name ("Unassigned" without a mapping).</summary>
        [JsonPropertyName("catHead")] public string CatHead { get; set; } = "";
        [JsonPropertyName("catHeadEmail")] public string? CatHeadEmail { get; set; }
        [JsonPropertyName("bucket")] public Tool2BucketDto? Bucket { get; set; }
        /// <summary>"Recommended Action (Engine A)" (<c>_recommendedActionA</c>), "" for others.</summary>
        [JsonPropertyName("recommendedActionA")] public string RecommendedActionA { get; set; } = "";
        /// <summary>Up to 4 ranked options (<c>_rankedOptionsAny(r,4)</c>) — CSV "Option 1–4".</summary>
        [JsonPropertyName("rankedOptions")] public List<string> RankedOptions { get; set; } = new();
        /// <summary>Reduce/Cancel sheet options (<c>_reduceCancelOptions(r,3)</c>).</summary>
        [JsonPropertyName("rcOptions")] public List<string> RcOptions { get; set; } = new();
        /// <summary>Reduce/Cancel sheet "Recommendation" (<c>_reduceCancelRecommendation</c>).</summary>
        [JsonPropertyName("rcRecommendation")] public string RcRecommendation { get; set; } = "";
        /// <summary>"🚫 Cancel" / "✅ Acceptable (95% retained)" / "⚠️ Action Needed (40% retained)" / "—".</summary>
        [JsonPropertyName("poStatus")] public string PoStatus { get; set; } = "";
        /// <summary>Driver bucket of the reason (<c>_aiDriverBucketT2</c>, Summary sheet / AI context).</summary>
        [JsonPropertyName("driver")] public string Driver { get; set; } = "";
    }
}
