using System.Globalization;
using System.Text.Json.Nodes;
using Application.DTOs.Platform;
using Application.Interfaces.V1;
using Domain.Constants;
using Domain.Entities.AppSettings;
using Domain.Entities.Store;
using Domain.Exceptions;
using Domain.Records;
using Microsoft.Extensions.Options;

namespace Application.Services.V1.Platform
{
    /// <summary>server.py api_GET_status, me_public and api_GET_health.</summary>
    public sealed class StatusService : IStatusService
    {
        private readonly IStore _store;
        private readonly IAppClock _clock;
        private readonly IAccountsService _acc;
        private readonly IApprovalService _approvals;
        private readonly PoGovSettings _cfg;

        public StatusService(IStore store, IAppClock clock, IAccountsService acc, IApprovalService approvals, IOptions<PoGovSettings> cfg)
        {
            _store = store;
            _clock = clock;
            _acc = acc;
            _approvals = approvals;
            _cfg = cfg.Value;
        }

        public async Task<UserPublicDto?> MePublicAsync(AppUserContext? me, CancellationToken ct = default)
        {
            if (me == null) return null;
            var u = await _acc.GetUserAsync(me.Email, ct);
            if (u == null) return null;
            var d = _acc.Public(u);
            d.RoleLabels = d.Roles.Select(r => Roles.Labels.TryGetValue(r, out var l) ? l : r).ToList();
            var scope = await _acc.ScopeIndexAsync(ct);
            var keys = scope.KeysOf(me.Email);
            d.McCount = keys.Count;
            d.Restricted = !PlatformCore.IsStaff(me) && _cfg.RestrictToOwnMcs;
            if (d.Restricted == true) d.ScopeKeys = keys.OrderBy(k => k, StringComparer.Ordinal).ToList();
            return d;
        }

        public async Task<StatusResponse> GetStatusAsync(AppUserContext? me, CancellationToken ct = default)
        {
            Manifest m;
            JsonNode? prm;
            int seq;
            using (await _store.LockAsync(ct))
            {
                m = await _store.GetManifestAsync(ct);
                prm = await _store.GetDocAsync(DocNames.Params, ct);
                (_, seq) = await _store.ReadRequestsAsync(_clock.Today, false, ct);
            }
            return new StatusResponse
            {
                Version = AppConstants.AppVersion,
                ServerTime = _clock.NowIso,
                Today = _clock.Today,
                DataVersion = m.DataVersion,
                ParamsVersion = m.ParamsVersion,
                Datasets = m.Datasets,
                Params = prm,
                ReqSeq = seq,
                Me = await MePublicAsync(me, ct),
                PendingApprovals = me == null ? 0 : await _approvals.PendingCountAsync(me, ct),
                Ai = new AiFlags { Chat = _cfg.AiChatEnabled, Insights = _cfg.AiInsightsEnabled }
            };
        }

        public HealthResponse Health() => new() { Version = AppConstants.AppVersion, Storage = _store.Kind };
    }

    /// <summary>server.py api_POST_ai (enable checks, limits, audit lines).</summary>
    public sealed class AiService : IAiService
    {
        private readonly IAiClient _client;
        private readonly IAuditService _audit;
        private readonly PoGovSettings _cfg;

        public AiService(IAiClient client, IAuditService audit, IOptions<PoGovSettings> cfg)
        {
            _client = client;
            _audit = audit;
            _cfg = cfg.Value;
        }

        public async Task<AiResult> ChatAsync(JsonObject body, AppUserContext me, CancellationToken ct = default)
        {
            if (!_cfg.AiChatEnabled)
                throw new ServiceUnavailableException("AI chat is not enabled on this server (no anthropic_api_key in config.json).");
            var maxTokens = Math.Min(body.GetTruthy("max_tokens") ? PlatformCore.PyInt(body["max_tokens"]) : 1000, 4000);
            var system = PlatformCore.Trunc(PyJson.StrOrEmpty(body.Get("system")), 400000);
            var messages = body.GetTruthy("messages") ? body["messages"]!.DeepClone() : new JsonArray();
            var res = await _client.AnthropicCallAsync(system, messages, (int)maxTokens, ct);
            await _audit.AppendAsync(me.AuditName, "ai-chat", $"status={res.Status}", ct);
            return res;
        }

        public async Task<AiResult> InsightsAsync(JsonObject body, AppUserContext me, CancellationToken ct = default)
        {
            var prompt = PlatformCore.Trunc(PyJson.StrOrEmpty(body.Get("prompt")), 400000);
            if (!_cfg.AiInsightsEnabled)
                throw new ServiceUnavailableException("AI Insights is not enabled on this server (add anthropic_api_key or groq_api_key in config.json).");
            var res = await _client.InsightsCallAsync(prompt, ct);
            var prov = (res.Data as JsonObject).GetStr("_provider") ?? "?";
            await _audit.AppendAsync(me.AuditName, "ai-insights", $"status={res.Status} provider={prov}", ct);
            return res;
        }
    }

    /// <summary>server.py migrate_ids and prune_history (run once at start-up).</summary>
    public sealed class MaintenanceService : IMaintenanceService
    {
        private readonly IStore _store;
        private readonly IAppClock _clock;
        private readonly PoGovSettings _cfg;

        public MaintenanceService(IStore store, IAppClock clock, IOptions<PoGovSettings> cfg)
        {
            _store = store;
            _clock = clock;
            _cfg = cfg.Value;
        }

