using Application.DTOs.Analytics.Grc;
using Application.Interfaces.V1;
using Application.Interfaces.V1.Analytics;
using Domain.Entities.Engine;
using Domain.Exceptions;

namespace Application.Services.V1.Analytics.Grc
{
    /// <summary>
    /// GRC Back Testing service (see <see cref="IGrcBacktestService"/>). Reads the current snapshot from
    /// <see cref="IDatasetCache"/>, uses the effective Season Selection (<see cref="ISeasonService"/>) and the
    /// server clock for "today". Back-test results are cached per snapshot and inputs (the browser kept
    /// the last result in <c>_grcBacktestCache</c>).
    /// </summary>
    public sealed class GrcBacktestService : IGrcBacktestService
    {
        private readonly IDatasetCache _cache;
        private readonly IAppClock _clock;
        private readonly ISeasonService _seasons;

        public GrcBacktestService(IDatasetCache cache, IAppClock clock, ISeasonService seasons)
        {
            _cache = cache;
            _clock = clock;
            _seasons = seasons;
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

        public GrcImpactDto Compute(EngineData data, GrcImpactRequest request)
        {
            var sel = _seasons.Resolve(data);
            var seasonsKey = string.Join(",", sel.SeasonCurrent) + "|" + string.Join(",", sel.SeasonFallback);
            return GrcImpactBuilder.Build(data, request ?? new GrcImpactRequest(), sel.SeasonCurrent, sel.SeasonFallback, NowMs,
                (engine, div, catHead, viewMode, brand) =>
                {
                    // No GRC rows, or no "as of" (depends on the current time): not cached.
                    if (engine.AsOfPeriod == null || data.Grc is not { Count: > 0 }) return engine.Compute(div, catHead, viewMode, brand);
                    var key = string.Join("|", "grc|bt", div ?? "", catHead ?? "", viewMode, brand ?? "", engine.AsOfPeriod, seasonsKey);
                    return data.GetOrBuild(key, _ => engine.Compute(div, catHead, viewMode, brand)!);
                });
        }

        /// <summary>
        /// The trace and the export use the same "as of" as the screen: the requested period, else the
        /// saved one, else the last period of the file (the browser read S.params.grcAsOfPeriod, which the
        /// GRC Impact render had just set the same way).
        /// </summary>
        public GrcArticleTraceDto Trace(EngineData data, string? gm, string? asOfPeriod)
        {
            var sel = _seasons.Resolve(data);
            var engine = new GrcBacktestEngine(data, sel.SeasonCurrent, sel.SeasonFallback, GrcImpactBuilder.ResolveAsOf(data, asOfPeriod), NowMs);
            return engine.Trace(gm);
        }

        public GrcRowDetailExportDto? GetRowDetail(EngineData data, string? asOfPeriod)
        {
            var sel = _seasons.Resolve(data);
            var engine = new GrcBacktestEngine(data, sel.SeasonCurrent, sel.SeasonFallback, GrcImpactBuilder.ResolveAsOf(data, asOfPeriod), NowMs);
            return engine.RowDetail(NowMs);
        }
    }
}
