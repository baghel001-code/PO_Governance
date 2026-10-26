using System.Data;
using System.Globalization;
using System.Text.Json;
using Application.Interfaces.V1.Sap;
using Application.Services.V1.Sap;
using Dapper;
using Domain.Entities.Sap;
using Domain.Exceptions;
using Infrastructure.Utilitys;

namespace Infrastructure.Repositories
{
    /// <summary>
    /// <see cref="ISapRepository"/> on SQL Server, QPS style: <see cref="DapperHelper"/> + the PRO_SAP_* procedures of
    /// database/sqlserver/05_sap_interface.sql (DynamicParameters, @Cmd, @Error_Code / @Error_Message OUTPUT).
    /// </summary>
    public sealed class SapSqlServerRepository : ISapRepository
    {
        private readonly DapperHelper _db;

        public SapSqlServerRepository(DapperHelper db)
        {
            _db = db;
        }

        public bool Available => true;
        public string? UnavailableReason => null;

        private static readonly JsonSerializerOptions RowJson = new() { PropertyNamingPolicy = null };

        private static DynamicParameters P(string? cmd = null)
        {
            var p = DapperHelper.AddErrorOutputs(new DynamicParameters());
            if (cmd != null) p.Add("@Cmd", cmd, DbType.String);
            return p;
        }

        private static void Check(string sp, DynamicParameters p)
        {
            var code = DapperHelper.ErrorCode(p);
            if (code != 0) throw new SapStoreException(sp, code, DapperHelper.ErrorMessage(p));
        }

        private async Task<List<T>> ListAsync<T>(string sp, DynamicParameters p, CancellationToken ct)
        {
            var rows = (await _db.ExecuteStoredProcedureListAsync<T>(sp, p, commandTimeout: null, ct: ct)).AsList();
            Check(sp, p);
            return rows;
        }

        private async Task<T> One<T>(string sp, DynamicParameters p, CancellationToken ct) =>
            (await ListAsync<T>(sp, p, ct)).FirstOrDefault() ?? throw new SapStoreException(sp, 50404, "No row returned");

        // ── technical users ─────────────────────────────────────────

        private const string TechUserSp = "PRO_SAP_TECH_USER";

        public async Task<SapTechUser?> GetTechUserAsync(string userName, CancellationToken ct = default)
        {
            var p = P("GET");
            p.Add("@User_Name", userName, DbType.String, size: 64);
            return (await ListAsync<SapTechUser>(TechUserSp, p, ct)).FirstOrDefault();
        }

        public Task<List<SapTechUser>> ListTechUsersAsync(CancellationToken ct = default) => ListAsync<SapTechUser>(TechUserSp, P("LIST"), ct);

        public Task<SapTechUser> AddTechUserAsync(string userName, string sourceSystem, string? description, string passwordHash, string by, CancellationToken ct = default)
        {
            var p = P("ADD");
            p.Add("@User_Name", userName, DbType.String, size: 64);
            p.Add("@Source_System", sourceSystem, DbType.String, size: 10);
            p.Add("@Description", description, DbType.String, size: 200);
            p.Add("@Password_Hash", passwordHash, DbType.String, size: 200);
            p.Add("@By", by, DbType.String, size: 200);
            return One<SapTechUser>(TechUserSp, p, ct);
        }

        public Task<SapTechUser> SetTechUserPasswordAsync(string userName, string passwordHash, string by, CancellationToken ct = default)
        {
            var p = P("SET_PASSWORD");
            p.Add("@User_Name", userName, DbType.String, size: 64);
            p.Add("@Password_Hash", passwordHash, DbType.String, size: 200);
            p.Add("@By", by, DbType.String, size: 200);
            return One<SapTechUser>(TechUserSp, p, ct);
        }

        public Task<SapTechUser> SetTechUserActiveAsync(string userName, bool active, string by, CancellationToken ct = default)
        {
            var p = P("SET_ACTIVE");
            p.Add("@User_Name", userName, DbType.String, size: 64);
            p.Add("@Active", active, DbType.Boolean);
            p.Add("@By", by, DbType.String, size: 200);
            return One<SapTechUser>(TechUserSp, p, ct);
        }

        public Task<SapTechUser> UnlockTechUserAsync(string userName, CancellationToken ct = default)
        {
            var p = P("UNLOCK");
            p.Add("@User_Name", userName, DbType.String, size: 64);
            return One<SapTechUser>(TechUserSp, p, ct);
        }

