using Application.Interfaces.V1;
using Domain.Constants;
using Microsoft.AspNetCore.Components.Authorization;

namespace Admin.Auth
{
    /// <summary>
    /// The signed-in user's POGovApi session token (claim <see cref="CustomClaimTypes.SessionToken"/> of the portal auth
    /// cookie) for <c>APICallService</c>, which sends it as X-Session-Token. Inside a Blazor circuit it reads the
    /// circuit's authentication state; during a plain HTTP request (sign-in / sign-out endpoints, prerendering before
    /// the state is set) it falls back to HttpContext.User.
    /// </summary>
    public sealed class SessionTokenAccessor : ISessionTokenAccessor
    {
        private readonly AuthenticationStateProvider _auth;
        private readonly IHttpContextAccessor _http;

        public SessionTokenAccessor(AuthenticationStateProvider auth, IHttpContextAccessor http)
        {
            _auth = auth;
            _http = http;
        }

        public async Task<string?> GetSessionTokenAsync()
        {
            try
            {
                var state = await _auth.GetAuthenticationStateAsync();
                var token = state.User.FindFirst(CustomClaimTypes.SessionToken)?.Value;
                if (!string.IsNullOrEmpty(token)) return token;
            }
            catch (InvalidOperationException)
            {
                // "GetAuthenticationStateAsync was called before SetAuthenticationState" — not in a circuit
            }
            return _http.HttpContext?.User.FindFirst(CustomClaimTypes.SessionToken)?.Value;
        }
    }
}
