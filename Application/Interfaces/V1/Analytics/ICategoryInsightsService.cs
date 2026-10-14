using System.Text.Json.Nodes;
using Application.DTOs.Analytics.CatInsights;
using Domain.Records;

namespace Application.Interfaces.V1.Analytics
{
    /// <summary>
    /// Category "📊 Insights" dashboard (index.html view-cat-insights, renderCatInsights line 8622):
    /// aggregation of the visible Tool 1 requests. Any role.
    /// </summary>
    public interface ICategoryInsightsService
    {
        /// <summary>Loads the visible requests (IRequestHistorySource, default today) and builds the dashboard.</summary>
        Task<CatInsightsDto> GetAsync(CatInsightsQuery? query, AppUserContext user, CancellationToken ct = default);

        /// <summary>Pure port of renderCatInsights over already-visible requests.</summary>
        CatInsightsDto Build(IReadOnlyList<JsonObject> visibleRequests, CatInsightsQuery? query);
    }

    /// <summary>
    /// Planner dashboard over a Tool 2 run (renderPlannerDashboard, line 9062). Staff only. Note: v1.1.4's
    /// index.html has no planner-dash-* markup any more, so the function returned early; it is ported as a service.
    /// </summary>
    public interface IPlannerDashboardService
    {
        Task<DTOs.Analytics.PlannerDashboard.PlannerDashboardDto> GetAsync(DTOs.Analytics.PlannerDashboard.PlannerDashboardQuery? query, AppUserContext user, CancellationToken ct = default);

        /// <summary>Pure port of renderPlannerDashboard over the rows of one run (empty = no run).</summary>
        DTOs.Analytics.PlannerDashboard.PlannerDashboardDto Build(IReadOnlyList<JsonObject> rows, DTOs.Analytics.PlannerDashboard.PlannerDashboardQuery? query);
    }
}