        /// <summary>
        /// Gives Upload IDs / Run IDs to history saved before IDs existed. Consecutive uploads by the
        /// same person within 2 minutes = one upload (IDs 9001+ mark back-filled numbers).
        /// </summary>
        public async Task MigrateIdsAsync(CancellationToken ct = default)
        {
            using (await _store.LockAsync(ct))
            {
                var entries = await _store.ReadLogAsync(LogStreams.Uploads, null, ct);
                if (entries.Any(e => !e.GetTruthy("uploadId")))
                {
                    var counters = new Dictionary<string, int>();
                    var batches = new List<UploadBatch>();
                    (string? By, string? Action, double Ts, string Uid)? cur = null;
                    foreach (var e in entries)
                    {
                        if (e.GetTruthy("uploadId")) { cur = null; continue; }
                        var t = e.GetTruthy("updatedAt") ? e.GetStr("updatedAt")! : _clock.NowIso;
                        var ts = t.Length > 0 ? PlatformCore.ParseIsoEpoch(t, _clock) : 0;
                        var by = e.GetStr("updatedBy");
                        var action = e.GetStr("action");
                        if (!(cur is { } c && c.By == by && c.Action == action && ts - c.Ts <= 120))
                        {
                            var day = (t.Length > 10 ? t[..10] : t).Replace("-", "");
                            counters[day] = counters.GetValueOrDefault(day) + 1;
                            var uid = $"UPL-{day}-{9000 + counters[day]:0000}";
                            cur = (by, action, ts, uid);
                            batches.Add(new UploadBatch
                            {
                                UploadId = uid, At = t, By = by,
                                Source = action == "restore" ? "restore" : action == "clear" ? "clear" : "files",
                                SourceFiles = new List<string>(), Note = "Uploaded before Upload IDs existed"
                            });
                        }
                        cur = (cur!.Value.By, cur.Value.Action, ts, cur.Value.Uid);
                        e["uploadId"] = cur.Value.Uid;
                    }
                    await _store.RewriteLogAsync(LogStreams.Uploads, entries, null, ct);
                    foreach (var b in batches) await _store.AppendLogAsync(LogStreams.Batches, PyJson.ToObject(b), null, ct);
                    var m = await _store.GetManifestAsync(ct);
                    var byVer = new Dictionary<(string, string?), string?>();
                    foreach (var e in entries.Where(e => e.GetStr("action") == "upload"))
                        byVer[(e.GetStr("key") ?? "", e.GetStr("version"))] = e.GetStr("uploadId");
                    foreach (var (k, d) in m.Datasets)
                        if (d != null && !HasUploadId(d))
                            d.UploadId = byVer.TryGetValue((k, d.Version), out var u) ? u : null;
                    await _store.SaveManifestAsync(m, ct);
                }

                var idx = await _store.ReadLogAsync(LogStreams.Runs, null, ct);
                if (idx.Any(e => !e.GetTruthy("runNo") && !e.GetTruthy("deleted")))
                {
                    var counters = new Dictionary<string, int>();
                    foreach (var e in idx)
                    {
                        if (e.GetTruthy("deleted")) continue;
                        var id = e.GetStr("id") ?? "";
                        var day = id.Length > 8 ? id[..8] : id;
                        if (e.GetTruthy("runNo"))
                        {
                            var rn = e.GetStr("runNo")!;
                            var last4 = rn.Length > 4 ? rn[^4..] : rn;
                            counters[day] = Math.Max(counters.GetValueOrDefault(day), int.Parse(last4, CultureInfo.InvariantCulture));
                            continue;
                        }
                        counters[day] = counters.GetValueOrDefault(day) + 1;
                        e["runNo"] = $"RUN-{day}-{counters[day]:0000}";
                    }
                    await _store.RewriteLogAsync(LogStreams.Runs, idx, null, ct);
                }
            }
        }

        // DatasetEntry keeps an explicit "uploadId" (even null) in UploadId; a missing key reads as null too.
        private static bool HasUploadId(DatasetEntry d) => d.UploadId != null;

        /// <summary>history_retention_days = 0 keeps everything forever (default).</summary>
        public async Task PruneHistoryAsync(CancellationToken ct = default)
        {
            var keep = _cfg.HistoryRetentionDays;
            if (keep <= 0) return;
            var cutoff = _clock.Epoch() - keep * 86400.0;
            var cutIso = DateTimeOffset.FromUnixTimeMilliseconds((long)(cutoff * 1000)).ToOffset(_clock.Now.Offset)
                .ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            foreach (var day in await _store.LogDaysAsync(LogStreams.Requests, ct))
                if (string.CompareOrdinal(day, cutIso) < 0) await _store.DeleteLogDayAsync(LogStreams.Requests, day, ct);
            var current = (await _store.GetManifestAsync(ct)).Datasets.Select(kv => (kv.Key, kv.Value?.Version)).ToHashSet();
            foreach (var b in await _store.ListBlobsAsync(BlobKinds.Dataset, ct))
                if (!current.Contains((b.Key, b.Version)) && b.Mtime < cutoff)
                    await _store.DeleteBlobAsync(BlobKinds.Dataset, b.Key, b.Version, ct);
            var cutDay = cutIso.Replace("-", "");
            foreach (var b in await _store.ListBlobsAsync(BlobKinds.Run, ct))
                if (!b.Deleted && string.CompareOrdinal(b.Version.Length > 8 ? b.Version[..8] : b.Version, cutDay) < 0)
                    await _store.DeleteBlobAsync(BlobKinds.Run, "", b.Version, ct);
        }
    }
}
