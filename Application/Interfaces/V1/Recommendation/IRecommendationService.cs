using DecisionEngine.Recommendation;
using DecisionEngine.Results.Recommendation;

namespace Application.Interfaces.V1.Recommendation
{
    /// <summary>
    /// "View Recommendation" use cases: loads the current data snapshot (<see cref="IDatasetCache"/>),
    /// today's requests of all users (<see cref="IRequestLedger"/>) and the server date
    /// (<see cref="IAppClock"/>), then runs <see cref="IRecommendationEngine"/> the way index.html
    /// <c>openRec</c> / <c>_openRec</c> / <c>_render</c> did.
    /// </summary>
    public interface IRecommendationService
    {
        /// <summary>The whole modal: header, month bar, Single Concept or Multi Concept body and painted texts.</summary>
        Task<RecommendationViewDto> GetAsync(RecommendationRequestDto request, CancellationToken ct = default);

        /// <summary>The month bar only (every F4 month of the bucket + the default selection).</summary>
        Task<RecommendationMonthsDto> GetMonthsAsync(RecommendationRequestDto request, CancellationToken ct = default);

        /// <summary>Rows of the full-detail Excel export (Multi Concept only).</summary>
        Task<RecommendationExportDto> BuildExportAsync(RecommendationRequestDto request, CancellationToken ct = default);

        /// <summary>A context (snapshot + today's ledger + server date) for other engines (Tool 1 / Tool 2) that call the engine directly.</summary>
        Task<RecommendationContext> CreateContextAsync(CancellationToken ct = default);
    }

    /// <summary>Writes the <see cref="RecommendationExportDto"/> workbook (.xlsx bytes).</summary>
    public interface IRecommendationExcelExporter
    {
        byte[] Write(RecommendationExportDto export);
    }
}
