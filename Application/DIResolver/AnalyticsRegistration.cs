using Application.Interfaces.V1.Analytics;
using Application.Services.V1.Analytics.Ai;
using Application.Services.V1.Analytics.AttrAnalysis;
using Application.Services.V1.Analytics.CatInsights;
using Application.Services.V1.Analytics.Help;
using Application.Services.V1.Analytics.PlannerDashboard;
using Microsoft.Extensions.DependencyInjection;

namespace Application.DIResolver
{
    /// <summary>DI registration of the analytics screens (Attribute Analysis, Category Insights, Planner dashboard, AI assistant, How it works).</summary>
    public static class AnalyticsRegistration
    {
        /// <summary>
        /// Registers <see cref="IAttributeAnalysisService"/>, <see cref="ICategoryInsightsService"/>,
        /// <see cref="IPlannerDashboardService"/>, <see cref="IAiAssistantService"/> and <see cref="IHelpContentService"/>.
        /// Needs IDatasetCache and IAppClock (Infrastructure) plus three ports implemented elsewhere and wired by the host:
        /// <see cref="IRequestHistorySource"/> (platform request history), <see cref="ITool2ResultSource"/> (Tool 2 runs)
        /// and <see cref="IAiGateway"/> (platform AI client).
        /// </summary>
        public static IServiceCollection AddAnalyticsServices(this IServiceCollection services)
        {
            services.AddDecisionEngine();
            services.AddSingleton<IAttributeAnalysisService, AttributeAnalysisService>();
            services.AddSingleton<IHelpContentService, HelpContentService>();
            services.AddScoped<ICategoryInsightsService, CategoryInsightsService>();
            services.AddScoped<IPlannerDashboardService, PlannerDashboardService>();
            services.AddScoped<IAiAssistantService, AiAssistantService>();
            return services;
        }
    }
}
