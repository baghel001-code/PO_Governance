using Application.DTOs.Ingest;
using Domain.Entities.Sap;
using Domain.Records;

namespace Application.Interfaces.V1.Sap
{
    /// <summary>
    /// MRA status storage (database/sqlserver/05_sap_interface.sql: SAP_MRA_STATUS / _HISTORY / _UPLOADS, PRO_SAP_MRA_STATUS).
    /// SQL Server only: with PostgreSQL storage <see cref="Available"/> is false and every call throws 503.
    /// </summary>
    public interface IMraRepository
    {
        bool Available { get; }
        string? UnavailableReason { get; }

        /// <summary>Morning file: merges <paramref name="lines"/> into SAP_MRA_STATUS in one transaction (added / updated; lines not in the file stay).</summary>
        Task<MraUploadCounts> UploadAsync(IReadOnlyList<MraLine> lines, string uploadId, string? fileName, string by, CancellationToken ct = default);

        /// <summary>SAP changes, in order, in one transaction: one result per line (CREATED / UPDATED / UNCHANGED / STALE / AMBIGUOUS).</summary>
        Task<List<MraApplyRow>> ApplyAsync(IReadOnlyList<MraLine> lines, string by, string? sourceSystem, string batchId, string? traceId, CancellationToken ct = default);

        Task<MraListResponse> ListAsync(MraListReq req, CancellationToken ct = default);
        Task<MraSummary> SummaryAsync(DateTimeOffset since, CancellationToken ct = default);
        Task<List<MraHistoryRow>> HistoryAsync(string mraNo, CancellationToken ct = default);
    }

    /// <summary>POST /api/sap/v1/mra-status: MRA status changes from SAP during the day.</summary>
    public interface ISapMraService
    {
        Task<SapCallResult> ReceiveAsync(string body, SapCallContext caller, CancellationToken ct = default);
    }

    /// <summary>Data Setup → MRA status: the morning file and the views of the current status.</summary>
    public interface IMraService
    {
        /// <summary>Reads the morning file; when it is valid, merges it into the MRA lines. A refused file changes nothing.</summary>
        Task<MraUploadResponse> UploadAsync(IngestUpload file, AppUserContext user, CancellationToken ct = default);
        Task<MraSummaryResponse> SummaryAsync(CancellationToken ct = default);
        Task<MraListResponse> ListAsync(MraListReq req, CancellationToken ct = default);
        Task<MraHistoryResponse> HistoryAsync(string? mraNo, CancellationToken ct = default);
    }
}
