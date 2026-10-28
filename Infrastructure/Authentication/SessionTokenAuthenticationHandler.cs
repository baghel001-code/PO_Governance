using System.Security.Claims;
using System.Text.Encodings.Web;
using Application.Interfaces.V1;
using Domain.Constants;
using Domain.Entities.AppSettings;
using Infrastructure.Middleware;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Infrastructure.Authentication
{
    /// <summary>
    /// Scheme <see cref="AppConstants.AuthScheme"/>: header "X-Session-Token: &lt;session token&gt;" (<see cref="AppConstants.SessionHeader"/>;
    /// the Authorization header carries the static API token, see StaticTokenMiddleware) checked
    /// against the sessions exactly like accounts.py session_user (idle timeout, last-seen persisted every
    /// 5 min, inactive user → not signed in). Claims: email, name, one role claim per role, mustChange,
    /// session token.
    /// </summary>
    public sealed class SessionTokenAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>
    {
        private readonly IAccountsService _acc;

        public SessionTokenAuthenticationHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger,
                                                 UrlEncoder encoder, IAccountsService acc)
            : base(options, logger, encoder)
        {
            _acc = acc;
        }

        public static string? TokenFrom(HttpRequest req)
        {
            var h = req.Headers[AppConstants.SessionHeader].ToString().Trim();
            return h.Length > 0 ? h : null;
        }

        protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var token = TokenFrom(Request);
            if (string.IsNullOrEmpty(token)) return AuthenticateResult.NoResult();
            // A database failure propagates → ExceptionMiddleware answers 503 (server.py route()).
            var (u, ended) = await _acc.SessionAsync(token, Context.RequestAborted);
            if (u == null)
            {
                Context.Items[EndReasonItem] = ended;   // the 401 says why (signed in elsewhere, changed by the admin, idle …)
                return AuthenticateResult.Fail(SessionEndReasons.Message(ended));
            }
            var claims = new List<Claim>
            {
                new(CustomClaimTypes.Email, u.Email),
                new(ClaimTypes.Email, u.Email),
                new(ClaimTypes.NameIdentifier, u.Email),
                new(ClaimTypes.Name, u.Name ?? ""),
                new(CustomClaimTypes.MustChange, u.MustChange ? "true" : "false"),
                new(CustomClaimTypes.SessionToken, token),
            };
            claims.AddRange(u.Roles.Select(r => new Claim(ClaimTypes.Role, r)));
            var id = new ClaimsIdentity(claims, Scheme.Name, ClaimTypes.Name, ClaimTypes.Role);
            return AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(id), Scheme.Name));
        }

        /// <summary>HttpContext.Items key: why the session token was refused.</summary>
        public const string EndReasonItem = "pogov.session.end";

        protected override Task HandleChallengeAsync(AuthenticationProperties properties) =>
            ExceptionMiddleware.WriteAsync(Context, 401, SessionEndReasons.Message(Context.Items[EndReasonItem] as string));

        protected override Task HandleForbiddenAsync(AuthenticationProperties properties) =>
            ExceptionMiddleware.WriteAsync(Context, 403, "Not allowed");
    }

    /// <summary>
    /// server.py route(): a user who must change the password gets 403 {mustChange:true} on everything
    /// except the auth endpoints (and the anonymous ones); optional X-VMM header check on non-GET calls.
    /// Runs after UseAuthentication.
    /// </summary>
    public sealed class MustChangePasswordMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly PoGovSettings _cfg;

        public MustChangePasswordMiddleware(RequestDelegate next, IOptions<PoGovSettings> cfg)
        {
            _next = next;
            _cfg = cfg.Value;
        }

        public async Task InvokeAsync(HttpContext ctx)
        {
            var path = ctx.Request.Path.Value ?? "";
            // the SAP interface (/api/sap/*) has its own authentication (SapBasic) and no portal header rules
            if (path.StartsWith("/api/", StringComparison.OrdinalIgnoreCase) && !SapInterfaceMiddleware.IsSapPath(ctx.Request.Path))
            {
                if (_cfg.RequireXvmmHeader && !HttpMethods.IsGet(ctx.Request.Method) && !HttpMethods.IsHead(ctx.Request.Method)
                    && !HttpMethods.IsOptions(ctx.Request.Method) && ctx.Request.Headers[AppConstants.RestrictedHeader] != "1")
                {
                    await ExceptionMiddleware.WriteAsync(ctx, 403, "Missing X-VMM header");
                    return;
                }
                var anonymous = ctx.GetEndpoint()?.Metadata.GetMetadata<IAllowAnonymous>() != null;
                var isAuth = string.Equals(ctx.GetRouteValue("controller") as string, "Auth", StringComparison.OrdinalIgnoreCase);
                if (!anonymous && !isAuth && ctx.User.Identity?.IsAuthenticated == true
                    && string.Equals(ctx.User.FindFirst(CustomClaimTypes.MustChange)?.Value, "true", StringComparison.OrdinalIgnoreCase))
                {
                    await ExceptionMiddleware.WriteAsync(ctx, 403, "Please change your password first", new { mustChange = true });
                    return;
                }
            }
            await _next(ctx);
        }
    }
}
