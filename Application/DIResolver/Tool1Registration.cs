using Application.Interfaces.V1.Tool1;
using Application.Services.V1.Tool1;
using Microsoft.Extensions.DependencyInjection;

namespace Application.DIResolver
{
    public static class Tool1Registration
    {
        /// <summary>
        /// Registers Tool 1 ("✏️ New Request"): the DecisionEngine façade (<c>ITool1DecisionEngine</c>, singleton) and the scoped
        /// <see cref="ITool1Service"/>. Needs IDatasetCache, IRequestLedger and IAppClock (Infrastructure)
        /// and the recommendation engine (<c>AddRecommendationServices()</c>, for the STR caution).
        /// </summary>
        public static IServiceCollection AddTool1Services(this IServiceCollection services)
        {
            services.AddDecisionEngine();
            services.AddSingleton(new Tool1Settings());
            services.AddScoped<ITool1Service, Tool1Service>();
            return services;
        }
    }
}
