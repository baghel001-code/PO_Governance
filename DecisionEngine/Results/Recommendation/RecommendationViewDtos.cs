using System.Text.Json.Serialization;

namespace DecisionEngine.Results.Recommendation
{
    /// <summary>
    /// Input of the "View Recommendation" screen (PO_RECOMMENDATION_GET, api/v1/RecommendationGet). Either a Generic
    /// Material (resolved as Tool 1 does: Article Master → Universe Master → F2, engine B when the GM
    /// is in Universe Master, else A), or the bucket (Brand / MC / MRP / Flag) with an optional engine.
    /// </summary>
    public sealed class RecommendationRequestDto
    {
        public string? Gm { get; set; }
        public string? Brand { get; set; }
        public string? McCd { get; set; }
        public string? Mrp { get; set; }
        public string? Flag { get; set; }
        /// <summary>Optional header texts when no GM is given.</summary>
        public string? Div { get; set; }
        public string? McDesc { get; set; }
        /// <summary>"A", "B" or "NEW" (the request card's engine). Default: from the GM, else B when Universe Master holds the bucket, else A.</summary>
        public string? Engine { get; set; }
        /// <summary>Selected months ("MM.YYYY"); null/empty = the default selection of _openRec (next 4 months from today).</summary>
        public List<string>? Months { get; set; }
        /// <summary>Dimension override (Style / Fit / Product Material / Design), Multi Concept only.</summary>
        public string? DimOverride { get; set; }
    }

    /// <summary>Bucket info shown in the modal header (the card's <c>_recInfo</c>).</summary>
    public sealed class RecommendationInfoDto
    {
        [JsonPropertyName("mcCd")] public string McCd { get; set; } = "";
        [JsonPropertyName("mrp")] public string Mrp { get; set; } = "";
        [JsonPropertyName("brand")] public string Brand { get; set; } = "";
        [JsonPropertyName("flag")] public string Flag { get; set; } = "";
        [JsonPropertyName("div")] public string Div { get; set; } = "";
        [JsonPropertyName("mcDesc")] public string McDesc { get; set; } = "";
    }

    /// <summary>Month bar of the modal (<c>_buildMonthBar</c>).</summary>
    public sealed class RecommendationMonthsDto
    {
        [JsonPropertyName("engine")] public string Engine { get; set; } = "";
        [JsonPropertyName("info")] public RecommendationInfoDto Info { get; set; } = new();
        /// <summary>Every F4 month of the bucket, chronological (chips).</summary>
        [JsonPropertyName("allMonths")] public List<string> AllMonths { get; set; } = new();
        /// <summary>Default selection: months from today's month on, max 4; else the first 4.</summary>
        [JsonPropertyName("months")] public List<string> Months { get; set; } = new();
        /// <summary>Attribute Relevance Master's dimension for the bucket (null when none).</summary>
        [JsonPropertyName("relevantDim")] public string? RelevantDim { get; set; }
    }

    /// <summary>
    /// Everything the Recommendation modal shows (<c>_openRec</c> → <c>_render</c> → <c>_paintRec</c>),
    /// computed on the server. Exactly one of <see cref="Error"/>, <see cref="B"/>, <see cref="A"/> drives
    /// the body (see docs/ui/recommendation.md).
    /// </summary>
    public sealed class RecommendationViewDto
    {
        /// <summary>The card's engine ("A", "B" or "NEW").</summary>
        [JsonPropertyName("engine")] public string Engine { get; set; } = "";
        /// <summary>
        /// How the body is painted: "B" (colour table) or "A" (attribute + colour tables). An A request
        /// whose dimension is "Colour-only" is painted as B, like _render does.
        /// </summary>
        [JsonPropertyName("renderEngine")] public string RenderEngine { get; set; } = "";
        [JsonPropertyName("info")] public RecommendationInfoDto Info { get; set; } = new();
        /// <summary>"📊 Colour Recommendation — Single Concept MRP" / "📊 Attribute &amp; Colour Recommendation — Multi Concept MRP".</summary>
        [JsonPropertyName("title")] public string Title { get; set; } = "";
        /// <summary>"{div} · {mcDesc} · ₹{mrp} · {flag}".</summary>
        [JsonPropertyName("subtitle")] public string Subtitle { get; set; } = "";
        [JsonPropertyName("allMonths")] public List<string> AllMonths { get; set; } = new();
        /// <summary>Selected months (chronological).</summary>
        [JsonPropertyName("months")] public List<string> Months { get; set; } = new();
        [JsonPropertyName("relevantDim")] public string? RelevantDim { get; set; }
        [JsonPropertyName("dimOverride")] public string? DimOverride { get; set; }
        /// <summary>Options of the dimension drop-down (Multi Concept body only).</summary>
        [JsonPropertyName("dimOptions")] public List<DimOptionDto> DimOptions { get; set; } = new();

