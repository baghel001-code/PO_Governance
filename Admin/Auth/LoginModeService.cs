using Application.DTOs.Platform;
using Application.Interfaces.V1;
using Domain.Entities.Common;
using Domain.Entities.Request;
using Domain.Entities.Response;
using Domain.Entities.AppSettings;

namespace Admin.Auth
{
    /// <summary>
    /// How people sign in (PoGovSettings:LoginMode of POGovApi), read from the anonymous Health call and kept for a minute:
    /// the sign-in page asks for the Employee ID (HRMS) or the email (Local). When the API can't be reached it assumes HRMS.
    /// </summary>
    public sealed class LoginModeService
    {
        private static readonly TimeSpan CacheTime = TimeSpan.FromMinutes(1);
        private readonly IAPICall _api;
        private static volatile Cached? _cached;   // shared by every scope (IAPICall is scoped)
        private sealed record Cached(LoginInfo Info, DateTime Until);

        public LoginModeService(IAPICall api) => _api = api;

        public async Task<LoginInfo> GetAsync(CancellationToken ct = default)
        {
            if (_cached is { } c && c.Until > DateTime.UtcNow) return c.Info;
            var res = await _api.CallingAPI<ApiDataResponse<HealthResponse>, EmptyReq>(AllApiNames.PO_HEALTH, EmptyReq.Instance, "", ct);
            if (res.IsSuccess && res.Data?.Login is { } info)
            {
                _cached = new Cached(info, DateTime.UtcNow + CacheTime);
                return info;
            }
            return new LoginInfo { Mode = LoginModes.Hrms };
        }

        public static bool IsHrms(LoginInfo info) => !string.Equals(info.Mode, LoginModes.Local, StringComparison.OrdinalIgnoreCase);
    }
}
