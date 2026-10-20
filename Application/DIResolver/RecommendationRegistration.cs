using Application.Interfaces.V1.Recommendation;
using Application.Services.V1.Recommendation;
using Microsoft.Extensions.DependencyInjection;

namespace Application.DIResolver
{
    public static class RecommendationRegistration
    {
        /// <summary>
        /// Registers the recommendation engine (stateless singleton) and the "View Recommendation"
        /// service. Needs IDatasetCache, IRequestLedger and IAppClock from Infrastructure, and
        /// IRecommendationExcelExporter (Infrastructure: <c>AddRecommendationExcel()</c>) for the export.
        /// </summary>
        public static IServiceCollection AddRecommendationServices(this IServiceCollection services)
        {
            services.AddDecisionEngine();
            services.AddScoped<IRecommendationService, RecommendationService>();
            return services;
        }
    }
}
