using System.Globalization;
using System.Security.Claims;
using Application.DTOs.Platform;
using Application.Interfaces.V1;
using Domain.Constants;
using Domain.Entities.Common;
using Domain.Entities.Request;
using Domain.Entities.Response;
using Microsoft.AspNetCore.Authentication;

namespace Admin.Auth
{
    /// <summary>
    /// Signs people in to the portal. The sign-in page checks the CAPTCHA, then <see cref="VerifyAsync"/> asks POGovApi
    /// (PO_AUTH_LOGIN: Employee ID + HRMS password for registered employees; the API keeps the session and ends the user's
    /// other session) and the session token goes, through a one-time ticket, to /account/complete-login, which stores it in
    /// the portal's auth cookie (<see cref="CompleteSignInAsync"/>). Every later API call is made as that person (X-Session-Token).
    /// </summary>
    public sealed class PortalAuthService
    {
        private readonly IAPICall _api;

        public PortalAuthService(IAPICall api) => _api = api;

        public sealed record SignInResult(bool Ok, string? Error, bool MustChange);

        /// <summary>
        /// Checks the credentials with POGovApi only (the portal never calls HRMS). Returns the API's answer (token,
        /// replacedOtherSession) or the error text to show.
        /// </summary>
        public async Task<(LoginResponse? Login, string? Error)> VerifyAsync(string login, string password, CancellationToken ct = default)
        {
            var res = await _api.CallingAPI<ApiDataResponse<LoginResponse>, LoginRequest>(AllApiNames.PO_AUTH_LOGIN,
                new LoginRequest { LoginId = login.Trim(), Password = password }, "", ct);
            if (!res.IsSuccess || string.IsNullOrEmpty(res.Data?.Token))
                return (null, res.responseCode is >= 1000 or 500 or 502 or 503 or 504
                    ? "Sign-in is temporarily unavailable. Please try again in a few minutes."
                    : string.IsNullOrWhiteSpace(res.responseMessage) ? "Sign in failed" : res.responseMessage);
            return (res.Data, null);
        }

        /// <summary>Issues the auth cookie for a verified session token (the HTTP endpoint behind the sign-in page).</summary>
        public async Task<SignInResult> CompleteSignInAsync(HttpContext http, string token, bool rememberMe)
        {
            var me = await MeAsync(token, http.RequestAborted);
            if (!me.IsSuccess || me.Data?.User == null) return new(false, "Sign in failed", false);
            var principal = FromUser(me.Data.User, token, DateTimeOffset.UtcNow, me.Data.IdleTimeoutMinutes);
            await http.SignInAsync(AppConstants.PortalAuthScheme, principal,
                new AuthenticationProperties { IsPersistent = rememberMe, AllowRefresh = true });
            return new(true, null, me.Data.User.MustChange);
        }

        /// <summary>Re-reads the user from the API (after a password change the mustChange flag clears); keeps the sign-in time.</summary>
        public async Task<bool> RefreshAsync(HttpContext http, string token)
        {
            var me = await MeAsync(token, http.RequestAborted);
            if (!me.IsSuccess || me.Data?.User == null) return false;
            var since = AuthTime(http.User) ?? DateTimeOffset.UtcNow;
            var current = await http.AuthenticateAsync(AppConstants.PortalAuthScheme);   // keeps "Keep me signed in"
            await http.SignInAsync(AppConstants.PortalAuthScheme, FromUser(me.Data.User, token, since, me.Data.IdleTimeoutMinutes),
                                   current.Properties ?? new AuthenticationProperties());
            return true;
        }

        /// <summary>PO_AUTH_CHANGE_PASSWORD, then re-issues the cookie (mustChange cleared). Null = success, else the message.</summary>
        public async Task<string?> ChangePasswordAsync(HttpContext http, string token, string oldPassword, string newPassword)
        {
            var res = await _api.CallingAPI<ApiDataResponse<OkResponse>, ChangePasswordRequest>(AllApiNames.PO_AUTH_CHANGE_PASSWORD,
                new ChangePasswordRequest { Old = oldPassword, New = newPassword }, token, http.RequestAborted);
            if (!res.IsSuccess) return string.IsNullOrEmpty(res.responseMessage) ? "Could not change password" : res.responseMessage;
            await RefreshAsync(http, token);
            return null;
        }