        /// <summary>
        /// Blocking error shown instead of the modal (v1.1.4 showed it in an alert):
        /// "Cannot generate recommendation.\n\nMissing files:\n• …".
        /// </summary>
        [JsonPropertyName("error")] public string? Error { get; set; }

        /// <summary>Single Concept body (also for a Colour-only Multi Concept bucket).</summary>
        [JsonPropertyName("b")] public RecBResultDto? B { get; set; }
        /// <summary>Multi Concept body (may carry <c>err</c>).</summary>
        [JsonPropertyName("a")] public RecAResultDto? A { get; set; }

        /// <summary>Texts and figures painted around the tables.</summary>
        [JsonPropertyName("paint")] public RecommendationPaintDto Paint { get; set; } = new();
    }

    /// <summary>The texts / figures <c>_paintRec</c> builds around the tables.</summary>
    public sealed class RecommendationPaintDto
    {
        /// <summary>S.params.T1||75.</summary>
        [JsonPropertyName("t")] public double T { get; set; }
        /// <summary>S.params.strBuffer||0.</summary>
        [JsonPropertyName("buf")] public double Buf { get; set; }
        /// <summary>Unrounded effective threshold — the basis of every STR colour (<c>_strColor(str, eff)</c>).</summary>
        [JsonPropertyName("eff")] public double Eff { get; set; }
        /// <summary>S.params.wof (as shown in the "no Q1/Q2" message).</summary>
        [JsonPropertyName("wof")] public double Wof { get; set; }
        /// <summary>Empty / error message shown instead of the body (".rec-empty"), null when there are rows.</summary>
        [JsonPropertyName("emptyMessage")] public string? EmptyMessage { get; set; }
        /// <summary>True when <see cref="EmptyMessage"/> is an error (red, "⚠ " prefix).</summary>
        [JsonPropertyName("emptyIsError")] public bool EmptyIsError { get; set; }
        /// <summary>Section heading of the Single Concept table / Step 1 / Step 2+3 (plain text).</summary>
        [JsonPropertyName("heading")] public string? Heading { get; set; }
        [JsonPropertyName("step2Heading")] public string? Step2Heading { get; set; }
        /// <summary>Step 2+3 "no eligible Q1/Q2" message (Multi Concept with no recs), else null.</summary>
        [JsonPropertyName("noRecsMessage")] public string? NoRecsMessage { get; set; }
        /// <summary>The blue 💡 summary line (plain text, without the emoji).</summary>
        [JsonPropertyName("summary")] public string? Summary { get; set; }
        /// <summary>Single Concept: colours with a "Raise" month / Multi: P1 count.</summary>
        [JsonPropertyName("raiseCount")] public int RaiseCount { get; set; }
        [JsonPropertyName("p1")] public int P1 { get; set; }
        [JsonPropertyName("p2")] public int P2 { get; set; }
        /// <summary>Sum of rec over every row × selected month.</summary>
        [JsonPropertyName("totalRec")] public double TotalRec { get; set; }
        /// <summary>Multi Concept Step 2+3: per recommendation, its attribute run (rowspan) layout.</summary>
        [JsonPropertyName("recLayout")] public List<RecRowLayoutDto> RecLayout { get; set; } = new();
    }

