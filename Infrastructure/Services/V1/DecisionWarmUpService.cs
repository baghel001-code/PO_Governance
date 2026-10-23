using Application.Interfaces.V1.Sap;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Infrastructure.Services.V1
{
    /// <summary>
    /// Keeps the decision data warm: once the database is ready, and then every <see cref="CheckSeconds"/> seconds, loads
    /// the current data snapshot and runs one in-memory decision (<see cref="ISapDecisionService.WarmUpAsync"/>). Loading the
    /// uploaded files and building the lookups can take longer than SAP's 3-second budget; done here, a /decisions call
    /// after a start or an upload only does the decision itself. A check with unchanged data is one manifest read.
    /// </summary>
    public sealed class DecisionWarmUpService : BackgroundService
    {
        public const int CheckSeconds = 30;

        private readonly ISapDecisionService _decisions;
        private readonly DatabaseReadiness _readiness;
        private readonly ILogger<DecisionWarmUpService> _log;

        public DecisionWarmUpService(ISapDecisionService decisions, DatabaseReadiness readiness, ILogger<DecisionWarmUpService> log)
        {
            _decisions = decisions;
            _readiness = readiness;
            _log = log;
        }

        protected override async Task ExecuteAsync(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                if (_readiness.Ready)
                {
                    try
                    {
                        var ms = await _decisions.WarmUpAsync(ct);
                        if (ms != null) _log.LogInformation("Decision data loaded and warmed up in {DurationMs} ms", ms);
                    }
                    catch (OperationCanceledException) when (ct.IsCancellationRequested) { return; }
                    catch (Exception ex)
                    {
                        _log.LogWarning("Decision warm-up failed: {Error}", ex.Message);
                    }
                }
                try { await Task.Delay(TimeSpan.FromSeconds(_readiness.Ready ? CheckSeconds : 5), ct); }
                catch (OperationCanceledException) { return; }
            }
        }
    }
}
