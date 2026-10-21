using Domain.Entities.Sap;

namespace Application.Interfaces.V1.Sap
{
    /// <summary>
    /// Persistence of the SAP interface (database/sqlserver/05_sap_interface.sql, PRO_SAP_* procedures). The SQL Server
    /// implementation is Infrastructure/Repositories/SapSqlServerRepository; with PostgreSQL storage
    /// <see cref="Available"/> is false and every call throws 503 "SAP interface requires SQL Server storage".
    /// </summary>
    public interface ISapRepository
    {
        bool Available { get; }
        string? UnavailableReason { get; }

        // ── technical users (PRO_SAP_TECH_USER)
        Task<SapTechUser?> GetTechUserAsync(string userName, CancellationToken ct = default);
        Task<List<SapTechUser>> ListTechUsersAsync(CancellationToken ct = default);
        Task<SapTechUser> AddTechUserAsync(string userName, string sourceSystem, string? description, string passwordHash, string by, CancellationToken ct = default);
        Task<SapTechUser> SetTechUserPasswordAsync(string userName, string passwordHash, string by, CancellationToken ct = default);
        Task<SapTechUser> SetTechUserActiveAsync(string userName, bool active, string by, CancellationToken ct = default);
        Task<SapTechUser> UnlockTechUserAsync(string userName, CancellationToken ct = default);
        Task LoginOkAsync(string userName, string ip, CancellationToken ct = default);
        /// <summary>Counts a failed login (locks the user after <paramref name="maxFailed"/>); returns the row (null = unknown user).</summary>
        Task<SapTechUser?> LoginFailAsync(string userName, int maxFailed, int lockoutMinutes, CancellationToken ct = default);

        // ── decisions (PRO_SAP_DECISION_SAVE / PRO_SAP_DECISION_GET)
        /// <summary>The answered (HTTP 200) decision of a request_id with its body hash and response JSON; null when none.</summary>
        Task<SapDecisionLogRow?> GetDecisionByRequestAsync(string requestId, CancellationToken ct = default);
        Task<long> NextDecisionSeqAsync(CancellationToken ct = default);
        /// <summary>Stores a decision log row and its items. Throws <see cref="Domain.Exceptions.SapRequestIdTakenException"/> when an answered row for the request_id exists.</summary>
        Task<long> SaveDecisionAsync(SapDecisionLogRow row, IReadOnlyList<SapDecisionItemRow> items, CancellationToken ct = default);
        Task<SapDecisionListResponse> ListDecisionsAsync(SapDecisionListReq req, int tzOffsetMinutes, CancellationToken ct = default);
        Task<SapDecisionDetailResponse> GetDecisionAsync(long id, CancellationToken ct = default);
        Task<SapDecisionStats> DecisionStatsAsync(string today, int tzOffsetMinutes, CancellationToken ct = default);

        // ── events (PRO_SAP_EVENTS_SAVE / PRO_SAP_EVENTS_GET)
        /// <summary>Stores a batch in one transaction; one result per event in batch order (ACCEPTED / PARKED / DUPLICATE).</summary>
        Task<List<SapEventSaveResult>> SaveEventsAsync(IReadOnlyList<SapEventInsert> events, string? queue, string? sourceSystem, string? client,
                                                       string? techUser, string batchId, CancellationToken ct = default);
        Task<SapEventStatus> EventStatusAsync(string today, int tzOffsetMinutes, CancellationToken ct = default);
        Task<SapEventListResponse> ListEventsAsync(SapEventListReq req, int tzOffsetMinutes, CancellationToken ct = default);
        Task<SapEventRow?> GetEventAsync(long id, CancellationToken ct = default);

        // ── reason codes (PRO_SAP_REASON_CODES)
        /// <summary>Active EXCEPTION codes (GET /reason-codes) and the list version (date of the last change).</summary>
        Task<(List<SapReasonCodeRow> Codes, string Version)> ActiveExceptionCodesAsync(int tzOffsetMinutes, CancellationToken ct = default);
        Task<List<SapReasonCodeRow>> ListReasonCodesAsync(string? codeType, CancellationToken ct = default);
        Task<SapReasonCodeRow> SaveReasonCodeAsync(SapReasonCodeSaveReq req, string by, CancellationToken ct = default);
    }

