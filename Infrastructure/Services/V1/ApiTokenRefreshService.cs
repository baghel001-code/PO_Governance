using Application.Interfaces.V1;
using Domain.Entities.Common;
using Infrastructure.Middleware;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Infrastructure.Services.V1
{
    /// <summary>
    /// Keeps <see cref="ApiTokenRegistry"/> in step with the database: once the database is ready and then every
    /// <see cref="RefreshSeconds"/> seconds it reads the API master of this environment (the same PRO_GET_ALL_API answer the
    /// portal gets) and gives the registry the paths whose token comes from API_AUTHORIZATION. A token that does not decode
    /// to a valid header value is skipped (the portal then falls back to the static token too). A failed read keeps the
    /// last known tokens.
    /// </summary>
    public sealed class ApiTokenRefreshService : BackgroundService
    {
        public const int RefreshSeconds = 60;

        private readonly IAPI _api;
        private readonly ApiTokenRegistry _registry;
        private readonly DatabaseReadiness _readiness;
        private readonly ILogger<ApiTokenRefreshService> _log;
        private string _lastSummary = "";

        public ApiTokenRefreshService(IAPI api, ApiTokenRegistry registry, DatabaseReadiness readiness, ILogger<ApiTokenRefreshService> log)
        {
            _api = api;
            _registry = registry;
            _readiness = readiness;
            _log = log;
        }

        protected override async Task ExecuteAsync(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                if (_readiness.Ready)
                {
                    try { await RefreshAsync(ct); }
                    catch (OperationCanceledException) when (ct.IsCancellationRequested) { return; }
                    catch (Exception ex) { _log.LogWarning("API tokens not refreshed (keeping the last ones): {Error}", ex.Message); }
                }
                try { await Task.Delay(TimeSpan.FromSeconds(_readiness.Ready ? RefreshSeconds : 5), ct); }
                catch (OperationCanceledException) { return; }
            }
        }

        public async Task RefreshAsync(CancellationToken ct = default)
        {
            var res = await _api.GetAllApi(ct);
            if (res.responseCode != 0 || res.ApiData == null) return;
            var (overrides, skipped) = Overrides(res.ApiData);
            _registry.Update(overrides);
            var summary = overrides.Count + "/" + string.Join(",", skipped);
            if (summary == _lastSummary) return;
            _lastSummary = summary;
            _log.LogInformation("API tokens: {Count} API(s) use their own token from API_AUTHORIZATION", overrides.Count);
            if (skipped.Count > 0)
                _log.LogWarning("API_AUTHORIZATION token of {Tasks} does not decode to a valid token (it must be My_Encode of the token) - " +
                                "the static token is used for them", string.Join(", ", skipped));
        }

        /// <summary>path → plain token for the rows whose token comes from API_AUTHORIZATION (GetAllApi always keeps the static token).</summary>
        public static (Dictionary<string, string> Overrides, List<string> Skipped) Overrides(IEnumerable<ApiData> rows)
        {
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var skipped = new List<string>();
            foreach (var r in rows)
            {
                if (r.Auth_Source is not ("TASK" or "ENVIRONMENT") || string.IsNullOrWhiteSpace(r.Api_Authorization) || string.IsNullOrWhiteSpace(r.Api_Url)) continue;
                if (string.Equals(r.Task_Name, "PO_GET_ALL_API", StringComparison.OrdinalIgnoreCase)) continue;
                string? token = null;
                try { token = StaticTokenMiddleware.DecodeToken(r.Api_Authorization); } catch { /* not My_Encode text */ }
                if (string.IsNullOrEmpty(token) || !token.All(c => c > ' ' && c < 0x7F))
                {
                    skipped.Add(r.Task_Name ?? "?");
                    continue;
                }
                var path = Uri.TryCreate(r.Api_Url, UriKind.Absolute, out var u) ? u.AbsolutePath : r.Api_Url;
                map[ApiTokenRegistry.NormalizePath(path)] = token;
            }
            return (map, skipped);
        }
    }
}
