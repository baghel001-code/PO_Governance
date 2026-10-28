using System.Security.Claims;
using System.Text.RegularExpressions;
using Domain.Constants;
using Microsoft.AspNetCore.Antiforgery;

namespace Admin.Auth
{
    /// <summary>
    /// Sign-in, password change, sign-out and the "/" start page as plain HTTP endpoints.
    /// <para>
    /// They set the auth cookie and answer with HTTP redirects. Doing this inside a Blazor page during server-side
    /// rendering only works through <c>NavigationManager.NavigateTo</c>, which in .NET 8 throws a
    /// <c>NavigationException</c> on purpose (caught by the framework) — harmless, but Visual Studio stops on it on
    /// every sign-in. The pages (Login.razor, ChangePassword.razor) only render the forms; the forms post here.
    /// </para>
    /// </summary>
    public static class AccountEndpoints
    {
        public const string LoginPath = "/Account/Login";
        public const string SignInPath = "/Account/SignIn";
        public const string ChangePasswordPath = "/Account/ChangePassword";
        public const string SavePasswordPath = "/Account/SavePassword";

        public static IEndpointRouteBuilder MapAccountEndpoints(this IEndpointRouteBuilder app)
        {
            // Start page: staff (admin / planner) → Data Setup, everyone else → New Request.
            app.MapGet("/", (HttpContext http) => Results.Redirect(StartPage(http.User))).RequireAuthorization();

            app.MapPost(SignInPath, SignInAsync).AllowAnonymous().DisableAntiforgery();
            app.MapPost(SavePasswordPath, SavePasswordAsync).RequireAuthorization().DisableAntiforgery();

            async Task<IResult> SignOut(HttpContext http, PortalAuthService auth)
            {
                await auth.SignOutAsync(http);
                return Results.Redirect(LoginPath);
            }
            app.MapGet("/Account/Logout", SignOut);
            app.MapPost("/Account/Logout", SignOut);
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

        private static async Task<IResult> SignInAsync(HttpContext http, PortalAuthService auth, IAntiforgery antiforgery)
        {
            if (!await antiforgery.IsRequestValidAsync(http)) return Results.Redirect(LoginPath);
            var form = await http.Request.ReadFormAsync();
            var login = (form.ContainsKey("LoginId") ? form["LoginId"] : form["Email"]).ToString().Trim();
            var password = form["Password"].ToString();
            var next = SafeNext(form["Next"]);
            if (login.Length == 0 || password.Length == 0)
                return Back(LoginPath, next, "Enter your Employee ID and password.", login);

            var res = await auth.SignInAsync(http, login, password);
            if (!res.Ok) return Back(LoginPath, next, res.Error ?? "Sign in failed", login);
            return Results.Redirect(res.MustChange
                ? ChangePasswordPath + "?forced=1&next=" + Uri.EscapeDataString(next)
                : next);
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

        private static IResult Back(string page, string next, string error, string? login = null) =>
            Results.Redirect(page + "?" + Query(next, error) + (string.IsNullOrEmpty(login) ? "" : "&login=" + Uri.EscapeDataString(login.Length > 100 ? login[..100] : login)));

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
