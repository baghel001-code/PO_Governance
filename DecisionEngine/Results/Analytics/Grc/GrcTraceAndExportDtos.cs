namespace DecisionEngine.Results.Analytics.Grc
{
    /// <summary>
    /// Article Trace — Week-by-Week Audit (port of index.html <c>_traceArticleHistory</c> line 3108 +
    /// <c>renderArticleTrace</c> line 3161).
    /// </summary>
    public sealed class GrcArticleTraceDto
    {
        /// <summary>The GM box was empty — the browser just clears the result area.</summary>
        public bool IsBlank { get; set; }
        /// <summary>Red error box text (no GRC, GM not resolvable, GM not in the GRC file).</summary>
        public string? Error { get; set; }
        /// <summary>"Assess as of" period the WOF back-calculation used (null = today's date).</summary>
        public string? AsOfPeriod { get; set; }

        public string Gm { get; set; } = "";
        public string Brand { get; set; } = "";
        public string Division { get; set; } = "";
        public string McCd { get; set; } = "";
        public string McDesc { get; set; } = "";
        public string Mrp { get; set; } = "";
        public string Flag { get; set; } = "";
        /// <summary>Flag or "—".</summary>
        public string FlagText { get; set; } = "";
        public bool IsSingleConcept { get; set; }
        /// <summary>"⚠ Single Concept — out of scope for quartile ranking" (amber) when single concept.</summary>
        public string? SingleConceptNote { get; set; }
        /// <summary>Chronological (by period) GRC lines of this GM.</summary>
        public List<GrcTraceWeekRowDto> WeekRows { get; set; } = new();
    }

    public sealed class GrcTraceWeekRowDto
    {
        public string Period { get; set; } = "";
        public string Colour { get; set; } = "";
        public double Qty { get; set; }
        /// <summary>en-IN rounded qty.</summary>
        public string QtyText { get; set; } = "";
        public double? Value { get; set; }
        public string Bucket { get; set; } = "";
        /// <summary>Pill colour (text colour; background is the same colour at ~13 % alpha, i.e. "#RRGGBB22").</summary>
        public string BucketColor { get; set; } = "";
        /// <summary>Full sentence explaining the bucket.</summary>
        public string Detail { get; set; } = "";
        /// <summary>"Reasoning" column (plain text; the numbers are shown bold in the browser).</summary>
        public string Extra { get; set; } = "";
        public double? WeeksElapsed { get; set; }
        public double? EstimatedWof { get; set; }
        public double? WofNow { get; set; }
        public double? WofGate { get; set; }
        public double? PoolSize { get; set; }
        public double? PeerMin { get; set; }
        public double? Pdstr { get; set; }
        public double? Wof { get; set; }
    }

    /// <summary>
    /// "⬇ Export Row Detail" (port of index.html <c>exportGRCRowDetail</c> line 3571): one row per GRC
    /// line. Cells are strings or numbers (double), exactly as the browser wrote them to the sheet.
    /// </summary>
    public sealed class GrcRowDetailExportDto
    {
        public const string Sheet = "GRC Row Detail";
        public static readonly string[] Columns =
        {
            "Period", "Brand", "Generic Material", "Colour", "Article Desc", "GRC Qty", "GRC Value (₹)",
            "Resolved Division", "Resolved MC CD", "Resolved MRP", "Resolved Flag", "Resolved Category Head",
            "In Universe Master (Single Concept)?", "BSR Match Found?", "PDSTR %/day", "Current WOF (weeks)",
            "Bucket", "Included in Judgeable?", "Included in Wrong Buying?"
        };

        /// <summary>GRC_Row_Detail_yyyy-MM-dd.xlsx (UTC date).</summary>
        public string FileName { get; set; } = "";
        public string SheetName { get; set; } = Sheet;
        public List<string> Headers { get; set; } = Columns.ToList();
        public List<List<object?>> Rows { get; set; } = new();
        /// <summary>Every column is 16 characters wide; an autofilter covers the whole range.</summary>
        public double ColumnWidth { get; set; } = 16;
        /// <summary>"Exported N rows".</summary>
        public string Message { get; set; } = "";
    }
}
