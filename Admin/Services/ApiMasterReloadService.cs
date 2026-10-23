using Admin.Common;
using Infrastructure.Services.V1;
using Microsoft.Extensions.Options;

namespace Admin.Services
{
    /// <summary>
    /// Reloads the API master list (GetAllApi → AllApi.ApiDetails) every AppConfigurationSettings:ApiMasterReloadMinutes, so a
    /// changed base URL (API_BASE_URL) or token (API_AUTHORIZATION) reaches the portal without a restart. A failed reload
    /// keeps the current list. The start-up load stays in Program.cs.
    /// </summary>
    public sealed class ApiMasterReloadService : BackgroundService
    {
        private readonly AppConfigurationSettings _cfg;
        private readonly ILogger<ApiMasterReloadService> _log;

        public ApiMasterReloadService(IOptions<AppConfigurationSettings> cfg, ILogger<ApiMasterReloadService> log)
        {
            _cfg = cfg.Value;
            _log = log;
        }

        protected override async Task ExecuteAsync(CancellationToken ct)
        {
            if (_cfg.ApiMasterReloadMinutes <= 0) return;
            var every = TimeSpan.FromMinutes(_cfg.ApiMasterReloadMinutes);
            while (!ct.IsCancellationRequested)
            {
                try { await Task.Delay(every, ct); }
                catch (OperationCanceledException) { return; }
                try
                {
                    // a failed load logs its reason and leaves AllApi.ApiDetails as it is
                    await new APICallService(_cfg.ProxyValue, null, _log).LoadApiMasterAsync(ct);
                }
                catch (Exception ex) when (!ct.IsCancellationRequested)
                {
                    _log.LogWarning("API master reload failed (keeping the current list): {Error}", ex.Message);
                }
            }
        }
    }
}
