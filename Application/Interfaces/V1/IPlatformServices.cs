using System.Text.Json.Nodes;
using Application.DTOs.Platform;
using DecisionEngine.Model;
using Domain.Entities.Store;
using Domain.Records;

namespace Application.Interfaces.V1
{
    /// <summary>PBKDF2-HMAC-SHA256 (200 000 iterations), "base64(salt)$base64(dk)" — accounts.py hash_password / check_password.</summary>
    public interface IPasswordHasher
    {
        string Hash(string password);
        bool Verify(string password, string? stored);
    }

    /// <summary>The audit trail (server.py _audit, GET /api/audit).</summary>
    public interface IAuditService
    {
        Task AppendAsync(string who, string action, string detail = "", CancellationToken ct = default);
        /// <summary>from / to = YYYY-MM-DD (inclusive), q = case-insensitive text; newest first, max 5000.</summary>
        Task<AuditQueryResponse> QueryAsync(string? from, string? to, string? q, CancellationToken ct = default);
    }

    /// <summary>The current SCR products (catheadmap) file, cached per version (server.py scr_rows).</summary>
    public interface IScrSource
    {
        Task<(string? Version, IReadOnlyList<SheetRow> Rows)> GetScrRowsAsync(CancellationToken ct = default);
    }

    /// <summary>
    /// Users, sessions, SCR-based user sync and MC scope — port of accounts.py <c>Accounts</c>.
    /// Validation errors throw <see cref="Domain.Exceptions.AppValidationException"/> with the v1.1.4 texts.
    /// </summary>
    public interface IAccountsService
    {
        /// <summary>Loads users / sessions and seeds the admin (accounts.py __init__ / _seed_admin).</summary>
        Task InitializeAsync(CancellationToken ct = default);

        Task<IReadOnlyList<AppUser>> GetUsersAsync(CancellationToken ct = default);
        Task<AppUser?> GetUserAsync(string email, CancellationToken ct = default);
        string DefaultPassword { get; }
        UserPublicDto Public(AppUser u);

        /// <summary>
        /// Returns (token, null) or (null, error message). <paramref name="login"/> = Employee ID (HRMS sign-in: the password is
        /// checked by HRMS, and only for employees registered here) or email (local accounts).
        /// </summary>
        Task<LoginResult> LoginAsync(string login, string password, string ip, CancellationToken ct = default);
        /// <summary>The active user of a live session (idle timeout; "last seen" persisted every 5 min), else null.</summary>
        Task<AppUser?> SessionUserAsync(string? token, CancellationToken ct = default);
        /// <summary>As <see cref="SessionUserAsync"/>, plus why the session ended (Domain.Constants.SessionEndReasons; null = unknown token).</summary>
        Task<(AppUser? User, string? EndReason)> SessionAsync(string? token, CancellationToken ct = default);
        Task LogoutAsync(string token, CancellationToken ct = default);
        /// <summary>Null on success, else the error message.</summary>
        Task<string?> ChangePasswordAsync(string email, string oldPassword, string newPassword, CancellationToken ct = default);
        string? ValidateNewPassword(string email, string password);
        /// <summary>Ends the user's sessions except <paramref name="keep"/>; they are told <paramref name="reason"/> (SessionEndReasons).</summary>
        Task EndSessionsAsync(string email, string? keep = null, string reason = Domain.Constants.SessionEndReasons.Changed, CancellationToken ct = default);

        /// <summary>A local account (Local sign-in mode only).</summary>
        Task<AppUser> AddUserAsync(string email, string name, IEnumerable<string> roles, string by, string source = "manual", CancellationToken ct = default);
        /// <summary>The employee as HRMS has them, and the login already registered with that code.</summary>
        Task<(HrmsEmployee Employee, AppUser? Registered)> LookupEmployeeAsync(string empCode, CancellationToken ct = default);
        /// <summary>Registers an employee with the details fetched from HRMS (signs in with Employee ID + HRMS password).</summary>
        Task<AppUser> AddEmployeeAsync(string empCode, IEnumerable<string> roles, string by, string? email = null, string source = "manual", CancellationToken ct = default);
        /// <summary>Links a login to an Employee ID, or re-reads its HRMS details (<paramref name="empCode"/> empty).</summary>
        Task<AppUser> LinkEmployeeAsync(string email, string? empCode, string by, CancellationToken ct = default);
        /// <summary>changes: {name?, roles?, active?, unlock?} — every check runs before anything is applied.</summary>
        Task<AppUser> UpdateUserAsync(string email, JsonObject changes, string by, CancellationToken ct = default);
        Task ResetPasswordAsync(string email, string by, CancellationToken ct = default);
        Task DeleteUserAsync(string email, string by, CancellationToken ct = default);

        Task<ScrMapping> GetMappingAsync(CancellationToken ct = default);
        Task SaveMappingAsync(ScrMapping mapping, string by, CancellationToken ct = default);
        ScrAnalysis AnalyseScr(IReadOnlyList<SheetRow> rows);
        Dictionary<string, ScrPerson> ScrPeople(IReadOnlyList<SheetRow> rows, ScrMapping mapping);
        Task<ScrSyncResult> SyncFromScrAsync(IReadOnlyList<SheetRow> rows, string by, CancellationToken ct = default);
        /// <summary>People and approvers of the current SCR file, cached per SCR version.</summary>
        Task<ScopeIndex> ScopeIndexAsync(CancellationToken ct = default);
    }

