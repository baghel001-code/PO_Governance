using System.Net;
using Application.Interfaces.V1;
using Domain.Constants;
using Domain.Entities.AppSettings;
using Infrastructure.Authentication;
using Infrastructure.Middleware;
using Infrastructure.Repositories;
using Infrastructure.Services.V1;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Infrastructure.ServicesConfiguration
{
    public static class InfrastructureRegistration
    {
        /// <summary>
        /// Settings (PoGovSettings section), PostgreSQL store, clock, password hasher, dataset cache,
        /// AI client (named HttpClient with outbound_proxy), backups (+ nightly loop), start-up
        /// maintenance and the session-token authentication scheme.
        /// </summary>
        public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
        {
            services.Configure<PoGovSettings>(configuration.GetSection(PoGovSettings.SectionName));

            services.AddSingleton<IStore>(sp =>
            {
                var cfg = sp.GetRequiredService<IOptions<PoGovSettings>>().Value;
                var cs = !string.IsNullOrWhiteSpace(cfg.ConnectionString) ? cfg.ConnectionString : configuration.GetConnectionString("PoGov");
                if (string.IsNullOrWhiteSpace(cs))
                    throw new InvalidOperationException("No database connection string: set ConnectionStrings:PoGov (or PoGovSettings:ConnectionString).");
                return new PgStore(cs, sp.GetService<ILogger<PgStore>>());
            });
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

        /// <summary>mustChange → 403 (and the optional X-VMM check). Add after UseAuthentication.</summary>
        public static IApplicationBuilder UseMustChangePassword(this IApplicationBuilder app) => app.UseMiddleware<MustChangePasswordMiddleware>();
    }
}
