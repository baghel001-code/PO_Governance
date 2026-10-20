using Admin.Common;
using Domain.Constants;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Server;
using Microsoft.Extensions.Options;

namespace Admin.Auth
{
    /// <summary>
    /// Re-checks the session with POGovApi (PO_AUTH_ME with the cookie's session token) every
    /// AppConfigurationSettings:UserRevalidateInSeconds while a page is open: a password reset, deactivation or idle
    /// expiry on the server signs the open page out (v1.1.4 behaviour: "Changing or resetting a password signs that
    /// person out everywhere else"). Transient failures (API restarting, network) are tolerated for a few checks so a
    /// short API restart doesn't sign everyone out (QPS CustomRevalidatingAuthenticationStateProvider).
    /// </summary>
    public sealed class ApiRevalidatingAuthStateProvider : RevalidatingServerAuthenticationStateProvider
    {
        private const int MaxConsecutiveFailures = 5;
        private readonly IServiceScopeFactory _scopes;
        private readonly AppConfigurationSettings _settings;
        private readonly ILogger _log;
        private int _failures;

        public ApiRevalidatingAuthStateProvider(ILoggerFactory lf, IServiceScopeFactory scopes, IOptions<AppConfigurationSettings> settings) : base(lf)
        {
            _scopes = scopes;
            _settings = settings.Value;
            _log = lf.CreateLogger<ApiRevalidatingAuthStateProvider>();
        }

        protected override TimeSpan RevalidationInterval => TimeSpan.FromSeconds(Math.Max(_settings.UserRevalidateInSeconds, 15));

        protected override async Task<bool> ValidateAuthenticationStateAsync(AuthenticationState state, CancellationToken ct)
        {
            var token = state.User.FindFirst(CustomClaimTypes.SessionToken)?.Value;
            if (string.IsNullOrEmpty(token)) return false;
            // Runs on a timer of the circuit: own DI scope.
            await using var scope = _scopes.CreateAsyncScope();
            var auth = scope.ServiceProvider.GetRequiredService<PortalAuthService>();
            var res = await auth.MeAsync(token, ct);
            if (res.IsSuccess) { _failures = 0; return true; }
            // 401 "Please log in" = the session ended; a 401 from the static-token check is a configuration problem, not the user's.
            if (res.responseCode is 401 && res.responseMessage != Infrastructure.Middleware.StaticTokenMiddleware.Unauthorized) return false;
            if (res.responseCode is 403) return true; // mustChange — handled by the layout
            if (++_failures < MaxConsecutiveFailures)
            {
                _log.LogWarning("Session revalidation failed ({Code}: {Message}) {Count}/{Max}; keeping the user signed in until the next check.",
                    res.responseCode, res.responseMessage, _failures, MaxConsecutiveFailures);
                return true;
            }
            _log.LogError("Session revalidation failed {Count} times in a row; signing the user out.", _failures);
            return false;
        }
    }
}
