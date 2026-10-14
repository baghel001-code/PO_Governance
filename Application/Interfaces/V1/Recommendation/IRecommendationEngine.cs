using Application.DTOs.Recommendation;
using Application.Services.V1.Recommendation;
using Domain.Entities.Engine;

namespace Application.Interfaces.V1.Recommendation
{
    /// <summary>
    /// The "View Recommendation" engine (index.html second &lt;script&gt;, lines 9369–11512) — pure:
    /// every method reads only the <see cref="RecommendationContext"/> it is given.
    /// Bucket arguments (mc, mrp, brand, flag) are matched after <c>_n</c> normalisation, so raw or
    /// normalised values both work.
    /// </summary>
    public interface IRecommendationEngine
    {
        /// <summary>Port of <c>_runB</c> — Single Concept: colour × month STR ranking with Max Permissible qty.</summary>
        RecBResultDto RunB(RecommendationContext ctx, string mc, string mrp, string brand, string flag, IReadOnlyList<string> months);

        /// <summary>Port of <c>_runA</c> — Multi Concept: attribute → colour → BSR Q1/Q2 + indicative qty.</summary>
        RecAResultDto RunA(RecommendationContext ctx, string mc, string mrp, string brand, string flag, IReadOnlyList<string> months, string? overrideDim = null);

        /// <summary>Port of <c>_runBCached</c>: memoised in <paramref name="cache"/> (when given); any exception → null.</summary>
        RecBResultDto? RunBCached(RecommendationContext ctx, string mc, string mrp, string brand, string flag, IReadOnlyList<string> months, RecommendationRunCache? cache = null);

        /// <summary>Port of <c>_runACached</c>: memoised in <paramref name="cache"/> (when given); any exception → null.</summary>
        RecAResultDto? RunACached(RecommendationContext ctx, string mc, string mrp, string brand, string flag, IReadOnlyList<string> months, RecommendationRunCache? cache = null);

        /// <summary>Port of <c>_strCaution</c> — Tool 1's information-only STR heads-up on a Q1/Q2 Allow (null = no caution).</summary>
        StrCautionDto? StrCaution(RecommendationContext ctx, string mc, string mrp, string brand, string flag, string colour, string gm, string? delMonth);

        /// <summary>Port of <c>_coreColourSTRFast</c> with plain dictionaries (month → qty).</summary>
        CoreStrResultDto? CoreColourStr(RecommendationContext ctx, double opStock, double mixPct, IReadOnlyDictionary<string, double> forecastByMon,
                                        IReadOnlyDictionary<string, double>? f2RawMap, string anchorMonth);

        /// <summary>Port of <c>_bsrQ</c>.</summary>
        BsrQDto? BsrQ(RecommendationContext ctx, string gm, string colour, string mc, string mrp, string flag);

        /// <summary>Port of <c>_getGMsForColour</c>.</summary>
        List<string> GetGMsForColour(RecommendationContext ctx, string mc, string mrp, string brand, string flag, string colour);

        /// <summary>Port of <c>_assessDim</c>: the Attribute Relevance Master dimension of the bucket, or null.</summary>
        string? AssessDim(EngineData data, string mc, string mrp, string brand, string flag);

        /// <summary>Port of <c>_buildMonths</c>: every F4 month column of the bucket, chronological.</summary>
        List<string> BuildMonths(EngineData data, string mc, string mrp, string brand, string flag);

        /// <summary>The default month selection of <c>_openRec</c>: months from today's on, max 4; else the first 4.</summary>
        List<string> DefaultMonths(IReadOnlyList<string> allMonths, string today);

        /// <summary>Port of <c>_buildDimOverrideOptions</c>.</summary>
        List<DimOptionDto> DimOverrideOptions(string? relevantDim, string? dimOverride);

        /// <summary>Port of <c>_recQty</c>.</summary>
        RecQtyResultDto RecQty(EngineParams p, double stock, double pending, double f4Total, string month, double sales45, double totalSales45);

        /// <summary>
        /// Port of <c>_exportEngineARecs</c>: the 5 sheets of the full-detail workbook for a Multi Concept
        /// result. Throws <c>AppValidationException("No recommendation rows to export")</c> when there are no recs.
        /// </summary>
        RecommendationExportDto BuildExport(RecommendationContext ctx, RecAResultDto res, string fileDate);

        /// <summary>
        /// The painted figures / texts of <c>_paintRec</c> for a result (summary line, headings,
        /// tooltips, formulas, Step 2+3 row layout). Fills <c>tip</c> / <c>formula</c> on the month DTOs.
        /// </summary>
        RecommendationPaintDto Paint(EngineParams p, RecBResultDto? b, RecAResultDto? a);
    }
}
