using System.Text.Json;
using System.Text.Json.Nodes;
using Application.Interfaces.V1;
using Application.Services.V1.Platform;
using Domain.Constants;
using Domain.Entities.Common;
using Domain.Entities.Engine;
using Domain.Entities.Store;
using Microsoft.Extensions.Logging;

namespace Infrastructure.Services.V1
{
    /// <summary>
    /// In-memory <see cref="EngineData"/> snapshot: the current version of every data file in the
    /// manifest (gunzipped rows) plus the params document. Rebuilt when the manifest's dataVersion /
    /// paramsVersion change or after <see cref="Invalidate"/>. Thread-safe: one rebuild at a time,
    /// readers get the last complete snapshot.
    /// </summary>
    public sealed class DatasetCache : IDatasetCache
    {
        private readonly IStore _store;
        private readonly ILogger<DatasetCache> _log;
        private readonly SemaphoreSlim _gate = new(1, 1);
        private volatile EngineData? _current;
        // data blobs already loaded, per (key, version) — a params change does not re-read the files
        private Dictionary<(string, string), DatasetPayload> _loaded = new();

        public DatasetCache(IStore store, ILogger<DatasetCache> log)
        {
            _store = store;
            _log = log;
        }

        public void Invalidate() => _current = null;

        public async Task<EngineData> GetCurrentAsync(CancellationToken ct = default)
        {
            var m = await _store.GetManifestAsync(ct);
            var cur = _current;
            if (cur != null && cur.DataVersion == m.DataVersion && cur.ParamsVersion == m.ParamsVersion) return cur;
            await _gate.WaitAsync(ct);
            try
            {
                cur = _current;
                if (cur != null && cur.DataVersion == m.DataVersion && cur.ParamsVersion == m.ParamsVersion) return cur;
                m = await _store.GetManifestAsync(ct);
                var files = new Dictionary<string, IReadOnlyList<SheetRow>?>();
                var loaded = new Dictionary<(string, string), DatasetPayload>();
                string? grcType = null;
                foreach (var (key, e) in m.Datasets)
                {
                    if (e == null || string.IsNullOrEmpty(e.Version)) continue;
                    var p = await LoadAsync(key, e.Version, ct);
                    if (p == null)
                    {
                        _log.LogWarning("Current version {Version} of {Key} is not stored", e.Version, key);
                        continue;
                    }
                    loaded[(key, e.Version)] = p;
                    files[key] = p.Rows;
                    if (key == DatasetKeys.Grc)   // shared.js loadDatasets: meta → rows[0].periodType → 'month'
                        grcType = p.MetaString("grcPeriodType")
                                  ?? (p.Rows.Count > 0 && Js.Truthy(p.Rows[0]["periodType"]) ? Js.ToJsString(p.Rows[0]["periodType"]) : null)
                                  ?? "month";
                }
                _loaded = loaded;
                var prm = ParseParams(await _store.GetDocAsync(DocNames.Params, ct));
                cur = new EngineData(files, prm, m.DataVersion, m.ParamsVersion, grcType);
                _current = cur;
                return cur;
            }
            finally { _gate.Release(); }
        }

        private async Task<DatasetPayload?> LoadAsync(string key, string version, CancellationToken ct)
        {
            if (_loaded.TryGetValue((key, version), out var p)) return p;
            var gz = await _store.GetBlobAsync(BlobKinds.Dataset, key, version, ct);
            return gz == null ? null : DatasetPayload.FromGzip(gz);
        }

        public static EngineParams ParseParams(JsonNode? node)
        {
            if (node is not JsonObject o || o.Count == 0) return new EngineParams();
            try { return o.Deserialize<EngineParams>(PyJson.Options) ?? new EngineParams(); }
            catch (JsonException)
            {
                // tolerate odd values (e.g. a number typed as text): take what parses, keep defaults otherwise
                var p = new EngineParams();
                foreach (var kv in o)
                {
                    try
                    {
                        var one = new JsonObject { [kv.Key] = kv.Value?.DeepClone() }.Deserialize<EngineParams>(PyJson.Options);
                        if (one == null) continue;
                        var prop = typeof(EngineParams).GetProperties().FirstOrDefault(pi =>
                            pi.GetCustomAttributes(typeof(System.Text.Json.Serialization.JsonPropertyNameAttribute), false)
                              .OfType<System.Text.Json.Serialization.JsonPropertyNameAttribute>().Any(a => a.Name == kv.Key));
                        if (prop != null) prop.SetValue(p, prop.GetValue(one));
                        else if (kv.Value != null) (p.Extra ??= new())[kv.Key] = JsonSerializer.SerializeToElement(kv.Value);
                    }
                    catch (JsonException) { }
                }
                return p;
            }
        }

        public async Task<EngineData> GetForVersionsAsync(IReadOnlyDictionary<string, string> versions, EngineParams p, CancellationToken ct = default)
        {
            var files = new Dictionary<string, IReadOnlyList<SheetRow>?>();
            string? grcType = null;
            foreach (var (key, ver) in versions)
            {
                var payload = await LoadAsync(key, ver, ct);
                files[key] = payload?.Rows;
                if (key == DatasetKeys.Grc && payload != null)   // shared.js openRun: meta → 'month'
                    grcType = payload.MetaString("grcPeriodType") ?? "month";
            }
            return new EngineData(files, p, -1, -1, grcType);
        }
    }
}
