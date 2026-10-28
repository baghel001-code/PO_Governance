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
            services.AddSingleton<ICaptchaGenerator>(sp => CaptchaGenerator(settings, env, sp.GetRequiredService<ILoggerFactory>().CreateLogger("Captcha")));
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

        /// <summary>
        /// The sign-in CAPTCHA: the company VmmCaptcha.dll (QPS) on Windows, with the built-in generator as fallback; the
        /// built-in one on other systems (GDI+ is Windows-only on .NET 8) or with Captcha:Engine = Builtin; a fixed code
        /// only in Development with DevCaptchaCode (automated UI tests).
        /// </summary>
        public static ICaptchaGenerator CaptchaGenerator(PortalSettings settings, IWebHostEnvironment env, ILogger log)
        {
            if (env.IsDevelopment() && !string.IsNullOrWhiteSpace(settings.DevCaptchaCode))
            {
                log.LogWarning("CAPTCHA: fixed code from PortalSettings:DevCaptchaCode (Development only)");
                return new PngCaptchaGenerator(settings.DevCaptchaCode);
            }
            var builtin = new PngCaptchaGenerator();
            if (!settings.Captcha.UseVmm)
            {
                log.LogInformation("CAPTCHA: built-in generator (PortalSettings:Captcha:Engine = Builtin)");
                return builtin;
            }
            if (!OperatingSystem.IsWindows())
            {
                log.LogWarning("CAPTCHA: VmmCaptcha needs Windows (GDI+); using the built-in generator on this server");
                return builtin;
            }
            log.LogInformation("CAPTCHA: VmmCaptcha.dll ({Length} characters, ±{Angle}°, {Font} {Size}{Bold})", settings.Captcha.LengthOrDefault,
                settings.Captcha.AngleDegrees, settings.Captcha.FontName, settings.Captcha.FontSize, settings.Captcha.Bold ? " bold" : "");
            return new FallbackCaptchaGenerator(new VmmCaptchaGenerator(settings.Captcha), builtin, log);
        }
    }
}
