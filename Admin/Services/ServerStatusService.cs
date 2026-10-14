using System.Text.Json;
using Admin.Interfaces;

namespace Admin.Services
{
    /// <summary>
    /// Polls GET api/v1/status (v1.1.4 polled /api/status every ~30 s): the Approvals badge, and the
    /// "New data on the server — Load now" banner when the planner uploads new files or changes parameters.
    /// </summary>
    public sealed class ServerStatusService : IDisposable
    {
        private readonly IAPICallService _api;
        private PeriodicTimer? _timer;
        private CancellationTokenSource? _cts;

        public ServerStatusService(IAPICallService api) => _api = api;

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
        public JsonElement Raw { get; private set; }

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

        public async Task RefreshAsync(CancellationToken ct = default)
        {
            var res = await _api.GetAsync<JsonElement>("api/v1/status", ct);
            Online = res.IsSuccess;
            if (res.IsSuccess && res.Data.ValueKind == JsonValueKind.Object)
            {
                var d = res.Data;
                Raw = d.Clone();
                PendingApprovals = Int(d, "pendingApprovals");
                DataVersion = Long(d, "dataVersion");
                ParamsVersion = Long(d, "paramsVersion");
                Today = d.TryGetProperty("today", out var t) && t.ValueKind == JsonValueKind.String ? t.GetString() : Today;
                ServerVersion = d.TryGetProperty("version", out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : ServerVersion;
                if (d.TryGetProperty("ai", out var ai) && ai.ValueKind == JsonValueKind.Object)
                {
                    AiChat = ai.TryGetProperty("chat", out var c) && c.ValueKind == JsonValueKind.True;
                    AiInsights = ai.TryGetProperty("insights", out var i) && i.ValueKind == JsonValueKind.True;
                }
                if (SeenDataVersion == 0) { SeenDataVersion = DataVersion; SeenParamsVersion = ParamsVersion; }
                Message = "● Shared · " + (Today ?? "");
            }
            else Message = res.ResponseCode == 401 ? "Signed out" : "Server unreachable";
            Changed?.Invoke();
        }

        private static int Int(JsonElement d, string n) => d.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetInt32() : 0;
        private static long Long(JsonElement d, string n) => d.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetInt64() : 0;

        public void Dispose()
        {
            _cts?.Cancel();
            _timer?.Dispose();
            _timer = null;
        }
    }
}
