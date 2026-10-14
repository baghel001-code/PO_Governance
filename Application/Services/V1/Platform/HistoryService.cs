using System.Text.Json.Nodes;
using Application.DTOs.Platform;
using Application.Interfaces.V1;
using Domain.Entities.AppSettings;
using Domain.Entities.Store;
using Domain.Records;
using Microsoft.Extensions.Options;

namespace Application.Services.V1.Platform
{
    /// <summary>server.py api_GET_history (uploads grouped by Upload ID; requests incl. cleared ones).</summary>
    public sealed class HistoryService : IHistoryService
    {
        private readonly IStore _store;
        private readonly IAccountsService _acc;
        private readonly PoGovSettings _cfg;

        public HistoryService(IStore store, IAccountsService acc, IOptions<PoGovSettings> cfg)
        {
            _store = store;
            _acc = acc;
            _cfg = cfg.Value;
        }

        public async Task<UploadHistoryResponse> UploadsAsync(string? from, string? to, CancellationToken ct = default)
        {
            PlatformCore.CheckRange(from, to);
            Dictionary<string, string?> cur;
            List<JsonObject> entries;
            Dictionary<string, JsonObject> batches = new();
            HashSet<(string, string)> stored;
            using (await _store.LockAsync(ct))
            {
                cur = (await _store.GetManifestAsync(ct)).Datasets.ToDictionary(kv => kv.Key, kv => kv.Value?.Version);
                entries = await _store.ReadLogAsync(LogStreams.Uploads, null, ct);
                foreach (var b in await _store.ReadLogAsync(LogStreams.Batches, null, ct))
                    batches[b.GetStr("uploadId") ?? ""] = b;
                stored = (await _store.ListBlobsAsync(BlobKinds.Dataset, ct)).Select(x => (x.Key, x.Version)).ToHashSet();
            }
            var groups = new Dictionary<string, UploadHistoryGroup>();
            foreach (var e in entries)
            {
                var uid = e.GetStr("uploadId");
                if (string.IsNullOrEmpty(uid))
                {
                    // uploaded before Upload IDs existed: group by minute + user
                    var t = e.GetStrOrEmpty("updatedAt");
                    uid = $"UPL-{Slice(t, 0, 10).Replace("-", "")}-L{Slice(t, 11, 16).Replace(":", "")}";
                }
                var key = e.GetStr("key");
                var ver = e.GetStr("version");
                e["stored"] = e.GetTruthy("version") && stored.Contains((key ?? "", ver ?? ""));
                e["current"] = (cur.TryGetValue(key ?? "", out var cv) ? cv : null) == ver && e.GetStr("action") != "clear";
                if (!groups.TryGetValue(uid, out var g))
                {
                    batches.TryGetValue(uid, out var b);
                    g = groups[uid] = new UploadHistoryGroup
                    {
                        UploadId = uid,
                        At = b.GetTruthy("at") ? b.GetStr("at") : e.GetStr("updatedAt"),
                        By = b.GetTruthy("by") ? b.GetStr("by") : e.GetStr("updatedBy"),
                        Source = b.GetTruthy("source") ? b.GetStr("source") : e.GetStr("action"),
                        SourceFiles = b.GetTruthy("sourceFiles") ? b.GetStrList("sourceFiles") : new List<string>(),
                        Note = b != null && b.ContainsKey("note") ? b.GetStr("note") : ""
                    };
                }
                g.Files.Add(e);
            }
            var items = groups.Values.Where(g => PlatformCore.InRange(g.At, from, to))
                .OrderByDescending(g => g.At ?? "", StringComparer.Ordinal).ToList();
            return new UploadHistoryResponse { Items = items, StorageBytes = await _store.StorageBytesAsync(ct) };
        }

        private static string Slice(string s, int a, int b) => a >= s.Length ? "" : s[a..Math.Min(b, s.Length)];

        public async Task<ItemsResponse> RequestsAsync(string? from, string? to, AppUserContext me, CancellationToken ct = default)
        {
            PlatformCore.CheckRange(from, to);
            var items = new List<JsonObject>();
            using (await _store.LockAsync(ct))
            {
                var days = (await _store.LogDaysAsync(LogStreams.Requests, ct))
                    .Where(d => !((!string.IsNullOrEmpty(from) && string.CompareOrdinal(d, from) < 0) || (!string.IsNullOrEmpty(to) && string.CompareOrdinal(d, to) > 0)))
                    .ToList();
                var byDay = await _store.ReadLogsAsync(LogStreams.Requests, days, ct);
                foreach (var d in days)
                {
                    var (reqs, _) = PlatformCore.Replay(byDay.TryGetValue(d, out var l) ? l : new List<JsonObject>(), includeCleared: true);
                    foreach (var r in reqs)
                        if (!r.GetTruthy("reqId"))
                        {
                            var n = r.GetTruthy("seq") ? r.GetLong("seq") : r.GetTruthy("id") ? r.GetLong("id") : 0;
                            r["reqId"] = $"REQ-{d.Replace("-", "")}-{n:0000}";
                        }
                    items.AddRange(reqs);
                }
            }
            var scope = await _acc.ScopeIndexAsync(ct);
            items = items.Where(r => PlatformCore.VisibleTo(me, r, scope, _cfg))
                         .OrderByDescending(r => r.GetStr("savedAt") ?? "", StringComparer.Ordinal).ToList();
            return new ItemsResponse { Items = items };
        }
    }
}
