using Application.Interfaces.V1;
using Domain.Entities.AppSettings;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Infrastructure.Services.V1
{
    /// <summary>
    /// server.py main(): waits for the database and creates / checks the schema (init_store), loads users and seeds
    /// the admin (Accounts), then migrate_ids and prune_history.
    /// <para>
    /// Runs in the background so the API always opens its port: when the database is down or not set up yet, the API
    /// keeps retrying every <see cref="RetrySeconds"/> seconds and answers calls with 503 and the reason
    /// (<see cref="DatabaseReadiness"/>), instead of exiting and leaving the portal with "Cannot reach the server".
    /// </para>
    /// </summary>
    public sealed class StartupService : BackgroundService
    {
        public const int RetrySeconds = 15;

        private readonly IStore _store;
        private readonly IAccountsService _acc;
        private readonly IMaintenanceService _maint;
        private readonly DatabaseReadiness _readiness;
        private readonly PoGovSettings _cfg;
        private readonly ILogger<StartupService> _log;

        public StartupService(IStore store, IAccountsService acc, IMaintenanceService maint, DatabaseReadiness readiness,
                              IOptions<PoGovSettings> cfg, ILogger<StartupService> log)
        {
            _store = store;
            _acc = acc;
            _maint = maint;
            _readiness = readiness;
            _cfg = cfg.Value;
            _log = log;
        }

        protected override async Task ExecuteAsync(CancellationToken ct)
        {
            await Task.Yield(); // let the host finish starting (Kestrel opens the port) before the first attempt
            string? lastError = null;
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    await _store.InitializeAsync(0, ct);
                    await _acc.InitializeAsync(ct);
                    await _maint.MigrateIdsAsync(ct);
                    await _maint.PruneHistoryAsync(ct);
                    var users = await _acc.GetUsersAsync(ct);
                    _readiness.MarkReady();
                    _log.LogInformation("VMM PO Governance API {Version} ready (storage: {Storage}, users: {Users}, admin: {Admin}, AI chat {Chat}, AI insights {Insights})",
                        Domain.Constants.AppConstants.AppVersion, _store.Describe(), users.Count, _cfg.AdminEmailOrDefault,
                        _cfg.AiChatEnabled ? "on" : "off", _cfg.AiInsightsEnabled ? "on" : "off");
                    return;
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception e)
                {
                    var reason = Describe(e);
                    _readiness.MarkFailed(reason);
                    if (reason != lastError)
                        _log.LogError(e, "The API cannot use the database yet ({Storage}): {Reason} - retrying every {Seconds} s. " +
                                         "Check ConnectionStrings:DefaultConnection and that database/sqlserver/00..04 were run.",
                                      _store.Describe(), reason, RetrySeconds);
                    else
                        _log.LogWarning("Still waiting for the database: {Reason}", reason);
                    lastError = reason;
                }
                try { await Task.Delay(TimeSpan.FromSeconds(RetrySeconds), ct); }
                catch (OperationCanceledException) { return; }
            }
        }

        /// <summary>The innermost meaningful message (SqlException, missing schema …), one line.</summary>
        private static string Describe(Exception e)
        {
            var inner = e;
            while (inner.InnerException != null && (inner is AggregateException || inner.Message.Length == 0)) inner = inner.InnerException;
            return inner.Message.Replace('\r', ' ').Replace('\n', ' ').Trim();
        }
    }
}