        public async Task LoginOkAsync(string userName, string ip, CancellationToken ct = default)
        {
            var p = P("LOGIN_OK");
            p.Add("@User_Name", userName, DbType.String, size: 64);
            p.Add("@Ip", ip.Length > 64 ? ip[..64] : ip, DbType.String, size: 64);
            await _db.ExecuteStoredProcedureAsync(TechUserSp, p, commandTimeout: null, ct: ct);
            Check(TechUserSp, p);
        }

        public async Task<SapTechUser?> LoginFailAsync(string userName, int maxFailed, int lockoutMinutes, CancellationToken ct = default)
        {
            var p = P("LOGIN_FAIL");
            p.Add("@User_Name", userName, DbType.String, size: 64);
            p.Add("@Max_Failed", maxFailed, DbType.Int32);
            p.Add("@Lockout_Minutes", lockoutMinutes, DbType.Int32);
            return (await ListAsync<SapTechUser>(TechUserSp, p, ct)).FirstOrDefault();
        }

        // ── decisions ───────────────────────────────────────────────

        private const string DecisionSaveSp = "PRO_SAP_DECISION_SAVE";
        private const string DecisionGetSp = "PRO_SAP_DECISION_GET";

        public async Task<SapDecisionLogRow?> GetDecisionByRequestAsync(string requestId, CancellationToken ct = default)
        {
            var p = P("BY_REQUEST");
            p.Add("@Request_Id", requestId, DbType.String, size: 80);
            return (await ListAsync<SapDecisionLogRow>(DecisionGetSp, p, ct)).FirstOrDefault();
        }

        public async Task<long> NextDecisionSeqAsync(CancellationToken ct = default)
        {
            var p = P("NEXT_ID");
            p.Add("@Seq_No", dbType: DbType.Int64, direction: ParameterDirection.Output);
            await _db.ExecuteStoredProcedureAsync(DecisionSaveSp, p, commandTimeout: null, ct: ct);
            Check(DecisionSaveSp, p);
            return p.Get<long>("@Seq_No");
        }

        public async Task<long> SaveDecisionAsync(SapDecisionLogRow r, IReadOnlyList<SapDecisionItemRow> items, CancellationToken ct = default)
        {
            var p = P("INSERT");
            p.Add("@Decision_Id", r.DecisionId, DbType.String, size: 30);
            p.Add("@Request_Id", r.RequestId, DbType.String, size: 80);
            p.Add("@Body_Hash", r.BodyHash, DbType.AnsiStringFixedLength, size: 64);
            p.Add("@Mode", r.Mode, DbType.String, size: 10);
            p.Add("@Source_System", r.SourceSystem, DbType.String, size: 10);
            p.Add("@Client", r.Client, DbType.String, size: 10);
            p.Add("@Po_Number", r.PoNumber, DbType.String, size: 20);
            p.Add("@Buyer_User", r.BuyerUser, DbType.String, size: 64);
            p.Add("@Division", r.Division, DbType.String, size: 40);
            p.Add("@Overall_Verdict", r.OverallVerdict, DbType.String, size: 10);
            p.Add("@Verdict_Code", r.VerdictCode, DbType.StringFixedLength, size: 1);
            p.Add("@Route_To", r.RouteTo, DbType.String, size: 10);
            p.Add("@Bm_Head_Required", r.BmHeadRequired, DbType.Boolean);
            p.Add("@Http_Status", r.HttpStatus, DbType.Int32);
            p.Add("@Error_Code_Out", r.ErrorCode, DbType.String, size: 50);
            p.Add("@Error_Text", r.ErrorMessage, DbType.String, size: 500);
            p.Add("@Item_Count", r.ItemCount, DbType.Int32);
            p.Add("@Data_Delayed", r.DataDelayed, DbType.Boolean);
            p.Add("@Events_Lag_Seconds", r.EventsLagSeconds, DbType.Int64);
            p.Add("@Rules_Version", r.RulesVersion, DbType.String, size: 40);
            p.Add("@Duration_Ms", r.DurationMs, DbType.Int32);
            p.Add("@Tech_User", r.TechUser, DbType.String, size: 200);
            p.Add("@Client_Ip", r.ClientIp, DbType.String, size: 64);
            p.Add("@Is_Test", r.IsTest, DbType.Boolean);
            p.Add("@Trace_Id", r.TraceId, DbType.String, size: 20);
            p.Add("@Requested_At", r.RequestedAt, DbType.DateTimeOffset);
            p.Add("@Decided_At", r.DecidedAt, DbType.DateTimeOffset);
            p.Add("@Request_Json", r.RequestJson, DbType.String, size: -1);
            p.Add("@Response_Json", r.ResponseJson, DbType.String, size: -1);
            p.Add("@View_Token", r.ViewToken, DbType.String, size: 64);
            p.Add("@View_Expires_At", r.ViewExpiresAt, DbType.DateTimeOffset);
            p.Add("@Items", JsonSerializer.Serialize(items, RowJson), DbType.String, size: -1);
            p.Add("@Log_Id", dbType: DbType.Int64, direction: ParameterDirection.Output);
            var code = 0;
            for (var attempt = 1; attempt <= 3; attempt++)
            {
                await _db.ExecuteStoredProcedureAsync(DecisionSaveSp, p, commandTimeout: null, ct: ct);
                code = DapperHelper.ErrorCode(p);
                if (code != 1205) break;   // deadlock victim: rolled back, try again
                await Task.Delay(20 * attempt, ct);
            }
            if (code == 50409) throw new SapRequestIdTakenException(r.RequestId);
            Check(DecisionSaveSp, p);
            return p.Get<long>("@Log_Id");
        }

