using Application.DTOs.Ingest;
using DecisionEngine.Model;
using DecisionEngine.Mra;
using DecisionEngine.Results.Mra;
using Domain.Records;

namespace Application.Interfaces.V1.Sap
{
    /// <summary>A stored feed message to add (one per received message, also a bad one).</summary>
    public sealed record MraFeedRow(string Kind, string RawJson, string? MsgJson, string? Error);

    /// <summary>What is stored: the last morning file and the messages to replay on top of it.</summary>
    public sealed class MraStoredState
    {
        public MraSnapshotInfo? Snapshot { get; set; }
        public Dictionary<string, MraFileRecord> File { get; set; } = new(StringComparer.Ordinal);
        public List<MraFeedEvent> Feed { get; set; } = new();
    }

    /// <summary>
    /// SAP MRA storage (database/sqlserver/05_sap_interface.sql v6: SAP_MRA_FILE / _FILE_UPLOADS / _FEED, PRO_SAP_MRA).
    /// SQL Server only: with PostgreSQL storage <see cref="Available"/> is false and every call throws 503.
    /// </summary>
    public interface IMraRepository
    {
        bool Available { get; }
        string? UnavailableReason { get; }

        /// <summary>Morning file: replaces the MRA list in one transaction.</summary>
        Task UploadAsync(IReadOnlyDictionary<string, MraFileRecord> mras, string uploadId, string uploadDay, string? fileName, string by, string countsJson,
                         CancellationToken ct = default);

        /// <summary>The last file and the messages received since its upload day (at most 31 days), in arrival order.</summary>
        Task<MraStoredState> StateAsync(string today, CancellationToken ct = default);

        /// <summary>"last upload id / max seq": changes whenever a file is uploaded or a message is received.</summary>
        Task<string> StampAsync(CancellationToken ct = default);

        /// <summary>Stores the messages in order; returns their Seq numbers in the same order.</summary>
        Task<List<long>> AddFeedAsync(IReadOnlyList<MraFeedRow> rows, string by, string? sourceSystem, string? traceId, DateTimeOffset receivedAt,
                                      CancellationToken ct = default);
    }

    /// <summary>
    /// The current MRA position for the engines (New PO request, SAP decisions, single concept Recommendation): replayed
    /// state + classification, cached per (data version, stamp, day, switch 2). Null when MRAs are not available
    /// (PostgreSQL storage) — the engines then count nothing extra.
    /// </summary>
    public interface IMraCapacitySource
    {
        Task<MraCapacity?> GetAsync(EngineData data, CancellationToken ct = default);
        /// <summary>The replayed state + feed (for the pages), with the capacity built on it.</summary>
        Task<(MraStoredState Stored, MraCapacity Capacity)?> GetFullAsync(EngineData data, CancellationToken ct = default);
        void Invalidate();
    }

    /// <summary>POST /api/sap/v1/mra-events and /po-events: live changes from SAP.</summary>
    public interface ISapMraFeedService
    {
        /// <param name="kind">"mra" or "po".</param>
        Task<SapCallResult> ReceiveAsync(string kind, string body, SapCallContext caller, CancellationToken ct = default);
    }

    /// <summary>Data Setup → MRA file, MRA Queues and SAP Feed pages.</summary>
    public interface IMraService
    {
        /// <summary>Reads the morning workbook; when it is readable, replaces the MRA list with it.</summary>
        Task<MraUploadResponse> UploadAsync(IngestUpload file, AppUserContext user, CancellationToken ct = default);
        Task<MraStateResponse> StateAsync(AppUserContext user, CancellationToken ct = default);
        Task<MraSwitchesResponse> SetSwitchesAsync(MraSwitchesReq req, AppUserContext user, CancellationToken ct = default);
        /// <summary>SAP Feed page test sender (admin): stored and applied exactly like a message from SAP.</summary>
        Task<MraFeedResponse> TestSendAsync(MraTestSendReq req, AppUserContext user, CancellationToken ct = default);
    }
}
