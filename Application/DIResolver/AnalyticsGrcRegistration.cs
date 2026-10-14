using Application.Interfaces.V1.Analytics;
using Application.Services.V1.Analytics.Grc;
using Microsoft.Extensions.DependencyInjection;

namespace Application.DIResolver
{
    /// <summary>DI registration of the GRC Back Testing + Season Selection services.</summary>
    public static class AnalyticsGrcRegistration
    {
        /// <summary>
        /// Registers <see cref="IGrcBacktestService"/> and <see cref="ISeasonService"/>. Needs IDatasetCache and
        /// IAppClock (Infrastructure) and, for the export endpoint, IGrcExcelExporter
        /// (Infrastructure: <c>AddGrcExcelExport()</c>).
        /// </summary>
        public static IServiceCollection AddGrcAnalyticsServices(this IServiceCollection services)
        {
            services.AddSingleton<ISeasonService, SeasonService>();
            services.AddSingleton<IGrcBacktestService, GrcBacktestService>();
            return services;
        }
    }
}