        private static DateTime? Date(string? ymd) =>
            DateTime.TryParseExact((ymd ?? "").Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : null;

        public async Task<SapDecisionListResponse> ListDecisionsAsync(SapDecisionListReq req, int tzOffsetMinutes, CancellationToken ct = default)
        {
            var p = P("LIST");
            p.Add("@From", Date(req.From), DbType.Date);
            p.Add("@To", Date(req.To), DbType.Date);
            p.Add("@Po_Number", string.IsNullOrWhiteSpace(req.PoNumber) ? null : req.PoNumber.Trim(), DbType.String, size: 20);
            p.Add("@Verdict", string.IsNullOrWhiteSpace(req.Verdict) ? null : req.Verdict.Trim().ToUpperInvariant(), DbType.String, size: 10);
            p.Add("@Mode", string.IsNullOrWhiteSpace(req.Mode) ? null : req.Mode.Trim().ToUpperInvariant(), DbType.String, size: 10);
            p.Add("@Status", string.IsNullOrWhiteSpace(req.Status) ? null : req.Status.Trim().ToLowerInvariant(), DbType.String, size: 10);
            p.Add("@Max", req.Max, DbType.Int32);
            p.Add("@Tz_Offset_Min", tzOffsetMinutes, DbType.Int32);
            p.Add("@Total", dbType: DbType.Int32, direction: ParameterDirection.Output);
            var rows = await ListAsync<SapDecisionLogRow>(DecisionGetSp, p, ct);
            return new SapDecisionListResponse { Items = rows, Total = p.Get<int?>("@Total") ?? rows.Count };
        }

        public async Task<SapDecisionDetailResponse> GetDecisionAsync(long id, CancellationToken ct = default)
        {
            var p = P("BY_ID");
            p.Add("@Id", id, DbType.Int64);
            return await DetailAsync(p, ct);
        }

        public async Task<SapDecisionDetailResponse?> GetDecisionByViewTokenAsync(string token, CancellationToken ct = default)
        {
            var p = P("BY_VIEW_TOKEN");
            p.Add("@View_Token", token, DbType.String, size: 64);
            var d = await DetailAsync(p, ct);
            return d.Decision == null ? null : d;
        }

        private async Task<SapDecisionDetailResponse> DetailAsync(DynamicParameters p, CancellationToken ct)
        {
            var (rows, items, docs) = await _db.ExecuteStoredProcedureMultiple3ListAsync<SapDecisionLogRow, SapDecisionItemRow, SapDocumentRow>(DecisionGetSp, p, ct);
            Check(DecisionGetSp, p);
            return new SapDecisionDetailResponse { Decision = rows.FirstOrDefault(), Items = items.AsList(), Documents = docs.AsList() };
        }

        // ── final documents ─────────────────────────────────────────

        private const string DocumentSaveSp = "PRO_SAP_DOCUMENT_SAVE";

        public async Task<SapDocumentSaveResult> SaveDocumentAsync(SapDocumentInsert d, CancellationToken ct = default)
        {
            var p = DapperHelper.AddErrorOutputs(new DynamicParameters());
            p.Add("@Request_Id", d.RequestId, DbType.String, size: 80);
            p.Add("@Document_No", d.DocumentNo, DbType.String, size: 40);
            p.Add("@Document_Type", d.DocumentType, DbType.String, size: 20);
            p.Add("@Po_Number", d.PoNumber, DbType.String, size: 20);
            p.Add("@Document_Date", Date(d.DocumentDate), DbType.Date);
            p.Add("@Note", d.Note, DbType.String, size: 500);
            p.Add("@Replace", d.Replace, DbType.Boolean);
            p.Add("@Tech_User", d.TechUser, DbType.String, size: 200);
            p.Add("@Source_System", d.SourceSystem, DbType.String, size: 10);
            p.Add("@Client_Ip", d.ClientIp, DbType.String, size: 64);
            p.Add("@Trace_Id", d.TraceId, DbType.String, size: 20);
            p.Add("@Request_Json", d.RequestJson, DbType.String, size: -1);
            p.Add("@Result", dbType: DbType.String, direction: ParameterDirection.Output, size: 20);
            p.Add("@Decision_Id", dbType: DbType.String, direction: ParameterDirection.Output, size: 30);
            p.Add("@Current_Document_No", dbType: DbType.String, direction: ParameterDirection.Output, size: 40);
            p.Add("@Received_At", dbType: DbType.DateTimeOffset, direction: ParameterDirection.Output);
            await _db.ExecuteStoredProcedureAsync(DocumentSaveSp, p, commandTimeout: null, ct: ct);
            var code = DapperHelper.ErrorCode(p);
            var res = new SapDocumentSaveResult
            {
                DecisionId = p.Get<string?>("@Decision_Id"), CurrentDocumentNo = p.Get<string?>("@Current_Document_No"),
                NotFound = code == 50404, Conflict = code == 50409, Message = code == 0 ? null : DapperHelper.ErrorMessage(p)
            };
            if (code is 50404 or 50409) return res;
            Check(DocumentSaveSp, p);
            res.Result = p.Get<string?>("@Result") ?? "";
            res.ReceivedAt = p.Get<DateTimeOffset?>("@Received_At") ?? DateTimeOffset.Now;
            return res;
        }

        public async Task<SapDocumentReportResponse> DocumentReportAsync(SapDocumentReportReq req, int tzOffsetMinutes, CancellationToken ct = default)
        {
            var p = P("REPORT");
            p.Add("@From", Date(req.From), DbType.Date);
            p.Add("@To", Date(req.To), DbType.Date);
            p.Add("@Po_Number", string.IsNullOrWhiteSpace(req.PoNumber) ? null : req.PoNumber.Trim(), DbType.String, size: 40);
            p.Add("@Verdict", string.IsNullOrWhiteSpace(req.Verdict) ? null : req.Verdict.Trim().ToUpperInvariant(), DbType.String, size: 10);
            p.Add("@Mode", string.IsNullOrWhiteSpace(req.Mode) ? null : req.Mode.Trim().ToUpperInvariant(), DbType.String, size: 10);
            p.Add("@Has_Document", string.IsNullOrWhiteSpace(req.HasDocument) ? null : req.HasDocument.Trim().ToLowerInvariant(), DbType.String, size: 10);
            p.Add("@Include_Tests", req.IncludeTests, DbType.Boolean);
            p.Add("@Max", req.Max, DbType.Int32);
            p.Add("@Tz_Offset_Min", tzOffsetMinutes, DbType.Int32);
            p.Add("@Total", dbType: DbType.Int32, direction: ParameterDirection.Output);
            var rows = await ListAsync<SapDocumentReportRow>(DecisionGetSp, p, ct);
            return new SapDocumentReportResponse { Items = rows, Total = p.Get<int?>("@Total") ?? rows.Count };
        }

        public async Task<SapDecisionStats> DecisionStatsAsync(string today, int tzOffsetMinutes, CancellationToken ct = default)
        {
            var p = P("STATS");
            p.Add("@Today", Date(today), DbType.Date);
            p.Add("@Tz_Offset_Min", tzOffsetMinutes, DbType.Int32);
            return (await ListAsync<SapDecisionStats>(DecisionGetSp, p, ct)).FirstOrDefault() ?? new SapDecisionStats();
        }

        // ── events ──────────────────────────────────────────────────

        private const string EventsSaveSp = "PRO_SAP_EVENTS_SAVE";
        private const string EventsGetSp = "PRO_SAP_EVENTS_GET";

        public async Task<List<SapEventSaveResult>> SaveEventsAsync(IReadOnlyList<SapEventInsert> events, string? queue, string? sourceSystem, string? client,
                                                                    string? techUser, string batchId, CancellationToken ct = default)
        {
            var p = P();
            p.Add("@Events", JsonSerializer.Serialize(events, RowJson), DbType.String, size: -1);
            p.Add("@Queue", queue, DbType.String, size: 64);
            p.Add("@Source_System", sourceSystem, DbType.String, size: 10);
            p.Add("@Client", client, DbType.String, size: 10);
            p.Add("@Tech_User", techUser is { Length: > 64 } t ? t[..64] : techUser, DbType.String, size: 64);
            p.Add("@Batch_Id", batchId, DbType.String, size: 40);
            var rows = await ListAsync<SapEventSaveResult>(EventsSaveSp, p, ct);
            if (rows.Count != events.Count) throw new SapStoreException(EventsSaveSp, 50500, $"{rows.Count} results for {events.Count} events");
            return rows;
        }

        public async Task<SapEventStatus> EventStatusAsync(string today, int tzOffsetMinutes, CancellationToken ct = default)
        {
            var p = P("STATUS");
            p.Add("@Today", Date(today), DbType.Date);
            p.Add("@Tz_Offset_Min", tzOffsetMinutes, DbType.Int32);
            return (await ListAsync<SapEventStatus>(EventsGetSp, p, ct)).FirstOrDefault() ?? new SapEventStatus();
        }

        public async Task<SapEventListResponse> ListEventsAsync(SapEventListReq req, int tzOffsetMinutes, CancellationToken ct = default)
        {
            var p = P("LIST");
            p.Add("@From", Date(req.From), DbType.Date);
            p.Add("@To", Date(req.To), DbType.Date);
            p.Add("@Event_Type", string.IsNullOrWhiteSpace(req.EventType) ? null : req.EventType.Trim().ToUpperInvariant(), DbType.String, size: 30);
            p.Add("@Status", string.IsNullOrWhiteSpace(req.Status) ? null : req.Status.Trim().ToUpperInvariant(), DbType.String, size: 20);
            p.Add("@Q", string.IsNullOrWhiteSpace(req.Q) ? null : req.Q.Trim(), DbType.String, size: 100);
            p.Add("@Max", req.Max, DbType.Int32);
            p.Add("@Tz_Offset_Min", tzOffsetMinutes, DbType.Int32);
            p.Add("@Total", dbType: DbType.Int32, direction: ParameterDirection.Output);
            var rows = await ListAsync<SapEventRow>(EventsGetSp, p, ct);
            return new SapEventListResponse { Items = rows, Total = p.Get<int?>("@Total") ?? rows.Count };
        }

        public async Task<SapEventRow?> GetEventAsync(long id, CancellationToken ct = default)
        {
            var p = P("BY_ID");
            p.Add("@Id", id, DbType.Int64);
            return (await ListAsync<SapEventRow>(EventsGetSp, p, ct)).FirstOrDefault();
        }

        // ── reason codes ────────────────────────────────────────────

        private const string ReasonSp = "PRO_SAP_REASON_CODES";

        public async Task<(List<SapReasonCodeRow> Codes, string Version)> ActiveExceptionCodesAsync(int tzOffsetMinutes, CancellationToken ct = default)
        {
            var p = P("ACTIVE");
            p.Add("@Tz_Offset_Min", tzOffsetMinutes, DbType.Int32);
            p.Add("@Version", dbType: DbType.String, size: 20, direction: ParameterDirection.Output);
            var rows = await ListAsync<SapReasonCodeRow>(ReasonSp, p, ct);
            return (rows, p.Get<string?>("@Version") ?? "");
        }

        public Task<List<SapReasonCodeRow>> ListReasonCodesAsync(string? codeType, CancellationToken ct = default)
        {
            var p = P("LIST");
            p.Add("@Code_Type", codeType, DbType.String, size: 10);
            return ListAsync<SapReasonCodeRow>(ReasonSp, p, ct);
        }

        public Task<SapReasonCodeRow> SaveReasonCodeAsync(SapReasonCodeSaveReq req, string by, CancellationToken ct = default)
        {
            var p = P("SAVE");
            p.Add("@Code", req.Code, DbType.String, size: 40);
            p.Add("@Text", req.Text, DbType.String, size: 200);
            p.Add("@Active", req.Active, DbType.Boolean);
            p.Add("@Sort_Order", req.SortOrder > 0 ? req.SortOrder : null, DbType.Int32);
            p.Add("@By", by, DbType.String, size: 200);
            return One<SapReasonCodeRow>(ReasonSp, p, ct);
        }
    }

