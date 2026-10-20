using System.Text.Json.Nodes;
using DecisionEngine.Analytics.PlannerDashboard;
using DecisionEngine.Results.Analytics.CatInsights;
using DecisionEngine.Results.Analytics.PlannerDashboard;

namespace DecisionEngine.Analytics.Insights
{
    /// <summary>
    /// The two insight dashboards, computed from already-loaded rows (the caller decides which rows the user may see):
    /// the Category "📊 Insights" dashboard over Tool 1 requests (index.html <c>renderCatInsights</c>, line 8622) and
    /// the Planner dashboard over the rows of one Tool 2 run (<c>renderPlannerDashboard</c>, line 9062).
    /// </summary>
    public interface IInsightsEngine
    {
        /// <summary>Port of renderCatInsights over the visible requests (stored v1.1.4 request objects).</summary>
        CatInsightsDto BuildCategoryInsights(IReadOnlyList<JsonObject> visibleRequests, CatInsightsQuery? query);

        /// <summary>Port of renderPlannerDashboard over the rows of one run (empty = no run).</summary>
        PlannerDashboardDto BuildPlannerDashboard(IReadOnlyList<JsonObject> runRows, PlannerDashboardQuery? query);
    }

    /// <summary>Default <see cref="IInsightsEngine"/> over <see cref="CategoryInsightsBuilder"/> and <see cref="PlannerDashboardBuilder"/>.</summary>
    public sealed class InsightsEngine : IInsightsEngine
    {
        private readonly CategoryInsightsBuilder _category = new();
        private readonly PlannerDashboardBuilder _planner = new();

        public CatInsightsDto BuildCategoryInsights(IReadOnlyList<JsonObject> visibleRequests, CatInsightsQuery? query) =>
            _category.Build(visibleRequests, query);

        public PlannerDashboardDto BuildPlannerDashboard(IReadOnlyList<JsonObject> runRows, PlannerDashboardQuery? query) =>
            _planner.Build(runRows, query);
    }
}
