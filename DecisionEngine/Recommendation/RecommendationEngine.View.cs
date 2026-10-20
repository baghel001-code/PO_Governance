using DecisionEngine.Model;
using DecisionEngine.Results.Recommendation;

namespace DecisionEngine.Recommendation
{
    public sealed partial class RecommendationEngine
    {
        public RecommendationViewDto View(RecommendationContext ctx, RecommendationRequestDto request) =>
            RecommendationView.Build(this, ctx, request);

        public RecommendationMonthsDto Months(EngineData data, RecommendationRequestDto request, string today) =>
            RecommendationView.Months(this, data, request, today);

        public RecommendationExportDto ExportView(RecommendationContext ctx, RecommendationRequestDto request, string fileDate) =>
            RecommendationView.Export(this, ctx, request, fileDate);
    }
}