        /// <summary>Ends the API session (PO_AUTH_LOGOUT) and clears the cookie.</summary>
        public async Task SignOutAsync(HttpContext http)
        {
            var token = http.User.FindFirst(CustomClaimTypes.SessionToken)?.Value;
            if (!string.IsNullOrEmpty(token))
                await _api.CallingAPI<GenRes, EmptyReq>(AllApiNames.PO_AUTH_LOGOUT, EmptyReq.Instance, token);
            await http.SignOutAsync(AppConstants.PortalAuthScheme);
        }

        /// <summary>PO_AUTH_ME with this token (also counts as activity for the API's idle timeout).</summary>
        public Task<ApiDataResponse<MeResponse>> MeAsync(string token, CancellationToken ct = default) =>
            _api.CallingAPI<ApiDataResponse<MeResponse>, EmptyReq>(AllApiNames.PO_AUTH_ME, EmptyReq.Instance, token, ct);

        /// <summary>
        /// Is the session still live? Null = yes; else why it ended (<see cref="SessionEndReasons"/>: signed in on another
        /// device, changed / deactivated by the admin, idle, expired). Throws <see cref="SessionCheckUnavailableException"/>
        /// when the API could not answer (the caller keeps the user signed in and tries again).
        /// </summary>
        public async Task<string?> CheckSessionAsync(string? token, CancellationToken ct = default)
        {
            if (string.IsNullOrEmpty(token)) return SessionEndReasons.Expired;
            var res = await MeAsync(token, ct);
            if (res.IsSuccess) return null;
            if (res.responseCode == 403) return null;   // must change the password: handled by the layout
            // 401 from the session check = the session ended; a 401 from the static-token check is a configuration problem
            if (res.responseCode == 401 && res.responseMessage != Infrastructure.Middleware.StaticTokenMiddleware.Unauthorized)
                return SessionEndReasons.FromMessage(res.responseMessage) ?? SessionEndReasons.Expired;
            throw new SessionCheckUnavailableException($"{res.responseCode}: {res.responseMessage}");
        }

        public static DateTimeOffset? AuthTime(ClaimsPrincipal user) =>
            long.TryParse(user.FindFirst(CustomClaimTypes.AuthTime)?.Value, NumberStyles.None, CultureInfo.InvariantCulture, out var unix)
                ? DateTimeOffset.FromUnixTimeSeconds(unix) : null;

        public static int IdleMinutes(ClaimsPrincipal user, int fallback) =>
            int.TryParse(user.FindFirst(CustomClaimTypes.IdleMinutes)?.Value, NumberStyles.None, CultureInfo.InvariantCulture, out var m) && m > 0 ? m : fallback;

        public static ClaimsPrincipal FromUser(UserPublicDto user, string token, DateTimeOffset signedInAt, int idleMinutes = 0)
        {
            var email = user.Email ?? "";
            var claims = new List<Claim>
            {
                new(ClaimTypes.NameIdentifier, email),
                new(ClaimTypes.Name, string.IsNullOrEmpty(user.Name) ? email : user.Name),
                new(ClaimTypes.Email, email),
                new(CustomClaimTypes.Email, email),
                new(CustomClaimTypes.SessionToken, token),
                new(CustomClaimTypes.MustChange, user.MustChange ? "true" : "false"),
                new(CustomClaimTypes.LoginType, string.IsNullOrEmpty(user.LoginType) ? "local" : user.LoginType),
                new(CustomClaimTypes.AuthTime, signedInAt.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture)),
            };
            if (idleMinutes > 0) claims.Add(new Claim(CustomClaimTypes.IdleMinutes, idleMinutes.ToString(CultureInfo.InvariantCulture)));
            if (!string.IsNullOrEmpty(user.EmpCode)) claims.Add(new Claim(CustomClaimTypes.EmpCode, user.EmpCode));
            foreach (var role in user.Roles ?? new List<string>())
                if (!string.IsNullOrEmpty(role)) claims.Add(new Claim(ClaimTypes.Role, role));
            return new ClaimsPrincipal(new ClaimsIdentity(claims, AppConstants.PortalAuthScheme));
        }
    }

    /// <summary>POGovApi could not say whether the session is live (down, time-out): not the user's fault.</summary>
    public sealed class SessionCheckUnavailableException : Exception
    {
        public SessionCheckUnavailableException(string message) : base(message) { }
    }
}
