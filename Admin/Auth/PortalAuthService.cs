using System.Security.Claims;
using System.Text.Json;
using Admin.Interfaces;
using Domain.Constants;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;

namespace Admin.Auth
{
    /// <summary>
    /// Signs people in to the portal: checks the password with POGovApi (which keeps the session in
    /// app_sessions, exactly like v1.1.4) and stores the returned session token in the portal's auth
    /// cookie, so every later API call is made as that person.
    /// </summary>
    public sealed class PortalAuthService
    {
        private readonly IAPICallService _api;

        public PortalAuthService(IAPICallService api) => _api = api;

        public sealed record SignInResult(bool Ok, string? Error, bool MustChange);

        public async Task<SignInResult> SignInAsync(HttpContext http, string email, string password)
        {
            var res = await _api.PostAnonymousAsync<JsonElement>("api/v1/auth/login", new { email = email.Trim(), password });
            if (!res.IsSuccess)
                return new(false, string.IsNullOrWhiteSpace(res.ResponseMessage) ? "Sign in failed" : res.ResponseMessage, false);
            var token = Str(res.Data, "token");
            if (string.IsNullOrEmpty(token)) return new(false, "Sign in failed", false);
            var principal = await BuildPrincipalAsync(token);
            if (principal == null) return new(false, "Sign in failed", false);
            await http.SignInAsync(AppConstants.PortalAuthScheme, principal, new AuthenticationProperties { IsPersistent = false });
            return new(true, null, principal.FindFirst(CustomClaimTypes.MustChange)?.Value == "true");
        }

        /// <summary>Re-reads the user from the API (after a password change the mustChange flag clears).</summary>
        public async Task<bool> RefreshAsync(HttpContext http, string token)
        {
            var principal = await BuildPrincipalAsync(token);
            if (principal == null) return false;
            await http.SignInAsync(AppConstants.PortalAuthScheme, principal);
            return true;
        }

        public async Task SignOutAsync(HttpContext http)
        {
            var token = http.User.FindFirst(CustomClaimTypes.SessionToken)?.Value;
            if (!string.IsNullOrEmpty(token))
                await _api.SendWithTokenAsync<JsonElement>(HttpMethod.Post, "api/v1/auth/logout", token, new { });
            await http.SignOutAsync(AppConstants.PortalAuthScheme);
        }

        public async Task<ClaimsPrincipal?> BuildPrincipalAsync(string token)
        {
            var me = await _api.SendWithTokenAsync<JsonElement>(HttpMethod.Get, "api/v1/auth/me", token);
            if (!me.IsSuccess) return null;
            var user = me.Data.ValueKind == JsonValueKind.Object && me.Data.TryGetProperty("user", out var u) ? u : me.Data;
            return FromUserJson(user, token);
        }

        public static ClaimsPrincipal FromUserJson(JsonElement user, string token)
        {
            var email = Str(user, "email");
            var claims = new List<Claim>
            {
                new(ClaimTypes.NameIdentifier, email),
                new(ClaimTypes.Name, Str(user, "name") is { Length: > 0 } n ? n : email),
                new(ClaimTypes.Email, email),
                new(CustomClaimTypes.Email, email),
                new(CustomClaimTypes.SessionToken, token),
                new(CustomClaimTypes.MustChange, Bool(user, "mustChange") ? "true" : "false"),
            };
            if (user.ValueKind == JsonValueKind.Object && user.TryGetProperty("roles", out var roles) && roles.ValueKind == JsonValueKind.Array)
                foreach (var r in roles.EnumerateArray())
                    if (r.GetString() is { Length: > 0 } role) claims.Add(new Claim(ClaimTypes.Role, role));
            return new ClaimsPrincipal(new ClaimsIdentity(claims, AppConstants.PortalAuthScheme));
        }

        private static string Str(JsonElement e, string name)
        {
            if (e.ValueKind != JsonValueKind.Object) return "";
            foreach (var p in e.EnumerateObject())
                if (string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase) && p.Value.ValueKind == JsonValueKind.String)
                    return p.Value.GetString() ?? "";
            return "";
        }

        private static bool Bool(JsonElement e, string name)
        {
            if (e.ValueKind != JsonValueKind.Object) return false;
            foreach (var p in e.EnumerateObject())
                if (string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase))
                    return p.Value.ValueKind == JsonValueKind.True;
            return false;
        }
    }
}
