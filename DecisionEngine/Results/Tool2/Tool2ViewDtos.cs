using System.Text.Json.Serialization;

namespace DecisionEngine.Results.Tool2
{
    /// <summary>A KPI tile of the strip (<c>renderAssessmentDashboard</c>).</summary>
    public sealed class Tool2KpiDto
    {
        [JsonPropertyName("label")] public string Label { get; set; } = "";
        /// <summary>Display value ("87", "23,505", "₹81.14L", "—", "43%").</summary>
        [JsonPropertyName("value")] public string Value { get; set; } = "";
        [JsonPropertyName("number")] public double Number { get; set; }
        [JsonPropertyName("color")] public string Color { get; set; } = "";
        [JsonPropertyName("bg")] public string Bg { get; set; } = "";
        /// <summary>The decision this tile filters on (null for the totals).</summary>
        [JsonPropertyName("decision")] public string? Decision { get; set; }
    }

    /// <summary>A node of the Brand → Division → MC → MRP tree (<c>renderBrandSummary</c> / <c>_treeRow</c>).</summary>
    public sealed class Tool2TreeNodeDto
    {
        /// <summary>Brand / division / MC text; MRP level is "Rs.{mrp}".</summary>
        [JsonPropertyName("label")] public string Label { get; set; } = "";
        /// <summary>The raw key (for MRP nodes without the "Rs." prefix).</summary>
        [JsonPropertyName("key")] public string Key { get; set; } = "";
        /// <summary>0 Brand, 1 Division, 2 MC, 3 MRP.</summary>
        [JsonPropertyName("level")] public int Level { get; set; }
        /// <summary>"Brand Type", "Division", "MC", "MRP" (grey suffix).</summary>
        [JsonPropertyName("depth")] public string Depth { get; set; } = "";
        [JsonPropertyName("total")] public int Total { get; set; }
        [JsonPropertyName("retain")] public int Retain { get; set; }
        [JsonPropertyName("reduce")] public int Reduce { get; set; }
        [JsonPropertyName("cancel")] public int Cancel { get; set; }
        [JsonPropertyName("watch")] public int Watch { get; set; }
        /// <summary>% Action = round((reduce+cancel+watch)/total×100).</summary>
        [JsonPropertyName("pct")] public double Pct { get; set; }
        /// <summary>&gt;50 red, &gt;20 amber, else green (white on the brand row).</summary>
        [JsonPropertyName("pctColor")] public string PctColor { get; set; } = "";
        /// <summary>Sum of released qty of Reduce / Cancel / Watch lines.</summary>
        [JsonPropertyName("atRisk")] public double AtRisk { get; set; }
        /// <summary>fmt(atRisk) or "—".</summary>
        [JsonPropertyName("atRiskText")] public string AtRiskText { get; set; } = "";
        [JsonPropertyName("children")] public List<Tool2TreeNodeDto> Children { get; set; } = new();
        /// <summary>MRP level: the lines (idx) sorted by released qty, high → low.</summary>
        [JsonPropertyName("rows")] public List<int>? Rows { get; set; }
    }

    /// <summary>A stacked bar of the "By Division" / "By Delivery Month" charts.</summary>
    public sealed class Tool2ChartBarDto
    {
        [JsonPropertyName("label")] public string Label { get; set; } = "";
        /// <summary>Right-hand text: total lines (division) or "{n} lines · {qty} at risk" (month).</summary>
        [JsonPropertyName("note")] public string Note { get; set; } = "";
        [JsonPropertyName("retain")] public int Retain { get; set; }
        [JsonPropertyName("reduce")] public int Reduce { get; set; }
        [JsonPropertyName("cancel")] public int Cancel { get; set; }
        [JsonPropertyName("watch")] public int Watch { get; set; }
        /// <summary>Segment widths in % of the largest bar: Retain #00873E, Reduce #D97706, Cancel #DC2626, Watch #FBBF24.</summary>
        [JsonPropertyName("widths")] public List<double> Widths { get; set; } = new();
    }

