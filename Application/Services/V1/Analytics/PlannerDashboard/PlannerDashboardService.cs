using System.Text.Json.Nodes;
using Application.Interfaces.V1.Analytics;
using DecisionEngine.Analytics.Insights;
using DecisionEngine.Results.Analytics.PlannerDashboard;
using Domain.Exceptions;
using Domain.Records;

namespace Application.Services.V1.Analytics.PlannerDashboard
{
    /// <summary>
    /// Planner dashboard use case: loads the rows of the requested (or latest) Tool 2 run
    /// (<see cref="ITool2ResultSource"/>) and builds the dashboard with <see cref="IInsightsEngine"/>.
    /// </summary>
    public sealed class PlannerDashboardService : IPlannerDashboardService
    {
        private readonly ITool2ResultSource _runs;
        private readonly IInsightsEngine _engine;

        public PlannerDashboardService(ITool2ResultSource runs, IInsightsEngine engine)
        {
            _runs = runs;
            _engine = engine;
        }

        public async Task<PlannerDashboardDto> GetAsync(PlannerDashboardQuery? query, AppUserContext user, CancellationToken ct = default)
        {
            query ??= new PlannerDashboardQuery();
            Tool2RunRows? run;
            if (!string.IsNullOrEmpty(query.RunId))
            {
                run = await _runs.GetRunRowsAsync(query.RunId!, user, ct);
                if (run == null) throw new NotFoundException("Run not found");
            }
            else run = await _runs.GetLatestRowsAsync(user, ct);
            var dto = _engine.BuildPlannerDashboard(run?.Rows ?? Array.Empty<JsonObject>(), query);
            dto.RunId = run?.RunId;
            return dto;
        }
    }
}
