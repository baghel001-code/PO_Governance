using Application.DTOs.Platform;
using Application.Interfaces.V1;
using Domain.Entities.Common;
using Domain.Entities.Request;
using Domain.Entities.Response;

namespace Admin.Services
{
    /// <summary>
    /// Polls PO_GET_STATUS (v1.1.4 polled /api/status every ~30 s): the Approvals badge, and the
    /// "New data on the server — Load now" banner when the planner uploads new files or changes parameters.
    /// </summary>
    public sealed class ServerStatusService : IDisposable
    {
        private readonly IAPICall _api;
        private PeriodicTimer? _timer;
        private CancellationTokenSource? _cts;

        public ServerStatusService(IAPICall api) => _api = api;

        public event Action? Changed;

        public bool Online { get; private set; }
        public string Message { get; private set; } = "Connecting…";
        public int PendingApprovals { get; private set; }
        public long DataVersion { get; private set; }
        public long ParamsVersion { get; private set; }
        public string? Today { get; private set; }
        public string? ServerVersion { get; private set; }
        public bool AiChat { get; private set; }
        public bool AiInsights { get; private set; }
        /// <summary>The last status answer (null until one arrived).</summary>
        public StatusResponse? Last { get; private set; }

        /// <summary>Data / params version the open page last loaded (0 = not yet).</summary>
        public long SeenDataVersion { get; private set; }
        public long SeenParamsVersion { get; private set; }
        public bool HasNewData => SeenDataVersion > 0 && (DataVersion > SeenDataVersion || ParamsVersion > SeenParamsVersion);

        public void MarkLoaded() { SeenDataVersion = DataVersion; SeenParamsVersion = ParamsVersion; Changed?.Invoke(); }

        public void Start(int seconds)
        {
            if (_timer != null) return;
            _cts = new CancellationTokenSource();
            _timer = new PeriodicTimer(TimeSpan.FromSeconds(Math.Max(seconds, 10)));
            _ = LoopAsync(_cts.Token);
        }

        private async Task LoopAsync(CancellationToken ct)
        {
            await RefreshAsync(ct);
            try
            {
                while (_timer != null && await _timer.WaitForNextTickAsync(ct)) await RefreshAsync(ct);
            }
            catch (OperationCanceledException) { }
        }

        /// <summary>PO_GET_STATUS (the same call the pages use to read the datasets / params on load).</summary>
        public Task<ApiDataResponse<StatusResponse>> GetAsync(CancellationToken ct = default) =>
            _api.CallingAPI<ApiDataResponse<StatusResponse>, EmptyReq>(AllApiNames.PO_GET_STATUS, EmptyReq.Instance, null, ct);

        public async Task RefreshAsync(CancellationToken ct = default)
        {
            ApiDataResponse<StatusResponse> res;
            try { res = await GetAsync(ct); }
            catch (OperationCanceledException) { return; }
            Online = res.IsSuccess;
            if (res.IsSuccess && res.Data != null)
            {
                var d = res.Data;
                Last = d;
                PendingApprovals = d.PendingApprovals;
                DataVersion = d.DataVersion;
                ParamsVersion = d.ParamsVersion;
                Today = string.IsNullOrEmpty(d.Today) ? Today : d.Today;
                ServerVersion = string.IsNullOrEmpty(d.Version) ? ServerVersion : d.Version;
                AiChat = d.Ai?.Chat == true;
                AiInsights = d.Ai?.Insights == true;
                if (SeenDataVersion == 0) { SeenDataVersion = DataVersion; SeenParamsVersion = ParamsVersion; }
                Message = "● Shared · " + (Today ?? "");
            }
            else Message = res.responseCode == 401 ? "Signed out" : "Server unreachable";
            Changed?.Invoke();
        }

        public void Dispose()
        {
            _cts?.Cancel();
            _timer?.Dispose();
            _timer = null;
        }
    }
}
