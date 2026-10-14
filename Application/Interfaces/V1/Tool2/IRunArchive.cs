using System.Text.Json.Nodes;
using Domain.Entities.Store;
using Domain.Records;

namespace Application.Interfaces.V1.Tool2
{
    /// <summary>Result of saving a Tool 2 run (server.py api_POST_runs: {ok, id, meta}).</summary>
    public sealed record RunSaveResult(string Id, string? RunNo, JsonObject Meta);

    /// <summary>
    /// Saved Tool 2 runs (server.py /api/runs; tables run_index + run_files). Implemented by the platform
    /// runs store. The payload is the gzip JSON v1.1.4's <c>Shared.saveRun</c> uploaded
    /// (<c>{params, dataVersions, packed}</c>); the meta is the X-Run-Meta index record
    /// (<c>{lines, counts, atRisk, valueAtRisk, dataVersions:{key:version}, T2, D}</c>) to which the store adds
    /// id, at, by, bytes and runNo ("RUN-…").
    /// </summary>
    public interface IRunArchive
    {
        Task<RunSaveResult> SaveRunAsync(byte[] gzipPayload, JsonObject meta, AppUserContext user, CancellationToken ct = default);

        /// <summary>The stored gzip payload, null when the run does not exist (or was deleted).</summary>
        Task<byte[]?> GetRunPayloadAsync(string id, CancellationToken ct = default);

        /// <summary>The index record of a run (id, runNo, at, by, lines, counts …), null when unknown.</summary>
        Task<JsonObject?> GetRunMetaAsync(string id, CancellationToken ct = default);
    }

    /// <summary>
    /// The manifest's current dataset entries ({key: {version, rows, updatedAt, …}}) — what v1.1.4 put in a run's
    /// <c>dataVersions</c> (Shared.status.datasets). Implemented by the platform store.
    /// </summary>
    public interface IDatasetManifestReader
    {
        Task<IReadOnlyDictionary<string, DatasetEntry>> GetCurrentDatasetsAsync(CancellationToken ct = default);
    }
}
