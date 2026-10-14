using System.IO.Compression;
using System.Text.Json.Nodes;
using Application.Interfaces.V1;
using Application.Interfaces.V1.Analytics;
using Application.Interfaces.V1.Tool2;
using Domain.Entities.Store;
using Domain.Exceptions;
using Domain.Records;

namespace Application.Services.V1.Adapters
{
    /// <summary>Tool 2 runs are stored by the platform runs store (run_index + run_files, as in v1.1.4).</summary>
    public sealed class RunArchiveAdapter : IRunArchive
    {
        private readonly IRunService _runs;
        public RunArchiveAdapter(IRunService runs) => _runs = runs;

        public async Task<RunSaveResult> SaveRunAsync(byte[] gzipPayload, JsonObject meta, AppUserContext user, CancellationToken ct = default)
        {
            var res = await _runs.SaveAsync(gzipPayload, meta, user, ct);
            return new RunSaveResult(res.Id, res.Meta["runNo"]?.GetValue<string>(), res.Meta);
        }

        public async Task<byte[]?> GetRunPayloadAsync(string id, CancellationToken ct = default)
        {
            try { return await _runs.GetGzipAsync(id, ct); }
            catch (NotFoundException) { return null; }
        }

        public async Task<JsonObject?> GetRunMetaAsync(string id, CancellationToken ct = default)
        {
            var list = await _runs.ListAsync(null, null, ct);
            return list.Items.FirstOrDefault(e => e["id"]?.GetValue<string>() == id);
        }
    }

    public sealed class DatasetManifestReaderAdapter : IDatasetManifestReader
    {
        private readonly IDataService _data;
        public DatasetManifestReaderAdapter(IDataService data) => _data = data;

        public async Task<IReadOnlyDictionary<string, DatasetEntry>> GetCurrentDatasetsAsync(CancellationToken ct = default) =>
            (await _data.GetManifestAsync(ct)).Datasets;
    }

    /// <summary>
    /// Tool 2 result rows for the planner dashboard / AI: the batch result objects of a saved run
    /// (shared.js packed.results — the same fields as the browser's batchResults).
    /// </summary>
    public sealed class Tool2ResultSourceAdapter : ITool2ResultSource
    {
        private readonly IRunService _runs;
        public Tool2ResultSourceAdapter(IRunService runs) => _runs = runs;

        public async Task<Tool2RunRows?> GetLatestRowsAsync(AppUserContext user, CancellationToken ct = default)
        {
            var list = await _runs.ListAsync(null, null, ct);
            var latest = list.Items.FirstOrDefault()?["id"]?.GetValue<string>();
            return latest == null ? null : await GetRunRowsAsync(latest, user, ct);
        }

        public async Task<Tool2RunRows?> GetRunRowsAsync(string runId, AppUserContext user, CancellationToken ct = default)
        {
            byte[] gz;
            try { gz = await _runs.GetGzipAsync(runId, ct); }
            catch (NotFoundException) { return null; }
            using var src = new GZipStream(new MemoryStream(gz), CompressionMode.Decompress);
            var doc = JsonNode.Parse(src) as JsonObject;
            var rows = (doc?["packed"]?["results"] as JsonArray)?.OfType<JsonObject>().Select(o => o.DeepClone().AsObject()).ToList()
                       ?? new List<JsonObject>();
            return new Tool2RunRows(runId, rows);
        }
    }
}