    /// <summary>
    /// Step 2+3 row layout: Q section header before the row, rowspan of the merged Attribute chip, the
    /// attribute's colour index (tint / accent palette) and whether this row closes its run.
    /// </summary>
    public sealed class RecRowLayoutDto
    {
        /// <summary>Q-section header to draw before this row ("Q1 — Priority 1 (best-quartile peers)"), else null.</summary>
        [JsonPropertyName("sectionHeader")] public string? SectionHeader { get; set; }
        [JsonPropertyName("isFirstInRun")] public bool IsFirstInRun { get; set; }
        /// <summary>Rowspan of the Attribute cell (only meaningful on the first row of a run).</summary>
        [JsonPropertyName("span")] public int Span { get; set; }
        [JsonPropertyName("isLastInRun")] public bool IsLastInRun { get; set; }
        /// <summary>Index of the attribute in <c>attrs</c> (palette index = attrIndex % 5).</summary>
        [JsonPropertyName("attrIndex")] public int AttrIndex { get; set; }
        /// <summary>Text of the colour-trace header line.</summary>
        [JsonPropertyName("traceHeader")] public string TraceHeader { get; set; } = "";
        /// <summary>Text of the qty pop-up header line.</summary>
        [JsonPropertyName("qtyHeader")] public string QtyHeader { get; set; } = "";
    }

    /// <summary>
    /// Colours of the Recommendation screen (<c>_strColor</c> and the Deviation% / palette rules of
    /// <c>_paintRec</c>), shared with the Blazor portal so both sides colour identically.
    /// </summary>
    public static class RecommendationColors
    {
        public const string Green = "#00873E";
        public const string Red = "#DC2626";
        public const string Amber = "#D97706";
        public const string Grey = "#9CA3AF";

        /// <summary>Port of <c>_strColor(str, eff)</c> (index.html line 10926).</summary>
        public static string StrColor(double? str, double eff) => str == null ? Grey : str <= eff ? Green : Red;

        /// <summary>Deviation% colour: &lt; −5 red, &gt; 5 green, else amber.</summary>
        public static string DevColor(double dev) => dev < -5 ? Red : dev > 5 ? Green : Amber;

        public static readonly string[] AttrTint = { "#EAF0FB", "#E6F7F5", "#F3EEFC", "#FCE9F0", "#F1F3F6" };
        public static readonly string[] AttrAccent = { "#1A4FAF", "#0F766E", "#7C3AED", "#BE185D", "#475569" };
        public static readonly string[] AttrText = { "#1A4FAF", "#0F766E", "#6D28D9", "#9D174D", "#334155" };
    }

    // ── Excel export (_exportEngineARecs) ──────────────────────────────────

    /// <summary>The full-detail workbook of the Multi Concept recommendation (5 sheets).</summary>
    public sealed class RecommendationExportDto
    {
        /// <summary>"VMM_EngineA_FullDetail_yyyy-MM-dd.xlsx" (UTC date, like toISOString()).</summary>
        [JsonPropertyName("fileName")] public string FileName { get; set; } = "";
        [JsonPropertyName("sheets")] public List<ExportSheetDto> Sheets { get; set; } = new();
    }

    public sealed class ExportSheetDto
    {
        [JsonPropertyName("name")] public string Name { get; set; } = "";
        /// <summary>Column widths in characters (SheetJS "wch").</summary>
        [JsonPropertyName("widths")] public List<double> Widths { get; set; } = new();
        /// <summary>Rows (first = header). Cells are string, double or null (empty).</summary>
        [JsonPropertyName("rows")] public List<List<object?>> Rows { get; set; } = new();
    }
}
