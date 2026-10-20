using Application.Interfaces.V1;
using Application.Interfaces.V1.Analytics;
using DecisionEngine.Analytics.Insights;
using DecisionEngine.Results.Analytics.CatInsights;
using Domain.Records;

namespace Application.Services.V1.Analytics.CatInsights
{
    /// <summary>
    /// Category "📊 Insights" use case: loads the requests the user may see (<see cref="IRequestHistorySource"/>,
    /// default range = today on the server clock) and builds the dashboard with <see cref="IInsightsEngine"/>.
    /// </summary>
    public sealed class CategoryInsightsService : ICategoryInsightsService
    {
        private readonly IRequestHistorySource _history;
        private readonly IAppClock _clock;
        private readonly IInsightsEngine _engine;

        public CategoryInsightsService(IRequestHistorySource history, IAppClock clock, IInsightsEngine engine)
        {
            _history = history;
            _clock = clock;
            _engine = engine;
        }

        public async Task<CatInsightsDto> GetAsync(CatInsightsQuery? query, AppUserContext user, CancellationToken ct = default)
        {
            query ??= new CatInsightsQuery();
            var from = string.IsNullOrWhiteSpace(query.From) ? _clock.Today : query.From!;
            var to = string.IsNullOrWhiteSpace(query.To) ? from : query.To!;
            var reqs = await _history.GetVisibleRequestsAsync(user, from, to, ct);
            return _engine.BuildCategoryInsights(reqs, query);
        }
    }
}
