using System.Globalization;
using Admin.Common;
using Domain.Constants;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.Extensions.Options;

namespace Admin.Auth
{
    /// <summary>
    /// QPS CookieValidator. Runs on every HTTP request that carries the auth cookie (page loads, the Blazor connection,
    /// keep-alive): enforces the absolute lifetime, refuses a session already found ended (<see cref="EndedSessions"/>),
    /// and at most every AppConfigurationSettings:UserRevalidateInSeconds asks POGovApi whether the session is still live
    /// (signed in on another device, roles changed or deactivated by the admin, idle). A refused cookie is cleared and the
    /// sign-in page says why (?reason=…).
    /// </summary>
    internal static class CookieValidator
    {
        private const string LastCheckedKey = ".pogov.checked";
        /// <summary>HttpContext.Items key: why the cookie was refused (added to the sign-in redirect as ?reason=).</summary>
        public const string EndReasonItem = "pogov.portal.end";

        public static async Task ValidatePrincipalAsync(CookieValidatePrincipalContext context)
        {
            if (context.Principal is not { Identity.IsAuthenticated: true } principal) return;
            var services = context.HttpContext.RequestServices;
            var portal = services.GetRequiredService<IOptions<PortalSettings>>().Value;
            var app = services.GetRequiredService<IOptions<AppConfigurationSettings>>().Value;
            var now = services.GetRequiredService<TimeProvider>().GetUtcNow();
            var token = principal.FindFirst(CustomClaimTypes.SessionToken)?.Value;

            if (PortalAuthService.AuthTime(principal) is not { } since
                || (portal.AbsoluteLifetimeHours > 0 && now - since >= TimeSpan.FromHours(portal.AbsoluteLifetimeHours)))
            {
                await RejectAsync(context, SessionEndReasons.Expired);
                return;
            }
            if (services.GetRequiredService<EndedSessions>().TryGet(token, out var ended))
            {
                await RejectAsync(context, ended);
                return;
            }
            if (context.Properties.Items.TryGetValue(LastCheckedKey, out var raw)
                && long.TryParse(raw, NumberStyles.None, CultureInfo.InvariantCulture, out var unix)
                && now - DateTimeOffset.FromUnixTimeSeconds(unix) < TimeSpan.FromSeconds(Math.Max(app.UserRevalidateInSeconds, 15)))
                return;

            string? reason;
            try
            {
                reason = await services.GetRequiredService<PortalAuthService>().CheckSessionAsync(token, context.HttpContext.RequestAborted);
            }
            catch (Exception ex) when (!context.HttpContext.RequestAborted.IsCancellationRequested)
            {
                // API down: let this request through and try again on the next one (open pages give up after 5 failures)
                services.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(CookieValidator))
                    .LogWarning(ex, "Session check failed; trying again on the next request");
                return;
            }
            if (reason != null)
            {
                services.GetRequiredService<EndedSessions>().Add(token, reason);
                await RejectAsync(context, reason);
                return;
            }
            context.Properties.Items[LastCheckedKey] = now.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);
            context.ShouldRenew = true;
        }

        private static async Task RejectAsync(CookieValidatePrincipalContext context, string reason)
        {
            context.HttpContext.Items[EndReasonItem] = reason;
            context.RejectPrincipal();
            await context.HttpContext.SignOutAsync(context.Scheme.Name);
        }

        /// <summary>Cookie events: APIs get a status code; pages go to the sign-in page, with the reason when the cookie was just refused.</summary>
        public static Task RedirectOrStatus(RedirectContext<CookieAuthenticationOptions> ctx, int status)
        {
            if (ctx.Request.Path.StartsWithSegments("/account/keepalive") || ctx.Request.Path.StartsWithSegments("/_blazor"))
                ctx.Response.StatusCode = status;
            else if (ctx.HttpContext.Items[EndReasonItem] is string reason)
                ctx.Response.Redirect(Microsoft.AspNetCore.WebUtilities.QueryHelpers.AddQueryString(ctx.RedirectUri, "reason", reason));
            else
                ctx.Response.Redirect(ctx.RedirectUri);
            return Task.CompletedTask;
        }
    }
}
