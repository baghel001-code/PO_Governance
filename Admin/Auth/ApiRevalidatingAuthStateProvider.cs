using Admin.Common;
using Domain.Constants;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Server;
using Microsoft.Extensions.Options;

namespace Admin.Auth
{
    /// <summary>
    /// QPS AppRevalidatingAuthStateProvider. An open interactive page makes no HTTP requests, so the cookie handler never
    /// sees it: this re-checks the circuit's session with POGovApi every AppConfigurationSettings:UserRevalidateInSeconds.
    /// The session ends when the user signed in on another device, the admin changed their roles or deactivated them, the
    /// password was reset, or the idle / absolute timeout passed — the page becomes anonymous and the layout signs the user
    /// out with the reason. Also the server-side idle backstop (when the page's JavaScript is not running). Transient
    /// failures (API restarting) are tolerated for a few checks so a short outage doesn't sign everyone out.
    /// </summary>
    public sealed class ApiRevalidatingAuthStateProvider : RevalidatingServerAuthenticationStateProvider
    {
        private const int MaxConsecutiveFailures = 5;
        private readonly IServiceScopeFactory _scopes;
        private readonly AppConfigurationSettings _settings;
        private readonly PortalSettings _portal;
        private readonly SessionEndState _endState;
        private readonly EndedSessions _ended;
        private readonly UserActivityState _activity;
        private readonly ILogger _log;
        private int _failures;

        public ApiRevalidatingAuthStateProvider(ILoggerFactory lf, IServiceScopeFactory scopes, IOptions<AppConfigurationSettings> settings,
                                                IOptions<PortalSettings> portal, SessionEndState endState, EndedSessions ended, UserActivityState activity)
            : base(lf)
        {
            _scopes = scopes;
            _settings = settings.Value;
            _portal = portal.Value;
            _endState = endState;
            _ended = ended;
            _activity = activity;
            _log = lf.CreateLogger<ApiRevalidatingAuthStateProvider>();
        }

        protected override TimeSpan RevalidationInterval => TimeSpan.FromSeconds(Math.Max(_settings.UserRevalidateInSeconds, 15));

        protected override async Task<bool> ValidateAuthenticationStateAsync(AuthenticationState state, CancellationToken ct)
        {
            if (state.User.Identity?.IsAuthenticated != true) return true;
            var token = state.User.FindFirst(CustomClaimTypes.SessionToken)?.Value;

            // idle backstop: IdleTimeoutMonitor (browser) normally signs the user out first, with a warning
            if (_activity.IdleFor >= TimeSpan.FromMinutes(PortalAuthService.IdleMinutes(state.User, _portal.IdleTimeoutMinutes)))
                return End(token, SessionEndReasons.Idle);
            if (PortalAuthService.AuthTime(state.User) is { } since && _portal.AbsoluteLifetimeHours > 0
                && DateTimeOffset.UtcNow - since >= TimeSpan.FromHours(_portal.AbsoluteLifetimeHours))
                return End(token, SessionEndReasons.Expired);

            try
            {
                // runs on a timer of the circuit: own DI scope
                await using var scope = _scopes.CreateAsyncScope();
                var reason = await scope.ServiceProvider.GetRequiredService<PortalAuthService>().CheckSessionAsync(token, ct);
                _failures = 0;
                return reason == null || End(token, reason);
            }
            catch (Exception) when (ct.IsCancellationRequested)
            {
                throw;   // circuit closing
            }
            catch (Exception ex)
            {
                if (++_failures < MaxConsecutiveFailures)
                {
                    _log.LogWarning("Session check failed ({Message}) {Count}/{Max}; keeping the user signed in until the next check.",
                        ex.Message, _failures, MaxConsecutiveFailures);
                    return true;
                }
                _log.LogError(ex, "Session check failed {Count} times in a row; signing the user out.", _failures);
                return End(token, SessionEndReasons.Expired);
            }
        }

        /// <summary>The layout reads the reason and posts the sign-out form; the cookie is refused on its next request already.</summary>
        private bool End(string? token, string reason)
        {
            _endState.Reason = reason;
            _ended.Add(token, reason);
            return false;
        }
    }
}
