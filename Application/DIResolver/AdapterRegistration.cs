using Application.Interfaces.V1.Analytics;
using Application.Services.V1.Adapters;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Application.DIResolver
{
    /// <summary>Connects the analytics ports to the platform services (request history, AI client, Tool 2 runs).</summary>
    public static class AdapterRegistration
    {
        public static IServiceCollection AddAnalyticsAdapters(this IServiceCollection services)
        {
            services.AddScoped<IRequestHistorySource, RequestHistorySource>();
            services.AddScoped<IAiGateway, AiGatewayAdapter>();
            services.TryAddScoped<ITool2ResultSource, EmptyTool2ResultSource>();
            return services;
        }
    }
}
