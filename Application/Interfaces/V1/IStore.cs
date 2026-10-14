using System.Text.Json.Nodes;
using Domain.Entities.Store;

namespace Application.Interfaces.V1
{
    /// <summary>
    /// The storage layer — port of storage.py (PgStore method set). Documents and log records are
    /// kept as raw JSON so everything v1.1.4 wrote reads back unchanged and vice versa.
    /// <list type="bullet">
    /// <item>documents: manifest, params, users, sessions, scr_mapping (<see cref="DocNames"/>)</item>
    /// <item>logs: uploads, batches, runs, requests (one per day), feedback (<see cref="LogStreams"/>)</item>
    /// <item>audit: one line per change</item>
    /// <item>blobs: "ds" = every uploaded data-file version, "run" = every saved Tool 2 run (gzip JSON)</item>
    /// </list>
    /// </summary>
    public interface IStore
    {
        /// <summary>"postgres" (storage.py kind).</summary>
        string Kind { get; }
        string Describe();

        /// <summary>Creates the schema (v2) if missing — storage.py SCHEMA. Waits for the database up to <paramref name="waitSeconds"/>.</summary>
        Task InitializeAsync(int waitSeconds = 0, CancellationToken ct = default);

        /// <summary>
        /// The server-wide lock (server.py LOCK) that makes read-modify-write sequences on the manifest
        /// and the logs atomic. Implemented as a process-wide <see cref="SemaphoreSlim"/> (one API
        /// process per database, as in v1.1.4). Not re-entrant: never acquire it twice in one call chain.
        /// </summary>
        Task<IDisposable> LockAsync(CancellationToken ct = default);

        // ── documents
        Task<JsonNode?> GetDocAsync(string name, CancellationToken ct = default);
        Task PutDocAsync(string name, JsonNode obj, CancellationToken ct = default);
        /// <summary>Changes whenever the document is written (doc_stamps) — 0 when never written.</summary>
        Task<long> DocStampAsync(string name, CancellationToken ct = default);

        // ── logs
        Task<List<JsonObject>> ReadLogAsync(string stream, string? day = null, CancellationToken ct = default);
        Task<Dictionary<string, List<JsonObject>>> ReadLogsAsync(string stream, IReadOnlyCollection<string> days, CancellationToken ct = default);
        Task AppendLogAsync(string stream, JsonObject obj, string? day = null, CancellationToken ct = default);
        Task RewriteLogAsync(string stream, IEnumerable<JsonObject> objs, string? day = null, CancellationToken ct = default);
        Task<List<string>> LogDaysAsync(string stream = LogStreams.Requests, CancellationToken ct = default);
        Task DeleteLogDayAsync(string stream, string day, CancellationToken ct = default);

        // ── audit
        Task AuditAppendAsync(string at, string who, string action, string detail, CancellationToken ct = default);
        Task<List<AuditLine>> AuditReadAsync(CancellationToken ct = default);

        // ── blobs ("ds" key=dataset key; "run" key ignored)
        Task PutBlobAsync(string kind, string key, string ver, byte[] data, bool deleted = false, CancellationToken ct = default);
        Task<byte[]?> GetBlobAsync(string kind, string key, string ver, CancellationToken ct = default);
        Task<byte[]?> GetBlobAnyAsync(string kind, string key, string ver, bool deleted, CancellationToken ct = default);
        Task<bool> BlobExistsAsync(string kind, string key, string ver, CancellationToken ct = default);
        Task<long?> BlobSizeAsync(string kind, string key, string ver, CancellationToken ct = default);
        Task MarkBlobDeletedAsync(string kind, string key, string ver, CancellationToken ct = default);
        Task DeleteBlobAsync(string kind, string key, string ver, CancellationToken ct = default);
        Task<List<BlobInfo>> ListBlobsAsync(string kind, CancellationToken ct = default);

        // ── misc
        Task<long> StorageBytesAsync(CancellationToken ct = default);
        Task<bool> IsEmptyAsync(CancellationToken ct = default);
    }
}
