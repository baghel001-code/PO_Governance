using System.Security.Claims;
using System.Text.RegularExpressions;
using Domain.Constants;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;

namespace Admin.Auth
{
    /// <summary>
    /// Sign-in completion, sign-out, password change, keep-alive and the "/" start page as plain HTTP endpoints (QPS
    /// AccountEndpoints): only an HTTP response can set or clear the auth cookie.
    /// <para>
    /// The sign-in page (Login.razor, interactive) checks the CAPTCHA and the credentials (through POGovApi) and gets a
    /// one-time ticket; auth.js then posts it with an antiforgery token to <see cref="CompleteLoginPath"/>, which sets the
    /// cookie. There is no endpoint that takes a password, so the CAPTCHA cannot be skipped.
    /// </para>
    /// </summary>
    public static class AccountEndpoints
    {
        public const string LoginPath = "/Account/Login";
        public const string ChangePasswordPath = "/Account/ChangePassword";
        public const string SavePasswordPath = "/Account/SavePassword";
        // Lower-case paths that are not page routes (routing ignores case: /account/logout would collide with a page)
        public const string CompleteLoginPath = "/account/complete-login";
        public const string SignOutPath = "/account/sign-out";
        public const string AntiforgeryTokenPath = "/account/antiforgery-token";
        public const string KeepAlivePath = "/account/keepalive";
        /// <summary>Bump when wwwroot/js/auth.js changes (browsers cache import() modules hard).</summary>
        public const string AuthScriptUrl = "./js/auth.js?v=2026102801";

        public static IEndpointRouteBuilder MapAccountEndpoints(this IEndpointRouteBuilder app)
        {
            // Start page: staff (admin / planner) → Data Setup, everyone else → New Request.
            app.MapGet("/", (HttpContext http) => Results.Redirect(StartPage(http.User))).RequireAuthorization();

            app.MapPost(CompleteLoginPath, CompleteLoginAsync).AllowAnonymous().DisableAntiforgery();
            app.MapPost(SignOutPath, SignOutAsync).AllowAnonymous().DisableAntiforgery();
            app.MapGet(AntiforgeryTokenPath, GetAntiforgeryToken).AllowAnonymous();
            app.MapPost(KeepAlivePath, KeepAliveAsync).RequireAuthorization().DisableAntiforgery();
            app.MapPost(SavePasswordPath, SavePasswordAsync).RequireAuthorization().DisableAntiforgery();
            return app;
        }

        /// <summary>
        /// Requests that must not reach a Blazor page: already signed in and opening the sign-in page → go on;
        /// signed in with a password that must be changed → the change-password page.
        /// </summary>
        public static IApplicationBuilder UseAccountRedirects(this IApplicationBuilder app) => app.Use(async (http, next) =>
        {
            var path = http.Request.Path;
            var user = http.User;
            if (user.Identity?.IsAuthenticated == true && HttpMethods.IsGet(http.Request.Method))
            {
                if (path.Equals(LoginPath, StringComparison.OrdinalIgnoreCase))
                {
                    http.Response.Redirect(SafeNext(http.Request.Query["next"]));
                    return;
                }
                var mustChange = user.FindFirst(CustomClaimTypes.MustChange)?.Value == "true";
                if (mustChange && !path.StartsWithSegments("/Account") && !path.StartsWithSegments("/_framework")
                    && !path.StartsWithSegments("/_blazor") && !path.StartsWithSegments("/_content") && !Path.HasExtension(path.Value))
                {
                    http.Response.Redirect(ChangePasswordPath + "?forced=1&next=" + Uri.EscapeDataString(path + http.Request.QueryString));
                    return;
                }
            }
            await next();
        });

        /// <summary>
        /// The ticket from the sign-in page → the auth cookie. Antiforgery stops "login CSRF" (another site posting a ticket
        /// of its own account to sign the victim in as the attacker).
        /// </summary>
        private static async Task<IResult> CompleteLoginAsync(HttpContext http, IAntiforgery antiforgery, LoginTicketStore tickets,
                                                              PortalAuthService auth, LoginNotices notices, ILoggerFactory lf)
        {
            if (!await antiforgery.IsRequestValidAsync(http)) return Results.BadRequest();
            var form = await http.Request.ReadFormAsync(http.RequestAborted);
            var next = SafeNext(form["next"]);
            var ticket = tickets.Redeem(form["ticket"]);
            if (ticket == null) return Results.LocalRedirect(LoginPath + "?error=expired&next=" + Uri.EscapeDataString(next));
            var res = await auth.CompleteSignInAsync(http, ticket.SessionToken, ticket.RememberMe);
            if (!res.Ok) return Results.LocalRedirect(LoginPath + "?error=unavailable&next=" + Uri.EscapeDataString(next));
            // shown once on the first page: "you were signed in on another device; that session has been ended"
            if (ticket.ReplacedOtherSession) notices.Add(ticket.SessionToken, LoginNotices.DuplicateLogin);
            lf.CreateLogger("Auth").LogInformation("Signed in {User}{Replaced}", http.User.Identity?.Name ?? "",
                ticket.ReplacedOtherSession ? " (the session on another device was ended)" : "");
            http.Response.Headers.CacheControl = "no-store";
            return Results.LocalRedirect(res.MustChange ? ChangePasswordPath + "?forced=1&next=" + Uri.EscapeDataString(next) : next);
        }

