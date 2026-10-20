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
    /// Base for every v1 controller (QPS style: each controller has <c>[Route("api/v1/[Action]")]</c>, POST actions
    /// taking a request object, <c>return Ok(response)</c> with a BaseResponse). Authenticated by default: the static
    /// API token (StaticTokenMiddleware) identifies the portal, the session token in X-Session-Token
    /// (SessionTokenAuthenticationHandler) the user — roles are checked here on the server.
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
            return new AppUserContext(email, name, roles, mustChange, token, ClientIp(ctx));
        }

        /// <summary>X-Forwarded-For (first) or the connection's address — server.py client_address.</summary>
        public static string ClientIp(HttpContext ctx)
        {
            var ip = ctx.Request.Headers["X-Forwarded-For"].ToString().Split(',')[0].Trim();
            return string.IsNullOrEmpty(ip) ? ctx.Connection.RemoteIpAddress?.ToString() ?? "" : ip;
        }

        protected void RequireRole(string message, params string[] roles)
        {
            if (!Me.Has(roles)) throw new ForbiddenException(message);
        }

        protected void RequireStaff() => RequireRole("Only planners and admins can do this", Roles.Admin, Roles.Planner);
        protected void RequireAdmin() => RequireRole("Admin only", Roles.Admin);

        /// <summary>Ok(new ApiDataResponse&lt;T&gt; { Data = data }) — responseCode 0, "SUCCESS".</summary>
        protected ActionResult<ApiDataResponse<T>> Success<T>(T? data) => Ok(ApiDataResponse<T>.Ok(data));
    }
}
