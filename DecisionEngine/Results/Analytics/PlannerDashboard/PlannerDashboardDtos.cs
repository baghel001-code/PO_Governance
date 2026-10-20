using DecisionEngine.Results.Analytics.Common;

namespace DecisionEngine.Results.Analytics.PlannerDashboard
{
    /// <summary>Filters of the Planner dashboard (pd-f-*). Empty / null = "All".</summary>
    public sealed class PlannerDashboardQuery
    {
        /// <summary>A saved Tool 2 run; null = the latest run.</summary>
        public string? RunId { get; set; }
        public string? Brand { get; set; }
        public string? Division { get; set; }
        public string? Mc { get; set; }
        public string? Mrp { get; set; }
        public string? Flag { get; set; }
        /// <summary>Delivery month ("—" = none).</summary>
        public string? DeliveryMonth { get; set; }
        /// <summary>"A" / "B".</summary>
        public string? Engine { get; set; }
        public string? Decision { get; set; }
    }

    public sealed class PlannerFiltersDto
    {
        public List<string> Brands { get; set; } = new();
        public List<string> Divisions { get; set; } = new();
        public List<string> Mcs { get; set; } = new();
        /// <summary>Sorted numerically (non-numeric first).</summary>
        public List<string> Mrps { get; set; } = new();
        public List<string> Flags { get; set; } = new();
        public List<string> DeliveryMonths { get; set; } = new();
        public List<OptionDto> Engines { get; set; } = new();
        public List<OptionDto> Decisions { get; set; } = new();
    }

    public sealed class PlannerMcRiskRowDto
    {
        public string Mc { get; set; } = "";
        public int Lines { get; set; }
        public int Retain { get; set; }
        public int Watch { get; set; }
        public int Cut { get; set; }
        public double QtyAtRisk { get; set; }
        public double ValueAtRisk { get; set; }
        public string ValueAtRiskText { get; set; } = "";
        public double RiskPct { get; set; }
        public string RiskColor { get; set; } = "";
    }

    public sealed class PlannerMcRiskTableDto
    {
        public string Title { get; set; } = "";
        public int TotalMcs { get; set; }
        public List<PlannerMcRiskRowDto> Rows { get; set; } = new();
    }

    /// <summary>The Planner dashboard (renderPlannerDashboard) over a Tool 2 run.</summary>
    public sealed class PlannerDashboardDto
    {
        /// <summary>"noRun" (placeholder shown, filters hidden), "noMatch" or "ok".</summary>
        public string State { get; set; } = "ok";
        public string? EmptyTitle { get; set; }
        public string? EmptyMessage { get; set; }
        public string? RunId { get; set; }
        public int TotalAll { get; set; }
        public int Total { get; set; }
        public bool FilterActive { get; set; }
        public PlannerFiltersDto Options { get; set; } = new();
        public PlannerDashboardQuery Applied { get; set; } = new();
        public List<KpiTileDto> Kpis { get; set; } = new();
        public double QtyAtRisk { get; set; }
        public double ValueAtRisk { get; set; }
        public DecisionMixDto? DecisionMix { get; set; }
        public DriverBreakdownDto? Drivers { get; set; }
        public PlannerMcRiskTableDto? McRisk { get; set; }
        public RecommendationsCardDto? Recommendations { get; set; }
    }
}
