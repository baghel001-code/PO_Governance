using Application.Interfaces.V1.Analytics;
using DecisionEngine.Results.Analytics.CatInsights;
using DecisionEngine.Results.Analytics.PlannerDashboard;
using Domain.Entities.Response;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace POGovApi.Controllers.V1
{
    /// <summary>
    /// Category "📊 Insights" dashboard (any role — over the requests the user may see) and the Tool 2 Planner
    /// dashboard (planners and admins). "Clear Filters" = call again without filters.
    /// </summary>
    [Route("api/v1/[Action]")]
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
        [HttpPost]
        public async Task<ActionResult<ApiDataResponse<CatInsightsDto>>> InsightsCategory([FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] CatInsightsQuery? req, CancellationToken ct) =>
            Success(await _cat.GetAsync(req ?? new CatInsightsQuery(), Me, ct));

        /// <summary>Planner dashboard over the latest (or a saved, runId) Tool 2 run (renderPlannerDashboard).</summary>
        [HttpPost]
        public async Task<ActionResult<ApiDataResponse<PlannerDashboardDto>>> InsightsPlanner([FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] PlannerDashboardQuery? req, CancellationToken ct)
        {
            RequireStaff();
            return Success(await _planner.GetAsync(req ?? new PlannerDashboardQuery(), Me, ct));
        }
    }
}
