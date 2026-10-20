using DecisionEngine.Model;
using DecisionEngine.Results.Analytics.Grc;

namespace DecisionEngine.Grc
{
    /// <summary>
    /// GRC Back Testing façade (index.html lines 2685-3643): how today's Multi Concept MRP peer-ranking
    /// rules would have judged past buying. Pure and synchronous — the snapshot, the request and "now"
    /// (epoch ms, for the default "as of" period) are passed in.
    /// </summary>
    public interface IGrcBacktestEngine
    {
        /// <summary>The whole GRC Impact card (renderGRCImpact). Back-test results are memoised per snapshot + inputs.</summary>
        GrcImpactDto Compute(EngineData data, GrcImpactRequest request, double nowMs);

        /// <summary>Article Trace for one GM (renderArticleTrace) at the given / saved / last "as of" period.</summary>
        GrcArticleTraceDto Trace(EngineData data, string? gm, string? asOfPeriod, double nowMs);

        /// <summary>Rows of "⬇ Export Row Detail" (exportGRCRowDetail); null when no GRC file is loaded.</summary>
        GrcRowDetailExportDto? GetRowDetail(EngineData data, string? asOfPeriod, double nowMs);
    }

    /// <summary>Default <see cref="IGrcBacktestEngine"/>: <see cref="GrcImpactBuilder"/> + <see cref="GrcBacktestEngine"/> with the effective Season Selection.</summary>
    public sealed class GrcBacktester : IGrcBacktestEngine
    {
        private readonly ISeasonSelectionEngine _seasons;

        public GrcBacktester(ISeasonSelectionEngine? seasons = null) => _seasons = seasons ?? new SeasonSelectionEngine();

        public GrcImpactDto Compute(EngineData data, GrcImpactRequest request, double nowMs)
        {
            var sel = _seasons.Resolve(data);
            var seasonsKey = string.Join(",", sel.SeasonCurrent) + "|" + string.Join(",", sel.SeasonFallback);
            return GrcImpactBuilder.Build(data, request ?? new GrcImpactRequest(), sel.SeasonCurrent, sel.SeasonFallback, nowMs,
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
        public GrcArticleTraceDto Trace(EngineData data, string? gm, string? asOfPeriod, double nowMs)
        {
            var sel = _seasons.Resolve(data);
            var engine = new GrcBacktestEngine(data, sel.SeasonCurrent, sel.SeasonFallback, GrcImpactBuilder.ResolveAsOf(data, asOfPeriod), nowMs);
            return engine.Trace(gm);
        }

        public GrcRowDetailExportDto? GetRowDetail(EngineData data, string? asOfPeriod, double nowMs)
        {
            var sel = _seasons.Resolve(data);
            var engine = new GrcBacktestEngine(data, sel.SeasonCurrent, sel.SeasonFallback, GrcImpactBuilder.ResolveAsOf(data, asOfPeriod), nowMs);
            return engine.RowDetail(nowMs);
        }
    }
}