    /// <summary>A row of the Engine Split card.</summary>
    public sealed class Tool2EngineSplitDto
    {
        [JsonPropertyName("label")] public string Label { get; set; } = "";
        [JsonPropertyName("count")] public int Count { get; set; }
        /// <summary>Bar width in % of all lines.</summary>
        [JsonPropertyName("width")] public double Width { get; set; }
        [JsonPropertyName("color")] public string Color { get; set; } = "";
        [JsonPropertyName("bg")] public string Bg { get; set; } = "";
    }

    /// <summary>A row of the "By Vendor" table.</summary>
    public sealed class Tool2VendorRowDto
    {
        [JsonPropertyName("vendor")] public string Vendor { get; set; } = "";
        [JsonPropertyName("total")] public int Total { get; set; }
        [JsonPropertyName("retain")] public int Retain { get; set; }
        [JsonPropertyName("reduce")] public int Reduce { get; set; }
        [JsonPropertyName("cancel")] public int Cancel { get; set; }
        [JsonPropertyName("watch")] public int Watch { get; set; }
        [JsonPropertyName("atRisk")] public double AtRisk { get; set; }
        [JsonPropertyName("valueAtRisk")] public double ValueAtRisk { get; set; }
        [JsonPropertyName("atRiskText")] public string AtRiskText { get; set; } = "";
        [JsonPropertyName("valueText")] public string ValueText { get; set; } = "";
        /// <summary>The action-required lines (idx) of the click-expand breakdown; empty = row not expandable.</summary>
        [JsonPropertyName("rows")] public List<int> Rows { get; set; } = new();
    }

    /// <summary>A row of the "Division × MC — Excess Concentration" table.</summary>
    public sealed class Tool2DivMcRowDto
    {
        [JsonPropertyName("brand")] public string Brand { get; set; } = "";
        [JsonPropertyName("div")] public string Div { get; set; } = "";
        [JsonPropertyName("mc")] public string Mc { get; set; } = "";
        [JsonPropertyName("lines")] public int Lines { get; set; }
        [JsonPropertyName("reduce")] public int Reduce { get; set; }
        [JsonPropertyName("cancel")] public int Cancel { get; set; }
        [JsonPropertyName("watch")] public int Watch { get; set; }
        [JsonPropertyName("atRisk")] public double AtRisk { get; set; }
        [JsonPropertyName("valueAtRisk")] public double ValueAtRisk { get; set; }
        [JsonPropertyName("atRiskText")] public string AtRiskText { get; set; } = "";
        [JsonPropertyName("valueText")] public string ValueText { get; set; } = "";
        [JsonPropertyName("pctOfTotal")] public double PctOfTotal { get; set; }
        /// <summary>"12.3%".</summary>
        [JsonPropertyName("pctText")] public string PctText { get; set; } = "";
        [JsonPropertyName("rows")] public List<int> Rows { get; set; } = new();
    }

    /// <summary>One line of a rollup row's breakdown table (<c>buildRollupDetailTable</c>).</summary>
    public sealed class Tool2RollupLineDto
    {
        [JsonPropertyName("idx")] public int Idx { get; set; }
        [JsonPropertyName("gm")] public string Gm { get; set; } = "";
        [JsonPropertyName("colour")] public string Colour { get; set; } = "";
        /// <summary>"₹399" or "—".</summary>
        [JsonPropertyName("mrpText")] public string MrpText { get; set; } = "";
        [JsonPropertyName("poNum")] public string PoNum { get; set; } = "";
        [JsonPropertyName("released")] public string Released { get; set; } = "";
        /// <summary>fmt(cut) or "—"; pink bold when &gt; 0.</summary>
        [JsonPropertyName("cutQty")] public string CutQty { get; set; } = "";
        [JsonPropertyName("cutPositive")] public bool CutPositive { get; set; }
        [JsonPropertyName("decision")] public string Decision { get; set; } = "";
        [JsonPropertyName("decisionColor")] public string DecisionColor { get; set; } = "";
    }