    /// <summary>Who is calling the SAP interface (from the SapBasic authentication) and how.</summary>
    public sealed record SapCallContext(string TechUser, string SourceSystem, string ClientIp, string TraceId, bool IsTest = false);

    /// <summary>A finished SAP call: HTTP status and the exact JSON body to send (Error body for non-200).</summary>
    public sealed class SapCallResult
    {
        public int Status { get; set; } = 200;
        public string Json { get; set; } = "{}";
        public string? ErrorCode { get; set; }
        public long? LogId { get; set; }
        public int DurationMs { get; set; }
        /// <summary>True when an earlier answer for the same request_id + body was returned (idempotent replay).</summary>
        public bool Replayed { get; set; }
        /// <summary>Elapsed milliseconds after each step (lookup, data, decide, answer, saved) — written to the log.</summary>
        public string? Phases { get; set; }
    }

    /// <summary>POST /decisions (spec v1.4 §6).</summary>
    public interface ISapDecisionService
    {
        /// <summary>Validates, de-duplicates by request_id, decides within SapInterface:DecisionTimeoutMs, logs, and returns the answer.</summary>
        Task<SapCallResult> DecideAsync(string body, SapCallContext caller, CancellationToken ct = default);
    }

    /// <summary>POST /events (spec v1.4 §5).</summary>
    public interface ISapEventService
    {
        Task<SapCallResult> ReceiveAsync(string body, SapCallContext caller, CancellationToken ct = default);
        /// <summary>The feed status (cached for a couple of seconds) — data_delayed / events_lag_seconds of the decisions.</summary>
        Task<SapEventStatus> StatusAsync(CancellationToken ct = default);
    }

    /// <summary>Result of a Basic-auth check of a technical user.</summary>
    public sealed record SapAuthResult(bool Ok, SapTechUser? User, string ErrorCode, string Message);

    /// <summary>HTTP Basic authentication of the technical users (PBKDF2 hashes, lockout, audit lines).</summary>
    public interface ISapAuthService
    {
        Task<SapAuthResult> AuthenticateAsync(string userName, string password, string clientIp, CancellationToken ct = default);
    }

    /// <summary>GET /ping and GET /reason-codes.</summary>
    public interface ISapUtilityService
    {
        Task<SapPingResponse> PingAsync(SapCallContext caller, CancellationToken ct = default);
        Task<SapReasonCodesResponse> ReasonCodesAsync(CancellationToken ct = default);
    }

    /// <summary>The portal's SAP monitoring screen (/admin/sap).</summary>
    public interface ISapAdminService
    {
        Task<SapOverviewResponse> OverviewAsync(CancellationToken ct = default);
        Task<SapDecisionListResponse> ListDecisionsAsync(SapDecisionListReq req, CancellationToken ct = default);
        Task<SapDecisionDetailResponse> GetDecisionAsync(long id, CancellationToken ct = default);
        Task<SapEventListResponse> ListEventsAsync(SapEventListReq req, CancellationToken ct = default);
        Task<SapEventRow> GetEventAsync(long id, CancellationToken ct = default);
        Task<SapTechUsersResponse> ListTechUsersAsync(CancellationToken ct = default);
        Task<SapTechUserResponse> AddTechUserAsync(SapTechUserAddReq req, string by, CancellationToken ct = default);
        Task<SapTechUserResponse> UpdateTechUserAsync(SapTechUserUpdateReq req, string by, CancellationToken ct = default);
        Task<SapReasonCodesListResponse> ListReasonCodesAsync(CancellationToken ct = default);
        Task<SapReasonCodeRow> SaveReasonCodeAsync(SapReasonCodeSaveReq req, string by, CancellationToken ct = default);
        /// <summary>Runs a request body through the real decision path (logged as a test, mode as sent — SHADOW in the sample).</summary>
        Task<SapTestDecisionResponse> TestDecisionAsync(SapTestDecisionReq req, string by, string clientIp, CancellationToken ct = default);
        /// <summary>A sample request built from the uploaded data (first GM + colour of the stock file).</summary>
        Task<string> SampleRequestAsync(CancellationToken ct = default);
    }
}
