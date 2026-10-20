using DecisionEngine.Results.Analytics.Common;

namespace DecisionEngine.Results.Analytics.CatInsights
{
    /// <summary>Filters of the Category dashboard (cd-f-*). Empty / null = "All".</summary>
    public sealed class CatInsightsQuery
    {
        public string? Brand { get; set; }
        public string? Division { get; set; }
        /// <summary>MC Desc ("Unclassified" = requests without one).</summary>
        public string? Mc { get; set; }
        /// <summary>Delivery month "MM.YYYY" ("—" = requests without one).</summary>
        public string? Month { get; set; }
        /// <summary>"B" (Single Concept MRP), "A" (Multi Concept MRP), "NEW" (New Article).</summary>
        public string? Engine { get; set; }
        public string? Decision { get; set; }
        /// <summary>Optional request-date range (yyyy-MM-dd); default = today (v1.1.4 showed today's requests only).</summary>
        public string? From { get; set; }
        public string? To { get; set; }
    }

    public sealed class CatInsightsFiltersDto
    {
        public List<string> Brands { get; set; } = new();
        public List<string> Divisions { get; set; } = new();
        public List<string> Mcs { get; set; } = new();
        public List<string> Months { get; set; } = new();
        public List<OptionDto> Engines { get; set; } = new();
        public List<OptionDto> Decisions { get; set; } = new();
    }

    public sealed class CatMcRiskRowDto
    {
        public string Mc { get; set; } = "";
        public int Requests { get; set; }
        public double Qty { get; set; }
        public string QtyText { get; set; } = "";
        public int Allow { get; set; }
        public int Watch { get; set; }
        public int Block { get; set; }
        public double RiskPct { get; set; }
        /// <summary>≥50% #DC2626, ≥20% #D97706, else #00873E.</summary>
        public string RiskColor { get; set; } = "";
    }

    public sealed class CatMcRiskTableDto
    {
        /// <summary>"Risk by Merchandise Category (MC)" (+ " — top 20 of N").</summary>
        public string Title { get; set; } = "";
        public int TotalMcs { get; set; }
        public List<CatMcRiskRowDto> Rows { get; set; } = new();
    }

    public sealed class CatFlaggedRowDto
    {
        /// <summary>The request's per-day number (r.id) as displayed.</summary>
        public string Id { get; set; } = "";
        public string? ReqId { get; set; }
        public string Gm { get; set; } = "";
        public string Colour { get; set; } = "";
        public string McDesc { get; set; } = "";
        public string Decision { get; set; } = "";
        /// <summary>dpill class: dp-allow / dp-reduce / dp-watch / dp-hold / dp-block / dp-nodata.</summary>
        public string DecisionPill { get; set; } = "";
        public string Driver { get; set; } = "";
    }

    public sealed class CatFlaggedTableDto
    {
        /// <summary>"Flagged Requests — Needs Attention Before Raising" (+ " (most recent 25 of N)").</summary>
        public string Title { get; set; } = "";
        public int TotalFlagged { get; set; }
        public List<CatFlaggedRowDto> Rows { get; set; } = new();
    }

    /// <summary>The whole "Category Dashboard" (renderCatInsights).</summary>
    public sealed class CatInsightsDto
    {
        /// <summary>"noRequests", "noMatch" or "ok".</summary>
        public string State { get; set; } = "ok";
        public string? EmptyTitle { get; set; }
        public string? EmptyMessage { get; set; }
        /// <summary>"New Request →" (noRequests) or "Clear Filters" (noMatch).</summary>
        public string? EmptyAction { get; set; }
        /// <summary>Visible requests before filtering / after filtering.</summary>
        public int TotalAll { get; set; }
        public int Total { get; set; }
        public bool FilterActive { get; set; }
        public CatInsightsFiltersDto Options { get; set; } = new();
        /// <summary>The filters actually applied (a stale Brand / Division / MC / Month that is no longer an option is dropped).</summary>
        public CatInsightsQuery Applied { get; set; } = new();
        public List<KpiTileDto> Kpis { get; set; } = new();
        public DecisionMixDto? DecisionMix { get; set; }
        public DriverBreakdownDto? Drivers { get; set; }
        public CatMcRiskTableDto? McRisk { get; set; }
        /// <summary>Null when nothing is flagged (the section is not shown).</summary>
        public CatFlaggedTableDto? Flagged { get; set; }
        public RecommendationsCardDto? Recommendations { get; set; }
    }
}
