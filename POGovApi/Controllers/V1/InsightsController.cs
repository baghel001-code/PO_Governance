using Application.Interfaces.V1.Analytics;
using DecisionEngine.Results.Analytics.CatInsights;
using DecisionEngine.Results.Analytics.PlannerDashboard;
using Domain.Entities.Response;
using Microsoft.AspNetCore.Mvc;

namespace POGovApi.Controllers.V1
{
    /// <summary>
    /// Category "📊 Insights" dashboard (any role — over the requests the user may see) and the Tool 2 Planner
    /// dashboard (planners and admins). "Clear Filters" = call again without filters.
    /// </summary>
    [Route("api/v1/insights")]
    public sealed class InsightsController : ApiControllerBase
    {
        private readonly ICategoryInsightsService _cat;
        private readonly IPlannerDashboardService _planner;

        public InsightsController(ICategoryInsightsService cat, IPlannerDashboardService planner)
        {
            _cat = cat;
            _planner = planner;
        }

        /// <summary>Category dashboard (renderCatInsights). Filters: brand, division, mc, month, engine, decision; optional from/to dates (default today).</summary>
        [HttpGet("category")]
        public async Task<ActionResult<ApiResponse<CatInsightsDto>>> Category([FromQuery] CatInsightsQuery query, CancellationToken ct) =>
            Success(await _cat.GetAsync(query, Me, ct));

        /// <summary>Planner dashboard over the latest (or a saved, runId) Tool 2 run (renderPlannerDashboard).</summary>
        [HttpGet("planner")]
        public async Task<ActionResult<ApiResponse<PlannerDashboardDto>>> Planner([FromQuery] PlannerDashboardQuery query, CancellationToken ct)
        {
            RequireStaff();
            return Success(await _planner.GetAsync(query, Me, ct));
        }
    }
}
