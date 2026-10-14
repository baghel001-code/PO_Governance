using Application.DTOs.Analytics.AttrAnalysis;
using Domain.Entities.Engine;

namespace Application.Interfaces.V1.Analytics
{
    /// <summary>
    /// Planner "🔎 Attribute Analysis" screen (index.html view-attranalysis, functions _aaBuildTree …
    /// _aaRenderLevel3, lines 9516–9902): Brand → MC → MRP × Flag drill-down with Sales Mix / Stock Mix /
    /// Deviation per Article Master attribute and colour-level STR. Read-only; staff only.
    /// </summary>
    public interface IAttributeAnalysisService
    {
        /// <summary>Level 1 — Brand → MC table with the filters applied.</summary>
        Task<AaLevel1Dto> GetLevel1Async(AttrAnalysisQuery? query, CancellationToken ct = default);
        /// <summary>Level 2 — MRP × Flag buckets of one Brand × MC (404 when the MC is not in the data).</summary>
        Task<AaLevel2Dto> GetLevel2Async(string brand, string mcKey, CancellationToken ct = default);
        /// <summary>Level 3 — attribute tables + colour-level STR of one bucket (404 when unknown).</summary>
        Task<AaLevel3Dto> GetLevel3Async(string brand, string mcKey, string mrp, string flag, CancellationToken ct = default);

        AaLevel1Dto GetLevel1(EngineData data, AttrAnalysisQuery? query);
        AaLevel2Dto GetLevel2(EngineData data, string brand, string mcKey);
        AaLevel3Dto GetLevel3(EngineData data, string brand, string mcKey, string mrp, string flag);
    }
}