    /// <summary>
    /// <see cref="ISapRepository"/> when the API runs on PostgreSQL (PoGovSettings:Storage = postgres): the SAP interface
    /// tables and procedures exist only on SQL Server, so every SAP endpoint answers 503 "SAP interface requires SQL Server storage".
    /// </summary>
    public sealed class SapUnavailableRepository : ISapRepository
    {
        public const string Reason = "SAP interface requires SQL Server storage";

        public bool Available => false;
        public string? UnavailableReason => Reason;

        private static Exception Fail() => new SapApiException(503, SapErrorCodes.StorageNotSupported, Reason);

        public Task<SapTechUser?> GetTechUserAsync(string userName, CancellationToken ct = default) => throw Fail();
        public Task<List<SapTechUser>> ListTechUsersAsync(CancellationToken ct = default) => throw Fail();
        public Task<SapDecisionDetailResponse?> GetDecisionByViewTokenAsync(string token, CancellationToken ct = default) => throw Fail();
        public Task<SapDocumentSaveResult> SaveDocumentAsync(SapDocumentInsert doc, CancellationToken ct = default) => throw Fail();
        public Task<SapDocumentReportResponse> DocumentReportAsync(SapDocumentReportReq req, int tzOffsetMinutes, CancellationToken ct = default) => throw Fail();
        public Task<SapTechUser> AddTechUserAsync(string userName, string sourceSystem, string? description, string passwordHash, string by, CancellationToken ct = default) => throw Fail();
        public Task<SapTechUser> SetTechUserPasswordAsync(string userName, string passwordHash, string by, CancellationToken ct = default) => throw Fail();
        public Task<SapTechUser> SetTechUserActiveAsync(string userName, bool active, string by, CancellationToken ct = default) => throw Fail();
        public Task<SapTechUser> UnlockTechUserAsync(string userName, CancellationToken ct = default) => throw Fail();
        public Task LoginOkAsync(string userName, string ip, CancellationToken ct = default) => throw Fail();
        public Task<SapTechUser?> LoginFailAsync(string userName, int maxFailed, int lockoutMinutes, CancellationToken ct = default) => throw Fail();
        public Task<SapDecisionLogRow?> GetDecisionByRequestAsync(string requestId, CancellationToken ct = default) => throw Fail();
        public Task<long> NextDecisionSeqAsync(CancellationToken ct = default) => throw Fail();
        public Task<long> SaveDecisionAsync(SapDecisionLogRow row, IReadOnlyList<SapDecisionItemRow> items, CancellationToken ct = default) => throw Fail();
        public Task<SapDecisionListResponse> ListDecisionsAsync(SapDecisionListReq req, int tzOffsetMinutes, CancellationToken ct = default) => throw Fail();
        public Task<SapDecisionDetailResponse> GetDecisionAsync(long id, CancellationToken ct = default) => throw Fail();
        public Task<SapDecisionStats> DecisionStatsAsync(string today, int tzOffsetMinutes, CancellationToken ct = default) => throw Fail();
        public Task<List<SapEventSaveResult>> SaveEventsAsync(IReadOnlyList<SapEventInsert> events, string? queue, string? sourceSystem, string? client, string? techUser, string batchId, CancellationToken ct = default) => throw Fail();
        public Task<SapEventStatus> EventStatusAsync(string today, int tzOffsetMinutes, CancellationToken ct = default) => throw Fail();
        public Task<SapEventListResponse> ListEventsAsync(SapEventListReq req, int tzOffsetMinutes, CancellationToken ct = default) => throw Fail();
        public Task<SapEventRow?> GetEventAsync(long id, CancellationToken ct = default) => throw Fail();
        public Task<(List<SapReasonCodeRow> Codes, string Version)> ActiveExceptionCodesAsync(int tzOffsetMinutes, CancellationToken ct = default) => throw Fail();
        public Task<List<SapReasonCodeRow>> ListReasonCodesAsync(string? codeType, CancellationToken ct = default) => throw Fail();
        public Task<SapReasonCodeRow> SaveReasonCodeAsync(SapReasonCodeSaveReq req, string by, CancellationToken ct = default) => throw Fail();
    }
}
