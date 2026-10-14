using Application.Interfaces.V1.Tool1;
using Application.Services.V1.Tool1;
using Microsoft.Extensions.DependencyInjection;

namespace Application.DIResolver
{
    public static class Tool1Registration
    {
        /// <summary>
        /// Registers Tool 1 ("✏️ New Request"): the stateless engines / flow (singletons) and the scoped
        /// <see cref="ITool1Service"/>. Needs IDatasetCache, IRequestLedger and IAppClock (Infrastructure)
        /// and the recommendation engine (<c>AddRecommendationServices()</c>, for the STR caution).
        /// </summary>
        public static IServiceCollection AddTool1Services(this IServiceCollection services)
        {
            services.AddSingleton(new Tool1Settings());
            services.AddSingleton<Tool1EngineB>();
            services.AddSingleton<Tool1EngineA>();
            services.AddSingleton<Tool1FormOptions>();
            services.AddSingleton<Tool1Flow>();
            services.AddScoped<ITool1Service, Tool1Service>();
            return services;
        }
    }
}
