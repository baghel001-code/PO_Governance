using Application.DTOs.Analytics.Grc;
using Domain.Entities.Engine;

namespace Application.Interfaces.V1.Analytics
{
    /// <summary>
    /// Planner "📈 GRC Back Testing" screen: how today's Multi Concept MRP peer-ranking rules would have
    /// judged past buying (GRC — Actual Buying History file). Port of index.html lines 2685-3643.
    /// </summary>
    public interface IGrcBacktestService
    {
        /// <summary>Filter / control options for the screen (as the browser populated its selects).</summary>
        Task<GrcControlsDto> GetOptionsAsync(GrcImpactRequest? request = null, CancellationToken ct = default);

        /// <summary>The whole GRC Impact card (renderGRCImpact) for the current dataset.</summary>
        Task<GrcImpactDto> ComputeAsync(GrcImpactRequest request, CancellationToken ct = default);

        /// <summary>Article Trace for one GM (renderArticleTrace) at the given / default "as of" period.</summary>
        Task<GrcArticleTraceDto> TraceAsync(string? gm, string? asOfPeriod = null, CancellationToken ct = default);

        /// <summary>Rows of "⬇ Export Row Detail" (exportGRCRowDetail). Throws NotFoundException("No GRC file loaded") without a GRC file.</summary>
        Task<GrcRowDetailExportDto> GetRowDetailAsync(string? asOfPeriod = null, CancellationToken ct = default);

        // ── Synchronous, snapshot-based variants (used by the async ones and by tests) ──
        GrcImpactDto Compute(EngineData data, GrcImpactRequest request);
        GrcArticleTraceDto Trace(EngineData data, string? gm, string? asOfPeriod);
        /// <summary>Null when no GRC file is loaded.</summary>
        GrcRowDetailExportDto? GetRowDetail(EngineData data, string? asOfPeriod);
    }
}
