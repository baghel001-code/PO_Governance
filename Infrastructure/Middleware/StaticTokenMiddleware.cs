using System.Security.Cryptography;
using System.Text;
using Infrastructure.Services.V1;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using MyNewEncDec;

namespace Infrastructure.Middleware
{
    /// <summary>
    /// QPS StaticTokenMiddleware: every <c>api/v1/*</c> call (except <c>api/v1/Health</c> and CORS pre-flights) must carry
    /// <c>Authorization: Bearer &lt;static API token&gt;</c>. The token is <c>JwtSettings:SecretKey</c> decoded with
    /// MyNewEncDec (<c>New_Enc_Dec.My_Decode</c>); the portal gets the same token from its API master rows.
    /// This identifies the calling application only — the user is identified by the session token in
    /// <c>X-Session-Token</c> (SessionTokenAuthenticationHandler), which still decides every role check.
    /// Differences from QPS: constant-time comparison, JSON 401 body, health and pre-flight requests pass, and a path
    /// can have its own token per environment (API_AUTHORIZATION, through <see cref="ApiTokenRegistry"/>).
    /// </summary>
    public class StaticTokenMiddleware
    {
        public const string Unauthorized = "Unauthorized: Invalid or missing token";

        private readonly RequestDelegate _next;
        private readonly byte[] _expected;
        private readonly ApiTokenRegistry? _registry;

        public StaticTokenMiddleware(RequestDelegate next, IConfiguration configuration, ApiTokenRegistry? registry = null)
        {
            _registry = registry;
            _next = next;
            var encoded = configuration.GetSection("JwtSettings")["SecretKey"];
            if (string.IsNullOrWhiteSpace(encoded))
                throw new InvalidOperationException("JwtSettings:SecretKey (the My_Encode-d static API token) is not set in POGovApi/appsettings.json.");
            var token = DecodeToken(encoded);
            if (string.IsNullOrEmpty(token)) throw new InvalidOperationException("JwtSettings:SecretKey decodes to an empty token.");
            _expected = Encoding.UTF8.GetBytes(token);
        }

        /// <summary>My_Decode of the configured (encoded) token.</summary>
        public static string DecodeToken(string encoded) => new New_Enc_Dec().My_Decode(encoded.Trim());

        /// <summary>My_Encode of a new static token — the text for JwtSettings:SecretKey, MasterApiSettings:Api_Authorization and API_MASTER.</summary>
        public static string EncodeToken(string plain) => new New_Enc_Dec().My_Encode(plain);

        public static bool IsProtected(PathString path) =>
            path.StartsWithSegments("/api/v1", StringComparison.OrdinalIgnoreCase)
            && !path.StartsWithSegments("/api/v1/Health", StringComparison.OrdinalIgnoreCase);

        public async Task InvokeAsync(HttpContext context)
        {
            if (!IsProtected(context.Request.Path) || HttpMethods.IsOptions(context.Request.Method))
            {
                await _next(context);
                return;
            }
            var authHeader = context.Request.Headers.Authorization.ToString();
            if (authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            {
                var plain = authHeader["Bearer ".Length..].Trim();
                if (_registry != null ? _registry.IsValid(context.Request.Path, plain)
                                      : CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(plain), _expected))
                {
                    await _next(context);
                    return;
                }
            }
            await ExceptionMiddleware.WriteAsync(context, 401, Unauthorized);
        }
    }
}
