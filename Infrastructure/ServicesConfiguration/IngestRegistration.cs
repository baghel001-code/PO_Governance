using System.Text;
using Application.Interfaces.V1.Ingest;
using Infrastructure.Services.V1.Excel;
using Microsoft.Extensions.DependencyInjection;

namespace Infrastructure.ServicesConfiguration
{
    public static class IngestRegistration
    {
        /// <summary>Workbook reader for the Data Setup uploads (ExcelDataReader + CSV).</summary>
        public static IServiceCollection AddIngestInfrastructure(this IServiceCollection services)
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            services.AddSingleton<IWorkbookReader, ExcelWorkbookReader>();
            return services;
        }
    }
}
