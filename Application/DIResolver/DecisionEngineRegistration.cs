using DecisionEngine.Analytics.Ai;
using DecisionEngine.Analytics.AttrAnalysis;
using DecisionEngine.Analytics.Help;
using DecisionEngine.Analytics.Insights;
using DecisionEngine.Grc;
using DecisionEngine.Recommendation;
using DecisionEngine.Tool1;
using DecisionEngine.Tool2;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Application.DIResolver
{
    /// <summary>
    /// Registers the DecisionEngine façades (the core business-logic layer has no DI dependency itself). All engines are
    /// stateless and thread-safe, so they are singletons. Idempotent (TryAdd): every area registration calls it.
    /// </summary>
    public static class DecisionEngineRegistration
    {
        public static IServiceCollection AddDecisionEngine(this IServiceCollection services)
        {
            services.TryAddSingleton<IRecommendationEngine, RecommendationEngine>();
            services.TryAddSingleton<ITool1DecisionEngine>(sp => new Tool1DecisionEngine(sp.GetRequiredService<IRecommendationEngine>()));
            services.TryAddSingleton<ITool2AssessmentEngine>(sp => new Tool2AssessmentEngine(sp.GetRequiredService<IRecommendationEngine>()));
            services.TryAddSingleton<ISeasonSelectionEngine, SeasonSelectionEngine>();
            services.TryAddSingleton<IGrcBacktestEngine>(sp => new GrcBacktester(sp.GetRequiredService<ISeasonSelectionEngine>()));
            services.TryAddSingleton<IAttributeAnalysisEngine, AttributeAnalysisEngine>();
            services.TryAddSingleton<IInsightsEngine, InsightsEngine>();
            services.TryAddSingleton<IHelpContentEngine, HelpContentEngine>();
            services.TryAddSingleton<IAiAssistantEngine, AiAssistantEngine>();
            services.TryAddSingleton<DecisionEngine.Sap.ISapDecisionEngine>(sp =>
                new DecisionEngine.Sap.SapDecisionEngine(sp.GetRequiredService<ITool1DecisionEngine>()));
            return services;
        }
    }
}
