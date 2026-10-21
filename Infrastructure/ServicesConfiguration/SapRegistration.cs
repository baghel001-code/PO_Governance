using Application.DIResolver;
using Application.Interfaces.V1.Sap;
using Domain.Entities.AppSettings;
using Infrastructure.Authentication;
using Infrastructure.Middleware;
using Infrastructure.Repositories;
using Infrastructure.Utilitys;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Infrastructure.ServicesConfiguration
{
    public static class SapInterfaceRegistration
    {
        /// <summary>
        /// The SAP R/3 interface (/api/sap/v1, spec v1.4): SapInterface settings, the PRO_SAP_* repository (SQL Server; with
        /// PostgreSQL storage a repository that answers 503 "SAP interface requires SQL Server storage"), the Application
        /// services (<see cref="SapRegistration.AddSapServices"/>) and the <c>SapBasic</c> authentication scheme.
        /// Call after <see cref="InfrastructureRegistration.AddInfrastructure"/>.
        /// </summary>
        public static IServiceCollection AddSapInterface(this IServiceCollection services, IConfiguration configuration)
        {
            services.Configure<SapInterfaceSettings>(configuration.GetSection(SapInterfaceSettings.SectionName));
            var storage = (configuration[PoGovSettings.SectionName + ":Storage"] ?? "").Trim().ToLowerInvariant();
            if (storage is "" or "sqlserver" or "mssql")
                services.AddSingleton<ISapRepository>(sp => new SapSqlServerRepository(sp.GetRequiredService<DapperHelper>()));
            else
                services.AddSingleton<ISapRepository, SapUnavailableRepository>();
            services.AddSapServices();
            services.AddAuthentication()
                .AddScheme<AuthenticationSchemeOptions, SapBasicAuthenticationHandler>(SapBasicAuthenticationHandler.SchemeName, _ => { });
            return services;
        }

        /// <summary>Trace id, port / IP allow-list / enabled / storage / readiness checks and SAP error bodies on /api/sap/*. Add right after UseApiExceptionHandling.</summary>
        public static IApplicationBuilder UseSapInterface(this IApplicationBuilder app) => app.UseMiddleware<SapInterfaceMiddleware>();
    }
}
