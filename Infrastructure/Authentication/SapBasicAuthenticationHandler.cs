using System.Security.Claims;
using System.Text;
using System.Text.Encodings.Web;
using Application.Interfaces.V1.Sap;
using Domain.Entities.Sap;
using Infrastructure.Middleware;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Infrastructure.Authentication
{
    /// <summary>
    /// Authentication scheme <see cref="SchemeName"/> ("SapBasic") — used only by the SAP controller (<c>/api/sap/v1/*</c>):
    /// <c>Authorization: Basic base64(user:password)</c> of a technical user (SAP_TECH_USERS, PBKDF2 hash, lockout —
    /// <see cref="ISapAuthService"/>). The IP allow-list has already been checked by <see cref="SapInterfaceMiddleware"/>.
    /// A missing / wrong login is answered 401 with the contract's Error body and <c>WWW-Authenticate: Basic</c>.
    /// Claims: name = technical user, <see cref="SourceSystemClaim"/> = its SAP system (ping source_system_recognised).
    /// </summary>
    public sealed class SapBasicAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>
    {
        public const string SchemeName = "SapBasic";
        public const string SourceSystemClaim = "sap_source_system";
        private const string ErrorItem = "Sap.AuthError";

        private readonly ISapAuthService _auth;

        public SapBasicAuthenticationHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder, ISapAuthService auth)
            : base(options, logger, encoder)
        {
            _auth = auth;
        }

        /// <summary>(user, password) of an "Authorization: Basic …" header; null when absent or malformed.</summary>
        public static (string User, string Password)? ParseBasic(string? header)
        {
            if (string.IsNullOrWhiteSpace(header) || !header.StartsWith("Basic ", StringComparison.OrdinalIgnoreCase)) return null;
            try
            {
                var text = Encoding.UTF8.GetString(Convert.FromBase64String(header[6..].Trim()));
                var i = text.IndexOf(':');
                return i <= 0 ? null : (text[..i], text[(i + 1)..]);
            }
            catch (FormatException)
            {
                return null;
            }
        }

        protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var creds = ParseBasic(Request.Headers.Authorization.ToString());
            if (creds == null) return AuthenticateResult.NoResult();
            var ip = Context.Items[SapInterfaceMiddleware.ClientIpItem] as string ?? Context.Connection.RemoteIpAddress?.ToString() ?? "";
            var res = await _auth.AuthenticateAsync(creds.Value.User, creds.Value.Password, ip, Context.RequestAborted);
            if (!res.Ok)
            {
                Context.Items[ErrorItem] = (res.ErrorCode, res.Message);
                return AuthenticateResult.Fail(res.Message);
            }
            var claims = new[]
            {
                new Claim(ClaimTypes.Name, res.User!.UserName),
                new Claim(ClaimTypes.NameIdentifier, res.User.UserName),
                new Claim(SourceSystemClaim, res.User.SourceSystem ?? ""),
            };
            var id = new ClaimsIdentity(claims, Scheme.Name);
            return AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(id), Scheme.Name));
        }

        protected override Task HandleChallengeAsync(AuthenticationProperties properties)
        {
            var (code, message) = Context.Items[ErrorItem] is ValueTuple<string, string> e ? e : (SapErrorCodes.Unauthorized, "Missing or wrong credentials");
            return SapInterfaceMiddleware.WriteAsync(Context, 401, code, message);
        }

        protected override Task HandleForbiddenAsync(AuthenticationProperties properties) =>
            SapInterfaceMiddleware.WriteAsync(Context, 401, SapErrorCodes.Unauthorized, "Not allowed");
    }
}