        /// <summary>
        /// POST + antiforgery (an &lt;img src=…&gt; on another site can't sign users out). Ends the API session (unless the API
        /// already ended it), clears the cookie and shows the reason on the sign-in page.
        /// </summary>
        private static async Task<IResult> SignOutAsync(HttpContext http, IAntiforgery antiforgery, PortalAuthService auth, EndedSessions ended, ILoggerFactory lf)
        {
            if (!await antiforgery.IsRequestValidAsync(http)) return Results.BadRequest();
            var form = await http.Request.ReadFormAsync(http.RequestAborted);
            var reason = SignOutReasons.Normalize(form["reason"]);
            var token = http.User.FindFirst(CustomClaimTypes.SessionToken)?.Value;
            ended.Add(token, reason == SignOutReasons.SignedOut ? SessionEndReasons.Expired : reason);   // a copy of this cookie stops working at once
            try
            {
                await auth.SignOutAsync(http);
            }
            catch (Exception ex) when (!http.RequestAborted.IsCancellationRequested)
            {
                lf.CreateLogger("Auth").LogWarning(ex, "Could not end the API session; signing out anyway");
                await http.SignOutAsync(AppConstants.PortalAuthScheme);
            }
            http.Response.Headers.CacheControl = "no-store";
            http.Response.Headers["Clear-Site-Data"] = "\"cache\"";   // drop cached signed-in pages
            return Results.LocalRedirect(LoginPath + "?reason=" + reason);
        }

        /// <summary>
        /// An interactive page's &lt;AntiforgeryToken /&gt; is only filled in when it was prerendered, so auth.js asks here right
        /// before posting. Other sites can't read this answer (same-origin policy), so they still can't forge the POST.
        /// </summary>
        private static IResult GetAntiforgeryToken(HttpContext http, IAntiforgery antiforgery)
        {
            var tokens = antiforgery.GetAndStoreTokens(http);
            http.Response.Headers.CacheControl = "no-store";
            return Results.Json(new { field = tokens.FormFieldName, token = tokens.RequestToken });
        }

        /// <summary>
        /// Called by idle-timeout.js while the user is active (QPS SessionKeepAliveController): Blazor interaction runs over
        /// the WebSocket, so without it the sliding cookie and the API session would expire although the user is working.
        /// The cookie check renews the cookie; PO_AUTH_ME counts as activity for the API session.
        /// </summary>
        private static async Task<IResult> KeepAliveAsync(HttpContext http, PortalAuthService auth)
        {
            http.Response.Headers.CacheControl = "no-store";
            var token = http.User.FindFirst(CustomClaimTypes.SessionToken)?.Value;
            try
            {
                return await auth.CheckSessionAsync(token, http.RequestAborted) == null ? Results.NoContent() : Results.Unauthorized();
            }
            catch (SessionCheckUnavailableException)
            {
                return Results.NoContent();
            }
        }

        private static async Task<IResult> SavePasswordAsync(HttpContext http, PortalAuthService auth, IAntiforgery antiforgery)
        {
            if (!await antiforgery.IsRequestValidAsync(http)) return Results.Redirect(ChangePasswordPath);
            var form = await http.Request.ReadFormAsync();
            var next = SafeNext(form["Next"]);
            var forced = form["Forced"] == "1";
            var minLength = int.TryParse(form["MinLength"], out var n) && n > 0 ? n : 8;
            var newPassword = form["New"].ToString();
            var back = ChangePasswordPath + (forced ? "?forced=1&" : "?");

            if (newPassword.Length < minLength || !Regex.IsMatch(newPassword, "[A-Za-z]") || !Regex.IsMatch(newPassword, @"\d") || newPassword == "1234")
                return Results.Redirect(back + Query(next, "The new password does not meet the rules below."));
            if (newPassword != form["Confirm"].ToString())
                return Results.Redirect(back + Query(next, "The two new passwords do not match."));

            var token = http.User.FindFirst(CustomClaimTypes.SessionToken)?.Value ?? "";
            var error = await auth.ChangePasswordAsync(http, token, form["Old"].ToString(), newPassword);
            return error == null ? Results.Redirect(next) : Results.Redirect(back + Query(next, error));
        }

        private static string Query(string next, string error) =>
            "next=" + Uri.EscapeDataString(next) + "&error=" + Uri.EscapeDataString(error.Length > 400 ? error[..400] : error);

        /// <summary>Only local paths (no //host, no back into /Account); default "/".</summary>
        public static string SafeNext(string? next) =>
            !string.IsNullOrEmpty(next) && next.StartsWith('/') && !next.StartsWith("//") && !next.StartsWith("/\\")
            && !next.StartsWith("/Account", StringComparison.OrdinalIgnoreCase) ? next : "/";

        public static string StartPage(ClaimsPrincipal user) =>
            user.IsInRole(Roles.Admin) || user.IsInRole(Roles.Planner) ? "/planner/data" : "/category/request";
    }
}
