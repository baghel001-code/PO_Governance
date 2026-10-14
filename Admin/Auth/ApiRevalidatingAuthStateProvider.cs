using System.Text.Json;
using Admin.Common;
using Admin.Interfaces;
using Domain.Constants;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Server;
using Microsoft.Extensions.Options;

namespace Admin.Auth
{
    /// <summary>
    /// Re-checks the session with POGovApi while a page is open: a password reset, deactivation or
    /// idle expiry on the server signs the open page out (v1.1.4 behaviour: "Changing or resetting a
    /// password signs that person out everywhere else"). Transient network failures are tolerated for
    /// a few checks so a short API restart doesn't sign everyone out.
    /// </summary>
    public sealed class ApiRevalidatingAuthStateProvider : RevalidatingServerAuthenticationStateProvider
    {
        private const int MaxConsecutiveFailures = 5;
        private readonly IServiceScopeFactory _scopes;
        private readonly PortalSettings _settings;
        private int _failures;

        public ApiRevalidatingAuthStateProvider(ILoggerFactory lf, IServiceScopeFactory scopes, IOptions<PortalSettings> settings) : base(lf)
        {
            _scopes = scopes;
            _settings = settings.Value;
        }

        protected override TimeSpan RevalidationInterval => TimeSpan.FromSeconds(Math.Max(_settings.StatusPollSeconds * 2, 30));

        protected override async Task<bool> ValidateAuthenticationStateAsync(AuthenticationState state, CancellationToken ct)
        {
            var token = state.User.FindFirst(CustomClaimTypes.SessionToken)?.Value;
            if (string.IsNullOrEmpty(token)) return false;
            await using var scope = _scopes.CreateAsyncScope();
            var api = scope.ServiceProvider.GetRequiredService<IAPICallService>();
            var res = await api.SendWithTokenAsync<JsonElement>(HttpMethod.Get, "api/v1/auth/me", token, null, ct);
            if (res.IsSuccess) { _failures = 0; return true; }
            if (res.ResponseCode is 401) return false;
            if (res.ResponseCode is 403) return true; // mustChange — handled by the layout
            return ++_failures < MaxConsecutiveFailures;
        }
    }
}