    /// <summary>The two rollup tables for a filter (<c>renderRollupTables</c>).</summary>
    public sealed class Tool2RollupDto
    {
        [JsonPropertyName("vendors")] public List<Tool2VendorRowDto> Vendors { get; set; } = new();
        [JsonPropertyName("divMc")] public List<Tool2DivMcRowDto> DivMc { get; set; } = new();
        /// <summary>Breakdown rows of every line referenced above, by idx.</summary>
        [JsonPropertyName("lines")] public Dictionary<int, Tool2RollupLineDto> Lines { get; set; } = new();
    }

    /// <summary>Filter of the rollup tables (values exactly as the drop-downs hold them: MRP with "₹").</summary>
    public sealed class Tool2RollupFilterDto
    {
        [JsonPropertyName("div")] public string? Div { get; set; }
        [JsonPropertyName("mc")] public string? Mc { get; set; }
        [JsonPropertyName("mrp")] public string? Mrp { get; set; }
        [JsonPropertyName("mon")] public string? Mon { get; set; }
        [JsonPropertyName("dec")] public string? Dec { get; set; }
    }

    /// <summary>Filter of the detail view ("Action Required — Detailed List").</summary>
    public sealed class Tool2DetailFilterDto
    {
        /// <summary>"B" (Single Concept, default) or "A" (Multi Concept).</summary>
        [JsonPropertyName("tab")] public string Tab { get; set; } = "B";
        [JsonPropertyName("brand")] public string? Brand { get; set; }
        [JsonPropertyName("div")] public string? Div { get; set; }
        [JsonPropertyName("mc")] public string? Mc { get; set; }
        /// <summary>"₹399" (as in the drop-down).</summary>
        [JsonPropertyName("mrp")] public string? Mrp { get; set; }
        [JsonPropertyName("mon")] public string? Mon { get; set; }
        /// <summary>Decision chip: "Cancel", "Reduce Qty", "Watch List", "Retain" or ""/null (All).</summary>
        [JsonPropertyName("dec")] public string? Dec { get; set; }
    }

    /// <summary>PO-wise FIFO allocation of a Single Concept colour group.</summary>
    public sealed class Tool2PoActionDto
    {
        [JsonPropertyName("poNum")] public string PoNum { get; set; } = "";
        [JsonPropertyName("gm")] public string Gm { get; set; } = "";
        [JsonPropertyName("relQty")] public double RelQty { get; set; }
        /// <summary>"Retain", "Cancel" or "Reduce Qty".</summary>
        [JsonPropertyName("action")] public string Action { get; set; } = "";
        [JsonPropertyName("recQty")] public double RecQty { get; set; }
        [JsonPropertyName("actionColor")] public string ActionColor { get; set; } = "";
    }

