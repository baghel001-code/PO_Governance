using Admin.Auth;
using Admin.Common;
using Admin.Services;
using Application.Interfaces.V1;
using Domain.Constants;
using Infrastructure.ServicesConfiguration;
using Admin.Auth.Captcha;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.DependencyInjection.Extensions;

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
            services.AddHostedService<ApiMasterReloadService>();

            services.AddAuthentication(AppConstants.PortalAuthScheme)
                .AddCookie(AppConstants.PortalAuthScheme, o =>
                {
                    o.Cookie.Name = AppConstants.PortalCookie;
                    o.Cookie.HttpOnly = true;
                    o.Cookie.SameSite = SameSiteMode.Strict;
                    // Secure cookie only over https (PortalSettings:UseHttps); over plain http the browser would not store it.
                    o.Cookie.SecurePolicy = settings.UseHttps ? CookieSecurePolicy.Always : CookieSecurePolicy.SameAsRequest;
                    o.LoginPath = AccountEndpoints.LoginPath;
                    o.ReturnUrlParameter = "next";   // Login.razor / AccountEndpoints read ?next=
                    o.LogoutPath = AccountEndpoints.SignOutPath;
                    o.AccessDeniedPath = "/Account/AccessDenied";
                    // idle timeout (QPS): sliding, renewed by requests and the keep-alive of an active page
                    o.ExpireTimeSpan = TimeSpan.FromMinutes(Math.Max(settings.IdleTimeoutMinutes, 1));
                    o.SlidingExpiration = true;
                    // absolute lifetime, single session, admin changes, idle: checked with POGovApi (QPS CookieValidator)
                    o.Events.OnValidatePrincipal = CookieValidator.ValidatePrincipalAsync;
                    o.Events.OnRedirectToLogin = ctx => CookieValidator.RedirectOrStatus(ctx, StatusCodes.Status401Unauthorized);
                    o.Events.OnRedirectToAccessDenied = ctx => CookieValidator.RedirectOrStatus(ctx, StatusCodes.Status403Forbidden);
                });
            services.AddAuthorization();
            services.AddCascadingAuthenticationState();
            services.AddScoped<AuthenticationStateProvider, ApiRevalidatingAuthStateProvider>();

            services.AddScoped<PortalAuthService>();
            // QPS sign-in security: CAPTCHA, one-time sign-in tickets, ended sessions, login notices, idle tracking per circuit
            services.TryAddSingleton(TimeProvider.System);
            var devCaptcha = env.IsDevelopment() ? settings.DevCaptchaCode : null;
            services.AddSingleton<ICaptchaGenerator>(_ => new PngCaptchaGenerator(devCaptcha));
            services.AddSingleton<CaptchaService>();
            services.AddSingleton<LoginTicketStore>();
            services.AddSingleton<EndedSessions>();
            services.AddSingleton<LoginNotices>();
            services.AddScoped<SessionEndState>();      // per circuit
            services.AddScoped<UserActivityState>();    // per circuit
            services.AddScoped<LoginModeService>();
            services.AddScoped<UserSessionState>();
            services.AddScoped<ServerStatusService>();
            services.AddScoped<FileDownloadService>();
            return services;
        }
    }
}
