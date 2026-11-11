using Application.DTOs.Ingest;
using DecisionEngine.Model;
using DecisionEngine.Mra;
using DecisionEngine.Results.Mra;
using Domain.Records;

namespace Application.Interfaces.V1.Sap
{
    /// <summary>A stored feed message to add (one per received message, also a bad one).</summary>
    public sealed record MraFeedRow(string Kind, string RawJson, string? MsgJson, string? Error);

    /// <summary>The decision's verdict link (view_url) of an MRA number and when it expires.</summary>
    public sealed record MraVerdictLink(string ViewUrl, string? DecisionId, DateTimeOffset? ExpiresAt);

    /// <summary>A stored message: its number, and whether it waits in the pending table (today's file not uploaded yet).</summary>
    public sealed record MraFeedStored(long Seq, bool Pending);

    /// <summary>What is stored: the last morning file, the messages to replay on top of it and the pending ones.</summary>
    public sealed class MraStoredState
    {
        public MraControl Control { get; set; } = new();
        public MraSnapshotInfo? Snapshot { get; set; }
        /// <summary>v7: SAP changes received while today's file is not uploaded (applied when it is).</summary>
        public List<MraFeedEvent> Pending { get; set; } = new();
        public Dictionary<string, MraFileRecord> File { get; set; } = new(StringComparer.Ordinal);
        public List<MraFeedEvent> Feed { get; set; } = new();
    }

    /// <summary>
    /// SAP MRA storage (database/sqlserver/05_sap_interface.sql v7: SAP_MRA_FILE / _FILE_UPLOADS / _FEED / _FEED_PENDING / _CONTROL, PRO_SAP_MRA).
    /// SQL Server only: with PostgreSQL storage <see cref="Available"/> is false and every call throws 503.
    /// </summary>
    public interface IMraRepository
    {
        bool Available { get; }
        string? UnavailableReason { get; }

        /// <summary>Morning file: replaces the MRA list, moves the pending SAP changes onto it (one transaction). Returns how many were moved.</summary>
        Task<int> UploadAsync(IReadOnlyDictionary<string, MraFileRecord> mras, string uploadId, string uploadDay, string? fileName, string by, string countsJson,
                         CancellationToken ct = default);

        /// <summary>Control (IsDailyDataUpload, data version), the last file, its messages (apply order) and the pending messages.</summary>
        Task<MraStoredState> StateAsync(string today, CancellationToken ct = default);

        /// <summary>Changes whenever a file is uploaded, a message is received or a new day starts (IsDailyDataUpload of <paramref name="today"/>).</summary>
        Task<string> StampAsync(string today, CancellationToken ct = default);

        /// <summary>
        /// Stores the messages in order — in the feed when today's file is uploaded (IsDailyDataUpload), else in the pending
        /// table; returns their numbers in the same order.
        /// </summary>
        Task<List<MraFeedStored>> AddFeedAsync(IReadOnlyList<MraFeedRow> rows, string by, string? sourceSystem, string? traceId, DateTimeOffset receivedAt,
                                      CancellationToken ct = default);

        /// <summary>v8: the tool's answered SAP decision items that have a document (MRA number) stored by POST /documents (newest first). <paramref name="since"/> is not used (kept for the call shape).</summary>
        Task<List<MraToolDecision>> DecisionsAsync(DateTime since, CancellationToken ct = default);

        /// <summary>v8: the working saved with decision item <paramref name="itemId"/> (JSON), or null.</summary>
        Task<string?> WorkingAsync(long itemId, CancellationToken ct = default);

        /// <summary>The verdict link (view_url) of the latest decision SAP stored MRA <paramref name="mraNo"/> for (POST /documents), or null.</summary>
        Task<MraVerdictLink?> VerdictLinkAsync(string mraNo, CancellationToken ct = default);
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

        /// <summary>GET /verdict-url?mra_no=: only the verdict page link of the MRA (404 MRA_NOT_FOUND when none).</summary>
        Task<SapCallResult> VerdictUrlAsync(string? mraNo, SapCallContext caller, CancellationToken ct = default);
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
        /// <summary>v8: the tool's decision of one MRA with the working saved with it (side panel of MRA Queues).</summary>
        Task<MraDecisionDetail> DecisionAsync(string mraNo, AppUserContext user, CancellationToken ct = default);
        /// <summary>
        /// The MRA page behind GET /api/sap/v1/verdict-url (portal /mra-verdict/&lt;token&gt;/&lt;mra&gt;, no sign-in): the same
        /// content as the MRA Queues panel. Valid only when <paramref name="token"/> is the verdict link token of the decision
        /// SAP stored this MRA number for; 404 otherwise, 410 when the link expired.
        /// </summary>
        Task<MraVerdictView> VerdictViewAsync(string token, string mraNo, CancellationToken ct = default);
    }
}
