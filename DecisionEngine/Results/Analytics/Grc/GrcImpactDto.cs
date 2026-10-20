namespace DecisionEngine.Results.Analytics.Grc
{
    /// <summary>
    /// Inputs of the GRC Impact screen (the controls of index.html <c>view-analytics</c>). Empty / null
    /// means "All" for the filters and the default for the others.
    /// </summary>
    public sealed class GrcImpactRequest
    {
        /// <summary>"Assess as of" native period (WW.YYYY / MM.YYYY). Default: saved param, else the last period in the file.</summary>
        public string? AsOfPeriod { get; set; }
        /// <summary>"native" (default) or "month" (roll weeks up to months — weekly files only).</summary>
        public string? ViewMode { get; set; }
        /// <summary>"Compare periods before … against that period onward" — a native period. Default: the last one.</summary>
        public string? Cutover { get; set; }
        public string? Division { get; set; }
        public string? CatHead { get; set; }
        public string? Brand { get; set; }
        /// <summary>"Drill into period" — a display period; empty = all combined.</summary>
        public string? DrillPeriod { get; set; }
    }

    /// <summary>
    /// Everything the GRC Impact card shows (port of index.html <c>renderGRCImpact</c>, line 3218). All
    /// figures are pre-computed and pre-formatted on the server (en-IN grouping, toFixed) so the portal
    /// only lays them out. See docs/ui/grc-backtesting.md.
    /// </summary>
    public sealed class GrcImpactDto
    {
        /// <summary>False → show the placeholder ("No GRC file loaded yet") and hide the controls.</summary>
        public bool HasGrcData { get; set; }
        public string PlaceholderTitle { get; set; } = GrcTexts.PlaceholderTitle;
        public string PlaceholderText { get; set; } = GrcTexts.PlaceholderText;
        /// <summary>Set when there is nothing to show ("No GRC data to show." / "… for this filter — try Clear Filters.").</summary>
        public string? Message { get; set; }

        public GrcControlsDto Controls { get; set; } = new();

        /// <summary>"week" / "month" of the displayed periods.</summary>
        public string PeriodType { get; set; } = "month";
        /// <summary>"Week" / "Month" — first-column header of the per-period tables.</summary>
        public string PeriodLabel { get; set; } = "Month";
        public List<string> Periods { get; set; } = new();
        /// <summary>Season selection the ranking used (Current / Fallback peer pools).</summary>
        public List<string> SeasonCurrent { get; set; } = new();
        public List<string> SeasonFallback { get; set; } = new();
        public double WofGate { get; set; }
        public double PeerMin { get; set; }

        /// <summary>Red banner when coverage of the "onward" group is below 50 %.</summary>
        public string? CoverageWarning { get; set; }
        public List<GrcLegendItemDto> Legend { get; set; } = new();
        /// <summary>100 % stacked bar per period (judgeable Q1..Abs. Block only).</summary>
        public List<GrcCompositionBarDto> Composition { get; set; } = new();
        public string QuartileTitle { get; set; } = "";
        public GrcQuartileTableDto QuartileTable { get; set; } = new();
        public string CoverageTitle { get; set; } = "";
        public List<GrcCoverageBarDto> Coverage { get; set; } = new();
        public GrcBreakdownDto Breakdown { get; set; } = new();
        public GrcGroupCardDto BeforeCard { get; set; } = new();
        public GrcGroupCardDto WithCard { get; set; } = new();
        public GrcLedgerDto Ledger { get; set; } = new();
        /// <summary>Blue explanatory box "How weeks are mapped to months" (month roll-up of weekly data only).</summary>
        public bool ShowMonthMappingNote { get; set; }
        public string? MonthMappingNoteTitle { get; set; }
        public List<string> MonthMappingNoteParagraphs { get; set; } = new();
        public string FooterNote { get; set; } = "";
    }

    /// <summary>State of every control after the render (options + the value each select ends on).</summary>
    public sealed class GrcControlsDto
    {
        /// <summary>"Assess as of" options: native periods, chronological.</summary>
        public List<string> AsOfOptions { get; set; } = new();
        public string? AsOfPeriod { get; set; }
        /// <summary>The "View by" select is only shown for weekly GRC files.</summary>
        public bool ViewModeVisible { get; set; }
        public string ViewMode { get; set; } = "native";
        public List<GrcOptionDto> ViewModeOptions { get; set; } = new()
        {
            new() { Value = "native", Label = "Week (native)" },
            new() { Value = "month", Label = "Month (rolled up)" },
        };
        /// <summary>Cutover options: native periods, last selected by default.</summary>
        public List<string> CutoverOptions { get; set; } = new();
        public string? Cutover { get; set; }

        public string BrandAllLabel { get; set; } = "All Brands";
        public List<string> BrandOptions { get; set; } = new();
        public string Brand { get; set; } = "";
        public string DivisionAllLabel { get; set; } = "All Divisions";
        public List<string> DivisionOptions { get; set; } = new();
        public string Division { get; set; } = "";
        public string CatHeadAllLabel { get; set; } = "All Category Heads";
        public List<string> CatHeadOptions { get; set; } = new();
        public string CatHead { get; set; } = "";
        /// <summary>"All weeks combined" / "All months combined".</summary>
        public string DrillAllLabel { get; set; } = "All months combined";
        public List<string> DrillOptions { get; set; } = new();
        public string DrillPeriod { get; set; } = "";
    }

    public sealed class GrcOptionDto
    {
        public string Value { get; set; } = "";
        public string Label { get; set; } = "";
    }

    public sealed class GrcLegendItemDto
    {
        public string Label { get; set; } = "";
        public string Color { get; set; } = "";
    }

    public sealed class GrcCompositionBarDto
    {
        public string Period { get; set; } = "";
        /// <summary>Sum of the five ranked buckets; 0 → no segments drawn (only the period label).</summary>
        public double Total { get; set; }
        /// <summary>Bottom-up: Q1, Q2, Q3, Q4, Abs. Block (empty when Total is 0).</summary>
        public List<GrcCompositionSegmentDto> Segments { get; set; } = new();
    }

    public sealed class GrcCompositionSegmentDto
    {
        public string Bucket { get; set; } = "";
        public string Color { get; set; } = "";
        public double Qty { get; set; }
        /// <summary>qty / total (bar height fraction).</summary>
        public double Share { get; set; }
        public double Pct { get; set; }
        /// <summary>"NN%" drawn inside the segment when Pct &gt; 5, else null.</summary>
        public string? Label { get; set; }
    }

    public sealed class GrcQuartileTableDto
    {
        public string PeriodHeader { get; set; } = "Month";
        /// <summary>Q1, Q2, Q3, Q4, Abs. Block, "Wrong Buy Value (Q4+Abs.Block)".</summary>
        public List<string> Headers { get; set; } = new();
        public List<GrcQuartileRowDto> Rows { get; set; } = new();
        public GrcQuartileRowDto Total { get; set; } = new();
    }

    public sealed class GrcQuartileRowDto
    {
        public string Period { get; set; } = "";
        public List<GrcPctCellDto> Cells { get; set; } = new();
        public double WrongValue { get; set; }
        /// <summary>"₹0.12 Cr".</summary>
        public string WrongValueText { get; set; } = "";
    }

    public sealed class GrcPctCellDto
    {
        public string Bucket { get; set; } = "";
        public double Pct { get; set; }
        /// <summary>"12.3%".</summary>
        public string Text { get; set; } = "";
        public string Color { get; set; } = "";
    }

    public sealed class GrcCoverageBarDto
    {
        public string Period { get; set; } = "";
        /// <summary>Judgeable ÷ Multi Concept qty × 100 — also the bar width in %.</summary>
        public double Pct { get; set; }
        /// <summary>"85%".</summary>
        public string Text { get; set; } = "";
        /// <summary>≥80 green #0E9C8C, ≥40 amber #B4790E, else red #D8483C.</summary>
        public string Color { get; set; } = "";
    }

    public sealed class GrcBreakdownDto
    {
        /// <summary>"Breakdown for 05.2026 · sorted worst-rate-first" / "Breakdown — all weeks combined · sorted worst-rate-first".</summary>
        public string Heading { get; set; } = "";
        /// <summary>By Division, By Category Head, By Brand Type — a table with no rows is left out.</summary>
        public List<GrcBreakdownTableDto> Tables { get; set; } = new();
    }

    public sealed class GrcBreakdownTableDto
    {
        public string Title { get; set; } = "";
        /// <summary>First-column header: "Division", "Category Head" or "Brand Type".</summary>
        public string KeyHeader { get; set; } = "";
        public List<string> Headers { get; set; } = new();
        /// <summary>Sorted worst Wrong Buy % (of Judgeable) first.</summary>
        public List<GrcBreakdownRowDto> Rows { get; set; } = new();
        public GrcBreakdownRowDto Total { get; set; } = new();
    }

    public sealed class GrcBreakdownRowDto
    {
        public string Key { get; set; } = "";
        public double TrueTotal { get; set; }
        public string TrueTotalText { get; set; } = "";
        public double Total { get; set; }
        public string TotalText { get; set; } = "";
        public double Judgeable { get; set; }
        public string JudgeableText { get; set; } = "";
        public double Wrong { get; set; }
        public string WrongText { get; set; } = "";
        public double WrongValue { get; set; }
        public string WrongValueText { get; set; } = "";
        public double Coverage { get; set; }
        public string CoverageText { get; set; } = "";
        /// <summary>Wrong ÷ Judgeable × 100.</summary>
        public double Rate { get; set; }
        public string RateText { get; set; } = "";
        /// <summary>Wrong ÷ Multi Concept qty × 100.</summary>
        public double RateOfMc { get; set; }
        public string RateOfMcText { get; set; } = "";
        /// <summary>Colour of both rate cells: ≥20 #D8483C, ≥10 #B4790E, else #0E9C8C. Null on the Total row (uncoloured).</summary>
        public string? RateColor { get; set; }
    }

    public sealed class GrcGroupCardDto
    {
        /// <summary>"Before 05.2026" / "05.2026 onward".</summary>
        public string Title { get; set; } = "";
        /// <summary>True when there are no periods before the cutover — show <see cref="EmptyText"/> in a grey card.</summary>
        public bool IsEmpty { get; set; }
        public string? EmptyText { get; set; }
        public double TrueTotal { get; set; }
        public string TrueTotalText { get; set; } = "";
        public double Total { get; set; }
        public string TotalText { get; set; } = "";
        public double Wrong { get; set; }
        public double Rate { get; set; }
        public double WrongValue { get; set; }
        public double Coverage { get; set; }
        /// <summary>"Coverage: 85% of buying judgeable".</summary>
        public string CoverageText { get; set; } = "";
        /// <summary>Q1..Abs. Block share of the group's ranked qty.</summary>
        public List<GrcPctCellDto> Buckets { get; set; } = new();
    }

    public sealed class GrcLedgerDto
    {
        public string PeriodHeader { get; set; } = "Month";
        public List<string> Headers { get; set; } = new();
        public List<GrcLedgerRowDto> Rows { get; set; } = new();
        public GrcLedgerRowDto Total { get; set; } = new();
    }

    public sealed class GrcLedgerRowDto
    {
        public string Period { get; set; } = "";
        public double TrueTotal { get; set; }
        public string TrueTotalText { get; set; } = "";
        public double Total { get; set; }
        public string TotalText { get; set; } = "";
        public double Coverage { get; set; }
        public string CoverageText { get; set; } = "";
        /// <summary>≥80 #0E9C8C else #D8483C, bold. Null on the Total row.</summary>
        public string? CoverageColor { get; set; }
        public double Judgeable { get; set; }
        public string JudgeableText { get; set; } = "";
        public double Wrong { get; set; }
        public string WrongText { get; set; } = "";
        public double Rate { get; set; }
        public string RateText { get; set; } = "";
        public double WrongValue { get; set; }
        public string WrongValueText { get; set; } = "";
    }

    /// <summary>Fixed texts of the GRC Back Testing screen (index.html view-analytics).</summary>
    public static class GrcTexts
    {
        public const string PageTitle = "🧪 GRC Back Testing";
        public const string PageSubtitle = "How would today's Multi Concept MRP peer-ranking rules have judged your actual past buying? Upload GRC — Actual Buying History in Data Setup to see coverage, judgeable quantity, and wrong-buying rate, week by week (or rolled up by month).";
        public const string ImpactCardTitle = "🧪 GRC Impact — Multi Concept MRP Backtest";
        public const string ImpactCardBadge = "Beta";
        public const string PlaceholderTitle = "No GRC file loaded yet";
        public const string PlaceholderText = "Upload the GRC — Actual Buying History file in Data Setup to see how today's Multi Concept MRP peer-ranking rules would have judged past buying, month by month.";
        public const string TraceCardTitle = "🔍 Article Trace — Week-by-Week Audit";
        public const string TraceCardText = "Type a GM code to see exactly how the backtest judged it, week by week — which bucket it landed in, its back-calculated WOF at that specific week, and how many peers had themselves matured enough to form a trustworthy pool at that point.";
        public const string TraceInputLabel = "Generic Material (GM) code";
        public const string TraceInputPlaceholder = "e.g. 1110064002";
    }
}
