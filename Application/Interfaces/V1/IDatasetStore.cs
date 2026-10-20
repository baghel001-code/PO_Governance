using DecisionEngine.Model;
using Domain.Entities.Store;
using Domain.Records;

namespace Application.Interfaces.V1
{
    /// <summary>
    /// Saving parsed data files (server.py PUT /api/data/&lt;key&gt; and POST /api/uploads). Every save
    /// keeps the old versions (history), bumps the manifest dataVersion, writes the upload log and the
    /// audit line, invalidates the dataset cache, and — for the SCR products file (catheadmap) — creates
    /// logins for new buyers / category heads.
    /// </summary>
    public interface IDatasetStore
    {
        /// <summary>Starts one upload action (a zip, a set of files) and returns its Upload ID (UPL-yyyyMMdd-0001).</summary>
        Task<string> NewUploadBatchAsync(string source, IEnumerable<string> sourceFiles, AppUserContext user, string note = "", CancellationToken ct = default);

        /// <summary>Stores a new version of <paramref name="key"/> and makes it current.</summary>
        Task<DatasetSaveResult> SaveVersionAsync(string key, DatasetPayload payload, string label, string? uploadId, AppUserContext user, CancellationToken ct = default);
    }

    public sealed record DatasetSaveResult(long DataVersion, DatasetEntry Entry, object? UsersSync = null, string? UsersError = null);
}
