using Application.Interfaces.V1;
using Domain.Entities.AppSettings;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Infrastructure.Services.V1
{
    /// <summary>
    /// server.py main(): waits for the database and creates the schema if missing (init_store), loads
    /// users / seeds the admin (Accounts), then migrate_ids and prune_history — before requests are served.
    /// </summary>
    public sealed class StartupService : IHostedService
    {
        private readonly IStore _store;
        private readonly IAccountsService _acc;
        private readonly IMaintenanceService _maint;
        private readonly PoGovSettings _cfg;
        private readonly ILogger<StartupService> _log;

        public StartupService(IStore store, IAccountsService acc, IMaintenanceService maint, IOptions<PoGovSettings> cfg, ILogger<StartupService> log)
        {
            _store = store;
            _acc = acc;
            _maint = maint;
            _cfg = cfg.Value;
            _log = log;
        }

        public async Task StartAsync(CancellationToken ct)
        {
            await _store.InitializeAsync(_cfg.DbWaitSeconds, ct);
            await _acc.InitializeAsync(ct);
            await _maint.MigrateIdsAsync(ct);
            await _maint.PruneHistoryAsync(ct);
            var users = await _acc.GetUsersAsync(ct);
            _log.LogInformation("VMM PO Governance API {Version} (storage: {Storage}, users: {Users}, admin: {Admin}, AI chat {Chat}, AI insights {Insights})",
                Domain.Constants.AppConstants.AppVersion, _store.Describe(), users.Count, _cfg.AdminEmailOrDefault,
                _cfg.AiChatEnabled ? "on" : "off", _cfg.AiInsightsEnabled ? "on" : "off");
        }

        public Task StopAsync(CancellationToken ct) => Task.CompletedTask;
    }
}
