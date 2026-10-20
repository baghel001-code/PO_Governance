using System.Net;
using Application.Interfaces.V1;
using Domain.Constants;
using Domain.Entities.AppSettings;
using Infrastructure.Authentication;
using Infrastructure.Middleware;
using Infrastructure.Repositories;
using Infrastructure.Services.V1;
using Infrastructure.Utilitys;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MyNewEncDec;

namespace Infrastructure.ServicesConfiguration
{
    public static class InfrastructureRegistration
    {
        /// <summary>
        /// Settings (PoGovSettings section), the store (SQL Server or PostgreSQL, PoGovSettings:Storage), clock, password hasher, dataset cache,
        /// AI client (named HttpClient with outbound_proxy), backups (+ nightly loop), start-up
        /// maintenance and the session-token authentication scheme.
        /// </summary>
        public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
        {
            services.Configure<PoGovSettings>(configuration.GetSection(PoGovSettings.SectionName));

            // PoGovSettings:Storage = "sqlserver" (default: stored procedures, ConnectionStrings:DefaultConnection)
            //                       | "postgres" (v1.1.4 schema, ConnectionStrings:PoGov — fallback / migration source)
            var storage = (configuration[PoGovSettings.SectionName + ":Storage"] ?? "").Trim().ToLowerInvariant();
            if (storage is "postgres" or "postgresql" or "pg")
                services.AddSingleton<IStore>(sp =>
                {
                    var cfg = sp.GetRequiredService<IOptions<PoGovSettings>>().Value;
                    var cs = !string.IsNullOrWhiteSpace(cfg.ConnectionString) ? cfg.ConnectionString : configuration.GetConnectionString("PoGov");
                    if (string.IsNullOrWhiteSpace(cs))
                        throw new InvalidOperationException("No database connection string: set ConnectionStrings:PoGov (or PoGovSettings:ConnectionString).");
                    return new PgStore(cs, sp.GetService<ILogger<PgStore>>());
                });
            else if (storage is "" or "sqlserver" or "mssql")
            {
                services.AddSingleton<New_Enc_Dec>();
                services.AddSingleton(sp => new DapperHelper(configuration, sp.GetRequiredService<New_Enc_Dec>(), sp.GetRequiredService<ILogger<DapperHelper>>()));
                services.AddSingleton<IStore>(sp => new SqlServerStore(sp.GetRequiredService<DapperHelper>(), sp.GetService<ILogger<SqlServerStore>>(),
                    configuration.GetValue<bool>(PoGovSettings.SectionName + ":SqlServerCreateSchema"))
                {
                    ApiMasterBaseUrl = configuration["ApiMaster:BaseUrl"]
                });
            }
            else
                throw new InvalidOperationException($"PoGovSettings:Storage = '{storage}': use 'sqlserver' or 'postgres'.");
            // API master list (GetAllApi): API_MASTER via PRO_GET_ALL_API; without SQL Server, the 04_api_master.sql seed
            services.AddSingleton<IAPI>(sp => new APIService(sp.GetService<DapperHelper>(), configuration, sp.GetService<ILogger<APIService>>()));
            services.AddSingleton<IAppClock, AppClock>();
            services.AddSingleton<IPasswordHasher, PasswordHasher>();
            services.AddSingleton<IDatasetCache, DatasetCache>();
            services.AddSingleton<IBackupService, BackupService>();
            services.AddSingleton<IAiClient, AiClient>();

            services.AddHttpClient(AiClient.HttpClientName, c => c.Timeout = TimeSpan.FromSeconds(90))
                .ConfigurePrimaryHttpMessageHandler(sp =>
                {
                    var cfg = sp.GetRequiredService<IOptions<PoGovSettings>>().Value;
                    var h = new HttpClientHandler();
                    if (!string.IsNullOrWhiteSpace(cfg.OutboundProxy))
                    {
                        h.Proxy = new WebProxy(cfg.OutboundProxy);
                        h.UseProxy = true;
                    }
                    return h;
                });

            services.AddHostedService<StartupService>();
            services.AddHostedService<BackupBackgroundService>();

            services.AddAuthentication(AppConstants.AuthScheme)
                .AddScheme<AuthenticationSchemeOptions, SessionTokenAuthenticationHandler>(AppConstants.AuthScheme, _ => { });
            return services;
        }

        /// <summary>Exception → ApiResponse mapping. Add first in the pipeline.</summary>
        public static IApplicationBuilder UseApiExceptionHandling(this IApplicationBuilder app) => app.UseMiddleware<ExceptionMiddleware>();

        /// <summary>Static API token (QPS StaticTokenMiddleware, JwtSettings:SecretKey) on api/v1/* except Health. Add before UseAuthentication.</summary>
        public static IApplicationBuilder UseStaticToken(this IApplicationBuilder app) => app.UseMiddleware<StaticTokenMiddleware>();

        /// <summary>
        /// Portal side (QPS AddInfrastructure(useProxyValue …)): <see cref="IAPICall"/> = <see cref="APICallService"/>, scoped, with the
        /// <see cref="ISessionTokenAccessor"/> the portal registers (signed-in user's session token → X-Session-Token).
        /// </summary>
        public static IServiceCollection AddApiCallService(this IServiceCollection services, int useProxyValue)
        {
            services.AddScoped<IAPICall>(sp => new APICallService(useProxyValue, sp.GetService<ISessionTokenAccessor>(),
                sp.GetService<ILoggerFactory>()?.CreateLogger<APICallService>()));
            return services;
        }

        /// <summary>mustChange → 403 (and the optional X-VMM check). Add after UseAuthentication.</summary>
        public static IApplicationBuilder UseMustChangePassword(this IApplicationBuilder app) => app.UseMiddleware<MustChangePasswordMiddleware>();
    }
}
