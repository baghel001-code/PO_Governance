using System.Security.Claims;
using Domain.Constants;
using Domain.Entities.Response;
using Domain.Exceptions;
using Domain.Records;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace POGovApi.Controllers.V1
{
    /// <summary>
    /// Base for every v1 controller: authenticated by default (session token → claims, see the
    /// SessionTokenAuthenticationHandler), helpers for the signed-in user and the ApiResponse envelope.
    /// </summary>
    [ApiController]
    [Authorize]
    [Produces("application/json")]
    public abstract class ApiControllerBase : ControllerBase
    {
        private AppUserContext? _me;

        /// <summary>The signed-in user (server.py self.me). Claims are set by the auth handler.</summary>
        protected AppUserContext Me => _me ??= FromClaims(User, HttpContext);

        public static AppUserContext FromClaims(ClaimsPrincipal p, HttpContext ctx)
        {
            var email = p.FindFirstValue(CustomClaimTypes.Email) ?? p.FindFirstValue(ClaimTypes.Email) ?? "";
            var name = p.FindFirstValue(ClaimTypes.Name) ?? email;
            var roles = p.FindAll(ClaimTypes.Role).Select(c => c.Value).ToList();
            var mustChange = string.Equals(p.FindFirstValue(CustomClaimTypes.MustChange), "true", StringComparison.OrdinalIgnoreCase);
            var token = p.FindFirstValue(CustomClaimTypes.SessionToken);
            var ip = ctx.Request.Headers["X-Forwarded-For"].ToString().Split(',')[0].Trim();
            if (string.IsNullOrEmpty(ip)) ip = ctx.Connection.RemoteIpAddress?.ToString() ?? "";
            return new AppUserContext(email, name, roles, mustChange, token, ip);
        }

        protected void RequireRole(string message, params string[] roles)
        {
            if (!Me.Has(roles)) throw new ForbiddenException(message);
        }

        protected void RequireStaff() => RequireRole("Only planners and admins can do this", Roles.Admin, Roles.Planner);
        protected void RequireAdmin() => RequireRole("Admin only", Roles.Admin);

        protected ActionResult<ApiResponse<T>> Success<T>(T? data) => Ok(ApiResponse<T>.Ok(data));
    }
}