    /// <summary>
    /// A Single Concept colour × delivery-month group (<c>buildEngineB_Groups</c>) with the texts of its
    /// table row and expand panel (<c>filterAssTable</c>, tab B).
    /// </summary>
    public sealed class Tool2ColourGroupDto
    {
        [JsonPropertyName("key")] public string Key { get; set; } = "";
        [JsonPropertyName("gm")] public string Gm { get; set; } = "";
        [JsonPropertyName("colour")] public string Colour { get; set; } = "";
        [JsonPropertyName("dm")] public string Dm { get; set; } = "";
        [JsonPropertyName("div")] public string Div { get; set; } = "";
        [JsonPropertyName("mcDesc")] public string McDesc { get; set; } = "";
        [JsonPropertyName("mrp")] public string Mrp { get; set; } = "";
        [JsonPropertyName("brand")] public string Brand { get; set; } = "";
        [JsonPropertyName("flag")] public string Flag { get; set; } = "";
        [JsonPropertyName("mcCd")] public string McCd { get; set; } = "";
        [JsonPropertyName("artSTR")] public double? ArtStr { get; set; }
        [JsonPropertyName("mixPct")] public double MixPct { get; set; }
        [JsonPropertyName("opStock")] public double OpStock { get; set; }
        [JsonPropertyName("windowDailyRate")] public double? WindowDailyRate { get; set; }
        [JsonPropertyName("winStockStart")] public double? WinStockStart { get; set; }
        [JsonPropertyName("rowDecision")] public string RowDecision { get; set; } = "";
        [JsonPropertyName("rowDecisionReason")] public string RowDecisionReason { get; set; } = "";
        [JsonPropertyName("totalQty")] public double TotalQty { get; set; }
        [JsonPropertyName("maxPermissible")] public double MaxPermissible { get; set; }
        [JsonPropertyName("excessUnits")] public double ExcessUnits { get; set; }
        /// <summary>"Retain", "Reduce Qty", "Cancel All", "Watch List", "Cannot Validate".</summary>
        [JsonPropertyName("colourDecision")] public string ColourDecision { get; set; } = "";
        [JsonPropertyName("poCount")] public int PoCount { get; set; }
        [JsonPropertyName("poActions")] public List<Tool2PoActionDto> PoActions { get; set; } = new();
        // display
        [JsonPropertyName("decisionColor")] public string DecisionColor { get; set; } = "";
        [JsonPropertyName("decisionBg")] public string DecisionBg { get; set; } = "";
        [JsonPropertyName("strText")] public string StrText { get; set; } = "";
        [JsonPropertyName("strColor")] public string StrColor { get; set; } = "";
        [JsonPropertyName("mixText")] public string MixText { get; set; } = "";
        /// <summary>Mix% ≥ the mix gate → green, else amber.</summary>
        [JsonPropertyName("mixOk")] public bool MixOk { get; set; }
        [JsonPropertyName("totalQtyText")] public string TotalQtyText { get; set; } = "";
        /// <summary>fmt(maxPermissible) or "—" (table cell).</summary>
        [JsonPropertyName("maxPermText")] public string MaxPermText { get; set; } = "";
        /// <summary>Panel heading "{decision} — Colour STR Computation &amp; PO Allocation".</summary>
        [JsonPropertyName("panelTitle")] public string PanelTitle { get; set; } = "";
        /// <summary>"Why no STR was computed: " box text (Cannot Validate / Watch List / no forecast), else null.</summary>
        [JsonPropertyName("whyNoStr")] public string? WhyNoStr { get; set; }
        [JsonPropertyName("kvs")] public List<KvDto> Kvs { get; set; } = new();
        /// <summary>Monospace formula box lines (only when an STR exists).</summary>
        [JsonPropertyName("formula")] public List<List<TextSegDto>>? Formula { get; set; }
        [JsonPropertyName("totalRetain")] public double TotalRetain { get; set; }
    }

    /// <summary>Month separator of the Single Concept table.</summary>
    public sealed class Tool2MonthSectionDto
    {
        [JsonPropertyName("month")] public string Month { get; set; } = "";
        /// <summary>"3 colour groups".</summary>
        [JsonPropertyName("groupsText")] public string GroupsText { get; set; } = "";
        [JsonPropertyName("excess")] public double Excess { get; set; }
        [JsonPropertyName("totalQty")] public double TotalQty { get; set; }
        /// <summary>"Excess: 1,124 units".</summary>
        [JsonPropertyName("excessText")] public string ExcessText { get; set; } = "";
        /// <summary>"Total PO Qty: 1,124".</summary>
        [JsonPropertyName("totalQtyText")] public string TotalQtyText { get; set; } = "";
        [JsonPropertyName("groups")] public List<Tool2ColourGroupDto> Groups { get; set; } = new();
    }

    public sealed class Tool2HeaderColDto
    {
        public Tool2HeaderColDto() { }
        public Tool2HeaderColDto(string label, string align, string? extra = null) { Label = label; Align = align; MinWidth = extra; }
        [JsonPropertyName("label")] public string Label { get; set; } = "";
        /// <summary>"left", "right", "center".</summary>
        [JsonPropertyName("align")] public string Align { get; set; } = "";
        [JsonPropertyName("minWidth")] public string? MinWidth { get; set; }
    }

