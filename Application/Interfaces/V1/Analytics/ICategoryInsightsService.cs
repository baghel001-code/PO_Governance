using DecisionEngine.Results.Analytics.CatInsights;
using DecisionEngine.Results.Analytics.PlannerDashboard;
using Domain.Records;

namespace Application.Interfaces.V1.Analytics
{
    /// <summary>
    /// Category "📊 Insights" dashboard (index.html view-cat-insights, renderCatInsights line 8622):
    /// aggregation of the visible Tool 1 requests. Any role. The aggregation itself is
    /// <c>DecisionEngine.Analytics.Insights.IInsightsEngine.BuildCategoryInsights</c>.
    /// </summary>
    public interface ICategoryInsightsService
    {
        /// <summary>Loads the visible requests (IRequestHistorySource, default today) and builds the dashboard.</summary>
        Task<CatInsightsDto> GetAsync(CatInsightsQuery? query, AppUserContext user, CancellationToken ct = default);
    }

    /// <summary>
    /// Planner dashboard over a Tool 2 run (renderPlannerDashboard, line 9062). Staff only. Note: v1.1.4's
    /// index.html has no planner-dash-* markup any more, so the function returned early; it is ported as a service.
    /// The aggregation itself is <c>DecisionEngine.Analytics.Insights.IInsightsEngine.BuildPlannerDashboard</c>.
    /// </summary>
    public interface IPlannerDashboardService
    {
        Task<PlannerDashboardDto> GetAsync(PlannerDashboardQuery? query, AppUserContext user, CancellationToken ct = default);
    }
}
