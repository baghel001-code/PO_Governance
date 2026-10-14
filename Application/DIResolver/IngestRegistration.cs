using Application.Interfaces.V1.Ingest;
using Application.Services.V1.Ingest;
using Microsoft.Extensions.DependencyInjection;

namespace Application.DIResolver
{
    public static class IngestRegistration
    {
        /// <summary>Data Setup uploads (IDataIngestService). Needs IWorkbookReader (AddIngestInfrastructure),
        /// IDatasetStore and IDatasetCache registered elsewhere.</summary>
        public static IServiceCollection AddIngestServices(this IServiceCollection services)
        {
            services.AddScoped<IDataIngestService, DataIngestService>();
            return services;
        }
    }
}