    /// <summary>The detail view for one tab + filter (<c>setEngineTab</c> → <c>renderDetailHeader</c> + <c>filterAssTable</c>).</summary>
    public sealed class Tool2DetailViewDto
    {
        [JsonPropertyName("tab")] public string Tab { get; set; } = "B";
        [JsonPropertyName("header")] public List<Tool2HeaderColDto> Header { get; set; } = new();
        /// <summary>Context banner: bold title + text.</summary>
        [JsonPropertyName("bannerTitle")] public string BannerTitle { get; set; } = "";
        [JsonPropertyName("bannerText")] public string BannerText { get; set; } = "";
        [JsonPropertyName("bannerColor")] public string BannerColor { get; set; } = "";
        [JsonPropertyName("bannerBg")] public string BannerBg { get; set; } = "";
        /// <summary>"8 colour groups · Single Concept MRP only (filtered)" / "23 rows · Multi Concept MRP only".</summary>
        [JsonPropertyName("countText")] public string CountText { get; set; } = "";
        /// <summary>True → show "✓ No action required for the selected filters".</summary>
        [JsonPropertyName("empty")] public bool Empty { get; set; }
        /// <summary>Tab B: month sections with colour groups.</summary>
        [JsonPropertyName("months")] public List<Tool2MonthSectionDto>? Months { get; set; }
        /// <summary>Tab A: the lines (idx) in display order (released qty high → low).</summary>
        [JsonPropertyName("rows")] public List<int>? Rows { get; set; }
    }

    /// <summary>Option lists of the drop-downs (<c>populateDetailFilters</c> / <c>populateRollupFilters</c>).</summary>
    public sealed class Tool2FilterOptionsDto
    {
        [JsonPropertyName("brands")] public List<string> Brands { get; set; } = new();
        [JsonPropertyName("divisions")] public List<string> Divisions { get; set; } = new();
        [JsonPropertyName("mcs")] public List<string> Mcs { get; set; } = new();
        /// <summary>"₹399" …, numeric order.</summary>
        [JsonPropertyName("mrps")] public List<string> Mrps { get; set; } = new();
        [JsonPropertyName("months")] public List<string> Months { get; set; } = new();
        /// <summary>Rollup decision drop-down: "Reduce Qty", "Cancel", "Watch List".</summary>
        [JsonPropertyName("rollupDecisions")] public List<string> RollupDecisions { get; set; } = new() { "Reduce Qty", "Cancel", "Watch List" };
    }

    /// <summary>Parameters the run used (S.params at run time).</summary>
    public sealed class Tool2ParamsDto
    {
        [JsonPropertyName("T2")] public double T2 { get; set; }
        [JsonPropertyName("D")] public double D { get; set; }
        [JsonPropertyName("strBuffer")] public double StrBuffer { get; set; }
        /// <summary>Rounded T2 × (1+buffer/100).</summary>
        [JsonPropertyName("effT2")] public double EffT2 { get; set; }
        [JsonPropertyName("mix")] public double Mix { get; set; }
        [JsonPropertyName("borderTolerance")] public double BorderTolerance { get; set; }
        [JsonPropertyName("wof")] public double Wof { get; set; }
        [JsonPropertyName("peer")] public double Peer { get; set; }
        [JsonPropertyName("seasonCurrent")] public List<string> SeasonCurrent { get; set; } = new();
        [JsonPropertyName("seasonFallback")] public List<string> SeasonFallback { get; set; } = new();
    }

