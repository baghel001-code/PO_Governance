using Application.Interfaces.V1;
using Application.Interfaces.V1.Analytics;
using DecisionEngine.Grc;
using DecisionEngine.Model;
using DecisionEngine.Results.Analytics.Grc;
using Domain.Exceptions;

namespace Application.Services.V1.Analytics.Grc
{
    /// <summary>
    /// GRC Back Testing use cases (see <see cref="IGrcBacktestService"/>). Reads the current snapshot from
    /// <see cref="IDatasetCache"/> and the server clock for "now", then calls <see cref="IGrcBacktestEngine"/>
    /// (DecisionEngine), which applies the effective Season Selection and caches back-test results per
    /// snapshot and inputs (the browser kept the last result in <c>_grcBacktestCache</c>).
    /// </summary>
    public sealed class GrcBacktestService : IGrcBacktestService
    {
        private readonly IDatasetCache _cache;
        private readonly IAppClock _clock;
        private readonly IGrcBacktestEngine _engine;

        public GrcBacktestService(IDatasetCache cache, IAppClock clock, IGrcBacktestEngine engine)
        {
            _cache = cache;
            _clock = clock;
            _engine = engine;
        }

        private double NowMs => _clock.Now.ToUnixTimeMilliseconds();

        public async Task<GrcControlsDto> GetOptionsAsync(GrcImpactRequest? request = null, CancellationToken ct = default) =>
            Compute(await _cache.GetCurrentAsync(ct), request ?? new GrcImpactRequest()).Controls;

        public async Task<GrcImpactDto> ComputeAsync(GrcImpactRequest request, CancellationToken ct = default) =>
            Compute(await _cache.GetCurrentAsync(ct), request);

        public async Task<GrcArticleTraceDto> TraceAsync(string? gm, string? asOfPeriod = null, CancellationToken ct = default) =>
            Trace(await _cache.GetCurrentAsync(ct), gm, asOfPeriod);

        public async Task<GrcRowDetailExportDto> GetRowDetailAsync(string? asOfPeriod = null, CancellationToken ct = default) =>
            GetRowDetail(await _cache.GetCurrentAsync(ct), asOfPeriod) ?? throw new NotFoundException("No GRC file loaded");

        public GrcImpactDto Compute(EngineData data, GrcImpactRequest request) => _engine.Compute(data, request, NowMs);

        public GrcArticleTraceDto Trace(EngineData data, string? gm, string? asOfPeriod) => _engine.Trace(data, gm, asOfPeriod, NowMs);

        public GrcRowDetailExportDto? GetRowDetail(EngineData data, string? asOfPeriod) => _engine.GetRowDetail(data, asOfPeriod, NowMs);
    }
}
