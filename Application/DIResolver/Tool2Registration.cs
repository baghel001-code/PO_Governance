using Application.Interfaces.V1.Tool2;
using Application.Services.V1.Tool2;
using Microsoft.Extensions.DependencyInjection;

namespace Application.DIResolver
{
    public static class Tool2Registration
    {
        /// <summary>
        /// Registers Tool 2 (PO Assessment): <see cref="ITool2Service"/> and the in-memory run cache. Needs
        /// IDatasetCache, IRequestLedger, IAppClock, IRecommendationEngine (<c>AddRecommendationServices()</c>),
        /// <see cref="IRunArchive"/> + <see cref="IDatasetManifestReader"/> (platform runs store) and
        /// <see cref="ITool2ExcelExporter"/> (Infrastructure: <c>AddTool2Excel()</c>).
        /// </summary>
        public static IServiceCollection AddTool2Services(this IServiceCollection services)
        {
            services.AddDecisionEngine();
            services.AddSingleton<Tool2RunCache>();
            services.AddScoped<ITool2Service, Tool2Service>();
            return services;
        }
    }
}
