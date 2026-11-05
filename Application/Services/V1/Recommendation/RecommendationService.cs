using System.Globalization;
using Application.Interfaces.V1;
using Application.Interfaces.V1.Recommendation;
using DecisionEngine.Recommendation;
using DecisionEngine.Results.Recommendation;

namespace Application.Services.V1.Recommendation
{
    /// <inheritdoc cref="IRecommendationService"/>
    public sealed class RecommendationService : IRecommendationService
    {
        private readonly IDatasetCache _cache;
        private readonly IRequestLedger _ledger;
        private readonly IAppClock _clock;
        private readonly IRecommendationEngine _engine;

        private readonly Interfaces.V1.Sap.IMraCapacitySource? _mra;

        public RecommendationService(IDatasetCache cache, IRequestLedger ledger, IAppClock clock, IRecommendationEngine engine,
                                     Interfaces.V1.Sap.IMraCapacitySource? mra = null)
        {
            _cache = cache; _ledger = ledger; _clock = clock; _engine = engine; _mra = mra;
        }

        public async Task<RecommendationContext> CreateContextAsync(CancellationToken ct = default)
        {
            var data = await _cache.GetCurrentAsync(ct);
            var today = _clock.Today;
            var (requests, _) = await _ledger.GetDayAsync(today, ct);
            return new RecommendationContext(data, requests, today);
        }

        public async Task<RecommendationMonthsDto> GetMonthsAsync(RecommendationRequestDto request, CancellationToken ct = default)
        {
            var data = await _cache.GetCurrentAsync(ct);
            return EngineErrors.Run(() => _engine.Months(data, request, _clock.Today));
        }

        public async Task<RecommendationViewDto> GetAsync(RecommendationRequestDto request, CancellationToken ct = default)
        {
            var ctx = await CreateContextAsync(ct);
            // v1.1.7: open MRAs from SAP are added in the single concept Recommendation view
            using var mra = DecisionEngine.Mra.MraScope.Use(_mra == null ? null : await _mra.GetAsync(ctx.Data, ct));
            return EngineErrors.Run(() => _engine.View(ctx, request));
        }

        public async Task<RecommendationExportDto> BuildExportAsync(RecommendationRequestDto request, CancellationToken ct = default)
        {
            var ctx = await CreateContextAsync(ct);
            var fileDate = _clock.Now.UtcDateTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            return EngineErrors.Run(() => _engine.ExportView(ctx, request, fileDate));
        }
    }
}
