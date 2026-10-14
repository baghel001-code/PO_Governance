using Admin.Auth;
using Admin.Common;
using Admin.Interfaces;
using Admin.Services;
using Domain.Constants;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Components.Authorization;

namespace Admin.ServiceRegistration
{
    public static class PortalRegistration
    {
        public static IServiceCollection AddPortal(this IServiceCollection services, IConfiguration config, IWebHostEnvironment env)
        {
            services.Configure<PortalSettings>(config.GetSection("PortalSettings"));
            var settings = config.GetSection("PortalSettings").Get<PortalSettings>() ?? new PortalSettings();

            services.AddHttpClient(AppConstants.ApiClientName, c =>
            {
                c.BaseAddress = new Uri(settings.ApiBaseUrl.EndsWith('/') ? settings.ApiBaseUrl : settings.ApiBaseUrl + "/");
                c.Timeout = TimeSpan.FromSeconds(settings.ApiTimeoutSeconds);
            });

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

            services.AddScoped<IAPICallService, APICallService>();
            services.AddScoped<PortalAuthService>();
            services.AddScoped<UserSessionState>();
            services.AddScoped<ServerStatusService>();
            services.AddScoped<FileDownloadService>();
            return services;
        }
    }
}