    /// <summary>Watch List approvals (server.py /api/approvals).</summary>
    public interface IApprovalService
    {
        Task<ApprovalsResponse> GetAsync(int days, AppUserContext me, CancellationToken ct = default);
        /// <summary>body: {decision: approved|rejected, qty, comment}.</summary>
        Task<SeqResponse> DecideAsync(string reqId, JsonObject body, AppUserContext me, CancellationToken ct = default);
        /// <summary>Pending approvals waiting for this user (last 30 request days).</summary>
        Task<int> PendingCountAsync(AppUserContext me, CancellationToken ct = default);
    }

    /// <summary>Tool 1 request log for the API (server.py /api/requests) — extends <see cref="IRequestLedger"/>.</summary>
    public interface IRequestService : IRequestLedger
    {
        /// <summary>A day's live requests, filtered to what the user may see.</summary>
        Task<RequestsDayResponse> GetDayForUserAsync(string? date, AppUserContext me, CancellationToken ct = default);
        Task<SeqResponse> ClearDayAsync(string? date, AppUserContext me, CancellationToken ct = default);
        /// <summary>The day's sequence number (status reqSeq).</summary>
        Task<int> GetSeqAsync(string date, CancellationToken ct = default);
    }

    /// <summary>Data files, upload batches and restores (server.py /api/data, /api/uploads) — extends <see cref="IDatasetStore"/>.</summary>
    public interface IDataService : IDatasetStore
    {
        /// <summary>The stored gzip JSON payload of the current (or given) version.</summary>
        Task<byte[]> GetPayloadGzipAsync(string key, string? version, CancellationToken ct = default);
        /// <summary>server.py api_PUT_data with an already gzip-compressed {"rows":[…]} body.</summary>
        Task<DataSaveResponse> PutRawAsync(string key, byte[] payloadGz, int rows, string label, string? uploadId, AppUserContext me, CancellationToken ct = default);
        Task<DataVersionResponse> ClearAsync(string key, string? uploadId, AppUserContext me, CancellationToken ct = default);
        Task<DataSaveResponse> RestoreVersionAsync(string key, string version, AppUserContext me, CancellationToken ct = default);
        Task<UploadRestoreResponse> RestoreUploadAsync(string uploadId, AppUserContext me, CancellationToken ct = default);
        Task<Manifest> GetManifestAsync(CancellationToken ct = default);
    }

    public interface IHistoryService
    {
        Task<UploadHistoryResponse> UploadsAsync(string? from, string? to, CancellationToken ct = default);
        Task<ItemsResponse> RequestsAsync(string? from, string? to, AppUserContext me, CancellationToken ct = default);
    }

    /// <summary>Saved Tool 2 runs (server.py /api/runs).</summary>
    public interface IRunService
    {
        Task<ItemsResponse> ListAsync(string? from, string? to, CancellationToken ct = default);
        Task<byte[]> GetGzipAsync(string id, CancellationToken ct = default);
        /// <summary>payloadGz = gzip JSON body; meta = the run meta object (X-Run-Meta).</summary>
        Task<RunSaveResponse> SaveAsync(byte[] payloadGz, JsonObject meta, AppUserContext me, CancellationToken ct = default);
        Task DeleteAsync(string id, AppUserContext me, CancellationToken ct = default);
    }

    public interface IParamsService
    {
        Task<JsonNode> GetAsync(CancellationToken ct = default);
        Task<ParamsSaveResponse> PutAsync(JsonNode? body, AppUserContext me, CancellationToken ct = default);
    }

    public interface IFeedbackService
    {
        Task<ItemsResponse> ListAsync(bool mine, AppUserContext me, CancellationToken ct = default);
        Task<FeedbackCreatedResponse> CreateAsync(JsonObject body, AppUserContext me, CancellationToken ct = default);
        Task SetStatusAsync(string id, JsonObject body, AppUserContext me, CancellationToken ct = default);
    }

    /// <summary>Nightly / on-demand backup of the whole store as vmm-data-*.zip in the v1.1.4 file layout.</summary>
    public interface IBackupService
    {
        BackupListResponse List();
        /// <summary>Writes a backup (one at a time), prunes old ones and audits it as <paramref name="who"/>; returns the zip name.</summary>
        Task<string> BackupNowAsync(string who, CancellationToken ct = default);
        /// <summary>True when a backup of the given business day (yyyyMMdd) exists.</summary>
        bool HasBackupFor(string yyyymmdd);
    }

    public interface IStatusService
    {
        Task<StatusResponse> GetStatusAsync(AppUserContext? me, CancellationToken ct = default);
        Task<UserPublicDto?> MePublicAsync(AppUserContext? me, CancellationToken ct = default);
        HealthResponse Health();
    }

    /// <summary>HTTP calls to the AI providers (server.py anthropic_call / insights_call).</summary>
    public interface IAiClient
    {
        Task<AiResult> AnthropicCallAsync(string system, JsonNode messages, int maxTokens, CancellationToken ct = default);
        /// <summary>Groq when a Groq key is set, else Anthropic; always the OpenAI shape plus "_provider".</summary>
        Task<AiResult> InsightsCallAsync(string prompt, CancellationToken ct = default);
    }

    public interface IAiService
    {
        Task<AiResult> ChatAsync(JsonObject body, AppUserContext me, CancellationToken ct = default);
        Task<AiResult> InsightsAsync(JsonObject body, AppUserContext me, CancellationToken ct = default);
    }

    /// <summary>Start-up maintenance (server.py migrate_ids, prune_history).</summary>
    public interface IMaintenanceService
    {
        Task MigrateIdsAsync(CancellationToken ct = default);
        Task PruneHistoryAsync(CancellationToken ct = default);
    }
}
