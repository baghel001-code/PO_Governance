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
    /// Signs people in to the portal: checks the password with POGovApi (PO_AUTH_LOGIN; the API keeps the session in
    /// app_sessions, exactly like v1.1.4) and stores the returned session token in the portal's auth cookie, so every
    /// later API call is made as that person (X-Session-Token).
    /// </summary>
    public sealed class PortalAuthService
    {
        private readonly IAPICall _api;

        public PortalAuthService(IAPICall api) => _api = api;

        public sealed record SignInResult(bool Ok, string? Error, bool MustChange);

        /// <summary>
        /// <paramref name="login"/> = Employee ID (HRMS sign-in) or email (local account). Only POGovApi is called: it checks
        /// that the employee is registered here and only then asks HRMS for the password — the portal never calls HRMS.
        /// </summary>
        public async Task<SignInResult> SignInAsync(HttpContext http, string login, string password)
        {
            var res = await _api.CallingAPI<ApiDataResponse<LoginResponse>, LoginRequest>(AllApiNames.PO_AUTH_LOGIN,
                new LoginRequest { LoginId = login.Trim(), Password = password }, "", http.RequestAborted);
            if (!res.IsSuccess)
                return new(false, string.IsNullOrWhiteSpace(res.responseMessage) ? "Sign in failed" : res.responseMessage, false);
            var token = res.Data?.Token;
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

        /// <summary>PO_AUTH_CHANGE_PASSWORD, then re-issues the cookie (mustChange cleared). Null = success, else the message.</summary>
        public async Task<string?> ChangePasswordAsync(HttpContext http, string token, string oldPassword, string newPassword)
        {
            var res = await _api.CallingAPI<ApiDataResponse<OkResponse>, ChangePasswordRequest>(AllApiNames.PO_AUTH_CHANGE_PASSWORD,
                new ChangePasswordRequest { Old = oldPassword, New = newPassword }, token, http.RequestAborted);
            if (!res.IsSuccess) return string.IsNullOrEmpty(res.responseMessage) ? "Could not change password" : res.responseMessage;
            await RefreshAsync(http, token);
            return null;
        }

        public async Task SignOutAsync(HttpContext http)
        {
            var token = http.User.FindFirst(CustomClaimTypes.SessionToken)?.Value;
            if (!string.IsNullOrEmpty(token))
                await _api.CallingAPI<GenRes, EmptyReq>(AllApiNames.PO_AUTH_LOGOUT, EmptyReq.Instance, token);
            await http.SignOutAsync(AppConstants.PortalAuthScheme);
        }

        /// <summary>PO_AUTH_ME with this token (null when the API refuses it).</summary>
        public Task<ApiDataResponse<MeResponse>> MeAsync(string token, CancellationToken ct = default) =>
            _api.CallingAPI<ApiDataResponse<MeResponse>, EmptyReq>(AllApiNames.PO_AUTH_ME, EmptyReq.Instance, token, ct);

        public async Task<ClaimsPrincipal?> BuildPrincipalAsync(string token)
        {
            var me = await MeAsync(token);
            if (!me.IsSuccess || me.Data?.User == null) return null;
            return FromUser(me.Data.User, token);
        }

        public static ClaimsPrincipal FromUser(UserPublicDto user, string token)
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
            };
            if (!string.IsNullOrEmpty(user.EmpCode)) claims.Add(new Claim(CustomClaimTypes.EmpCode, user.EmpCode));
            foreach (var role in user.Roles ?? new List<string>())
                if (!string.IsNullOrEmpty(role)) claims.Add(new Claim(ClaimTypes.Role, role));
            return new ClaimsPrincipal(new ClaimsIdentity(claims, AppConstants.PortalAuthScheme));
        }
    }
}
