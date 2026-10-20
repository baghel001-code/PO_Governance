using Admin.Auth;
using Admin.Common;
using Admin.Services;
using Application.Interfaces.V1;
using Domain.Constants;
using Infrastructure.ServicesConfiguration;
using Microsoft.AspNetCore.Components.Authorization;

namespace Admin.ServiceRegistration
{
    public static class PortalRegistration
    {
        public static IServiceCollection AddPortal(this IServiceCollection services, IConfiguration config, IWebHostEnvironment env)
        {
            services.Configure<PortalSettings>(config.GetSection("PortalSettings"));
            services.Configure<AppConfigurationSettings>(config.GetSection("AppConfigurationSettings"));
            var settings = config.GetSection("PortalSettings").Get<PortalSettings>() ?? new PortalSettings();

            // QPS: IAPICall = Infrastructure APICallService (task name → API_MASTER row); the user's session token from the auth state.
            services.AddApiCallService(config.GetValue<int>("AppConfigurationSettings:ProxyValue"));
            services.AddScoped<ISessionTokenAccessor, SessionTokenAccessor>();

            services.AddAuthentication(AppConstants.PortalAuthScheme)
                .AddCookie(AppConstants.PortalAuthScheme, o =>
                {
                    o.Cookie.Name = AppConstants.PortalCookie;
                    o.Cookie.HttpOnly = true;
                    o.Cookie.SameSite = SameSiteMode.Strict;
                    o.Cookie.SecurePolicy = env.IsDevelopment() ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
                    o.LoginPath = "/Account/Login";
                    o.LogoutPath = "/Account/Logout";
                    o.AccessDeniedPath = "/Account/AccessDenied";
                    o.ExpireTimeSpan = TimeSpan.FromHours(settings.SessionIdleHours);
                    o.SlidingExpiration = true;
                });
            services.AddAuthorization();
            services.AddCascadingAuthenticationState();
            services.AddScoped<AuthenticationStateProvider, ApiRevalidatingAuthStateProvider>();

            services.AddScoped<PortalAuthService>();
            services.AddScoped<UserSessionState>();
            services.AddScoped<ServerStatusService>();
            services.AddScoped<FileDownloadService>();
            return services;
        }
    }
}