    /// <summary>The whole "📋 Raised PO Assessment" dashboard of one run (live or reopened).</summary>
    public sealed class Tool2ViewDto
    {
        [JsonPropertyName("runId")] public string? RunId { get; set; }
        [JsonPropertyName("runNo")] public string? RunNo { get; set; }
        /// <summary>Run time / user (saved runs).</summary>
        [JsonPropertyName("at")] public string? At { get; set; }
        [JsonPropertyName("by")] public string? By { get; set; }
        /// <summary>True when re-opened from history (show the "📜 Viewing saved run" banner).</summary>
        [JsonPropertyName("isHistory")] public bool IsHistory { get; set; }
        /// <summary>Data files of the run that are not available any more (history banner).</summary>
        [JsonPropertyName("missingFiles")] public List<string> MissingFiles { get; set; } = new();
        /// <summary>Sidebar status: "✓ 87 lines assessed" / "📜 Saved run — 87 lines".</summary>
        [JsonPropertyName("statusText")] public string StatusText { get; set; } = "";
        /// <summary>Toast after a run: "Assessment complete — 87 lines processed".</summary>
        [JsonPropertyName("toast")] public string? Toast { get; set; }
        /// <summary>Set when the run finished but could not be saved to History.</summary>
        [JsonPropertyName("saveError")] public string? SaveError { get; set; }
        [JsonPropertyName("params")] public Tool2ParamsDto Params { get; set; } = new();
        /// <summary>Server date the view was built for (decides which delivery months are "past").</summary>
        [JsonPropertyName("today")] public string Today { get; set; } = "";
        [JsonPropertyName("lines")] public List<Tool2LineDto> Lines { get; set; } = new();
        [JsonPropertyName("kpis")] public List<Tool2KpiDto> Kpis { get; set; } = new();
        [JsonPropertyName("tree")] public List<Tool2TreeNodeDto> Tree { get; set; } = new();
        [JsonPropertyName("divisionChart")] public List<Tool2ChartBarDto> DivisionChart { get; set; } = new();
        [JsonPropertyName("monthChart")] public List<Tool2ChartBarDto> MonthChart { get; set; } = new();
        [JsonPropertyName("engineSplit")] public List<Tool2EngineSplitDto> EngineSplit { get; set; } = new();
        /// <summary>Vendor + Division × MC tables, unfiltered.</summary>
        [JsonPropertyName("rollup")] public Tool2RollupDto Rollup { get; set; } = new();
        /// <summary>Detail view drop-downs.</summary>
        [JsonPropertyName("detailFilters")] public Tool2FilterOptionsDto DetailFilters { get; set; } = new();
        /// <summary>Rollup drop-downs.</summary>
        [JsonPropertyName("rollupFilters")] public Tool2FilterOptionsDto RollupFilters { get; set; } = new();
        /// <summary>Default detail view (tab B, no filter).</summary>
        [JsonPropertyName("detail")] public Tool2DetailViewDto Detail { get; set; } = new();
        /// <summary>Every Single Concept colour × month group (unfiltered) — the detail view filters these.</summary>
        [JsonPropertyName("colourGroups")] public List<Tool2ColourGroupDto> ColourGroups { get; set; } = new();
        /// <summary>Names of the Category Head export panel ("Unassigned" last).</summary>
        [JsonPropertyName("catHeads")] public List<string> CatHeads { get; set; } = new();
    }

    /// <summary>Response of POST api/v1/assessment/run.</summary>
    public sealed class Tool2RunResultDto
    {
        [JsonPropertyName("runId")] public string? RunId { get; set; }
        [JsonPropertyName("runNo")] public string? RunNo { get; set; }
        [JsonPropertyName("view")] public Tool2ViewDto View { get; set; } = new();
    }

    /// <summary>GET api/v1/assessment/options.</summary>
    public sealed class Tool2OptionsDto
    {
        /// <summary>Current parameters (or the run's when a run id is given).</summary>
        [JsonPropertyName("params")] public Tool2ParamsDto Params { get; set; } = new();
        /// <summary>F2 lines that would be assessed now (0 = Run Assessment would fail).</summary>
        [JsonPropertyName("assessableLines")] public int AssessableLines { get; set; }
        [JsonPropertyName("hasF2")] public bool HasF2 { get; set; }
        [JsonPropertyName("catHeads")] public List<string> CatHeads { get; set; } = new();
        [JsonPropertyName("detailFilters")] public Tool2FilterOptionsDto? DetailFilters { get; set; }
        [JsonPropertyName("rollupFilters")] public Tool2FilterOptionsDto? RollupFilters { get; set; }
    }
}
