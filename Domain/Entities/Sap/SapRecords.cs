using Domain.Entities.Base;

namespace Domain.Entities.Sap
{
    // ─────────────────────────────────────────────────────────────────────────────
    // SAP interface records (database/sqlserver/05_sap_interface.sql) and the portal-facing request / response
    // classes of the SAP monitoring screen (/admin/sap, QPS conventions: PascalCase, ApiDataResponse<T>.Data).
    // ─────────────────────────────────────────────────────────────────────────────

    /// <summary>SAP_TECH_USERS row (one technical account per SAP system). The hash never leaves the API.</summary>
    public sealed class SapTechUser
    {
        public int Id { get; set; }
        public string UserName { get; set; } = "";
        public string SourceSystem { get; set; } = "";
        public string? Description { get; set; }
        [System.Text.Json.Serialization.JsonIgnore] public string PasswordHash { get; set; } = "";
        public bool Active { get; set; }
        public int FailedCount { get; set; }
        public DateTimeOffset? LockedUntil { get; set; }
        public DateTimeOffset? LastCallAt { get; set; }
        public string? LastCallIp { get; set; }
        public DateTimeOffset? PasswordChangedAt { get; set; }
        public DateTimeOffset CreatedAt { get; set; }
        public string? CreatedBy { get; set; }
    }

    /// <summary>SAP_DECISION_LOG row (one per /decisions call, incl. 4xx / 503 answers, and portal test decisions).</summary>
    public sealed class SapDecisionLogRow
    {
        public long Id { get; set; }
        public string? DecisionId { get; set; }
        public string RequestId { get; set; } = "";
        public string? BodyHash { get; set; }
        public string? Mode { get; set; }
        public string? SourceSystem { get; set; }
        public string? Client { get; set; }
        public string? PoNumber { get; set; }
        public string? BuyerUser { get; set; }
        public string? Division { get; set; }
        public string? OverallVerdict { get; set; }
        public string? VerdictCode { get; set; }
        public string? RouteTo { get; set; }
        public bool BmHeadRequired { get; set; }
        public int HttpStatus { get; set; }
        public string? ErrorCode { get; set; }
        public string? ErrorMessage { get; set; }
        public int ItemCount { get; set; }
        public bool DataDelayed { get; set; }
        public long? EventsLagSeconds { get; set; }
        public string? RulesVersion { get; set; }
        public int DurationMs { get; set; }
        public string? TechUser { get; set; }
        public string? ClientIp { get; set; }
        public bool IsTest { get; set; }
        public string? TraceId { get; set; }
        public DateTimeOffset? RequestedAt { get; set; }
        public DateTimeOffset DecidedAt { get; set; }
        /// <summary>Filled by the detail call only.</summary>
        public string? RequestJson { get; set; }
        public string? ResponseJson { get; set; }
        /// <summary>Verdict link token (portal /verdict/&lt;token&gt;) and its expiry; null when no link was issued.</summary>
        public string? ViewToken { get; set; }
        public DateTimeOffset? ViewExpiresAt { get; set; }
    }

    /// <summary>SAP_DECISION_DOCUMENTS row: the final SAP document (MRN) of a request_id (current, or replaced = history).</summary>
    public sealed class SapDocumentRow
    {
        public long Id { get; set; }
        public long DecisionLogId { get; set; }
        public string RequestId { get; set; } = "";
        public string? DecisionId { get; set; }
        public string DocumentNo { get; set; } = "";
        public string DocumentType { get; set; } = "MRN";
        public string? PoNumber { get; set; }
        /// <summary>YYYY-MM-DD.</summary>
        public string? DocumentDate { get; set; }
        public string? Note { get; set; }
        public bool IsCurrent { get; set; }
        public DateTimeOffset? ReplacedAt { get; set; }
        public string? TechUser { get; set; }
        public string? SourceSystem { get; set; }
        public DateTimeOffset ReceivedAt { get; set; }
    }

    /// <summary>What to store for POST /documents (PRO_SAP_DOCUMENT_SAVE).</summary>
    public sealed class SapDocumentInsert
    {
        public string RequestId { get; set; } = "";
        public string DocumentNo { get; set; } = "";
        public string? DocumentType { get; set; }
        public string? PoNumber { get; set; }
        public string? DocumentDate { get; set; }
        public string? Note { get; set; }
        public bool Replace { get; set; }
        public string? TechUser { get; set; }
        public string? SourceSystem { get; set; }
        public string? ClientIp { get; set; }
        public string? TraceId { get; set; }
        public string? RequestJson { get; set; }
    }

    /// <summary>PRO_SAP_DOCUMENT_SAVE outcome: Result STORED / UNCHANGED / REPLACED, or Conflict / NotFound.</summary>
    public sealed class SapDocumentSaveResult
    {
        public string Result { get; set; } = "";
        public string? DecisionId { get; set; }
        public string? CurrentDocumentNo { get; set; }
        public DateTimeOffset ReceivedAt { get; set; }
        public bool NotFound { get; set; }
        public bool Conflict { get; set; }
        public string? Message { get; set; }
    }

    /// <summary>One line of the PO document report: an answered decision and its current document (MRN), if any.</summary>
    public sealed class SapDocumentReportRow
    {
        public long Id { get; set; }
        public string? DecisionId { get; set; }
        public string RequestId { get; set; } = "";
        public string? PoNumber { get; set; }
        public string? BuyerUser { get; set; }
        public string? Division { get; set; }
        public string? Mode { get; set; }
        public string? OverallVerdict { get; set; }
        public string? RouteTo { get; set; }
        public int ItemCount { get; set; }
        public double? OrderQty { get; set; }
        public string? SourceSystem { get; set; }
        public bool IsTest { get; set; }
        public DateTimeOffset DecidedAt { get; set; }
        public string? DocumentNo { get; set; }
        public string? DocumentType { get; set; }
        public string? DocumentDate { get; set; }
        public DateTimeOffset? DocumentReceivedAt { get; set; }
    }

    /// <summary>SAP_DECISION_ITEMS row.</summary>
    public sealed class SapDecisionItemRow
    {
        public long DecisionLogId { get; set; }
        public int PoItem { get; set; }
        public string? Article { get; set; }
        public string? GenericArticle { get; set; }
        public string? Gm { get; set; }
        public string? GmMatch { get; set; }
        public string? ColourSent { get; set; }
        public string? Colour { get; set; }
        public string? ColourMatch { get; set; }
        public string? DeliveryMonth { get; set; }
        public double OrderQty { get; set; }
        public double GroupQty { get; set; }
        public string? Verdict { get; set; }
        public string? VerdictCode { get; set; }
        public double? MaxQty { get; set; }
        public string? ReasonCode { get; set; }
        public string? RuleId { get; set; }
        public string? ReasonText { get; set; }
        public string? Engine { get; set; }
        public string? EngineDecision { get; set; }
        public string? EngineReason { get; set; }
        public string? MetricsJson { get; set; }
    }

    /// <summary>SAP_EVENTS row (every received event: accepted or parked; duplicates only bump Dup_Count).</summary>
    public sealed class SapEventRow
    {
        public long Id { get; set; }
        public string MsgId { get; set; } = "";
        public long? Seq { get; set; }
        public string? Queue { get; set; }
        public string? SourceSystem { get; set; }
        public string? Client { get; set; }
        public string? EventType { get; set; }
        public DateTimeOffset? OccurredAt { get; set; }
        public DateTimeOffset ReceivedAt { get; set; }
        public string? SapUser { get; set; }
        public string? PoNumber { get; set; }
        public int? PoItem { get; set; }
        public string? MaterialDoc { get; set; }
        public string? DecisionId { get; set; }
        /// <summary>ACCEPTED or PARKED.</summary>
        public string Status { get; set; } = "";
        public string? ErrorCode { get; set; }
        public string? ErrorMessage { get; set; }
        /// <summary>IN_ORDER, or LATE when its seq is not above the last seq stored for the queue.</summary>
        public string? SeqStatus { get; set; }
        /// <summary>Applied to the live open-PO / stock position — always false until the position tables exist (handover §8 items 6, 7, 9).</summary>
        public bool Applied { get; set; }
        public DateTimeOffset? AppliedAt { get; set; }
        public int DupCount { get; set; }
        public DateTimeOffset? LastDupAt { get; set; }
        public long LagSeconds { get; set; }
        public string? TechUser { get; set; }
        public string? BatchId { get; set; }
        /// <summary>Filled by the detail call only.</summary>
        public string? PayloadJson { get; set; }
    }

    /// <summary>One event as handed to the store (normalised by the events service).</summary>
    public sealed class SapEventInsert
    {
        public string MsgId { get; set; } = "";
        public long? Seq { get; set; }
        public string? EventType { get; set; }
        public string? OccurredAt { get; set; }
        public string? SapUser { get; set; }
        public string? PoNumber { get; set; }
        public int? PoItem { get; set; }
        public string? MaterialDoc { get; set; }
        public string? DecisionId { get; set; }
        public string Status { get; set; } = "ACCEPTED";
        public string? ErrorCode { get; set; }
        public string? ErrorMessage { get; set; }
        public string PayloadJson { get; set; } = "{}";
    }

    /// <summary>Per-event result of the store: ACCEPTED, PARKED or DUPLICATE.</summary>
    public sealed class SapEventSaveResult
    {
        public string MsgId { get; set; } = "";
        public string Result { get; set; } = "";
    }

    /// <summary>The event feed state (lag, last seq, counts).</summary>
    public sealed class SapEventStatus
    {
        public DateTimeOffset? LastReceivedAt { get; set; }
        public DateTimeOffset? LastOccurredAt { get; set; }
        public long? LastSeq { get; set; }
        /// <summary>received_at − occurred_at of the most recently received event (seconds); 0 when there is none.</summary>
        public long LagSeconds { get; set; }
        public int Total { get; set; }
        public int Accepted { get; set; }
        public int Parked { get; set; }
        public int Late { get; set; }
        public int NotApplied { get; set; }
        public int Today { get; set; }
    }

    /// <summary>SAP_REASON_CODES row. Code_Type EXCEPTION = served by GET /reason-codes (approver dropdown, ≤ 4 chars); VERDICT = item reason codes of /decisions.</summary>
    public sealed class SapReasonCodeRow
    {
        public string CodeType { get; set; } = "EXCEPTION";
        public string Code { get; set; } = "";
        public string Text { get; set; } = "";
        public bool Active { get; set; } = true;
        public int SortOrder { get; set; }
        public string? Verdict { get; set; }
        public string? RuleIds { get; set; }
        public DateTimeOffset? UpdatedAt { get; set; }
        public string? UpdatedBy { get; set; }
    }

    // ── portal requests (QPS: POST api/v1/<Action> with a request object) ──

    /// <summary>SapDecisionList: filters (empty = all).</summary>
    public sealed class SapDecisionListReq : BaseRequest
    {
        public string? From { get; set; }
        public string? To { get; set; }
        public string? PoNumber { get; set; }
        public string? Verdict { get; set; }
        public string? Mode { get; set; }
        /// <summary>"all" (default), "ok" (HTTP 200), "error" (NOT_CHECKED answers).</summary>
        public string? Status { get; set; }
        public int Max { get; set; } = 200;
    }

    /// <summary>SapDocumentReport / SapDocumentReportExport: filters (empty = all).</summary>
    public sealed class SapDocumentReportReq : BaseRequest
    {
        public string? From { get; set; }
        public string? To { get; set; }
        /// <summary>Contains: PO number, request_id or document number.</summary>
        public string? PoNumber { get; set; }
        public string? Verdict { get; set; }
        public string? Mode { get; set; }
        /// <summary>"yes" (default: POs with a final document), "no", "all".</summary>
        public string? HasDocument { get; set; } = "yes";
        /// <summary>Include portal test decisions.</summary>
        public bool IncludeTests { get; set; }
        public int Max { get; set; } = 500;
    }

    /// <summary>SapVerdictView: the token of a verdict link.</summary>
    public sealed class SapVerdictViewReq : BaseRequest
    {
        public string? Token { get; set; }
    }

    /// <summary>SapDecisionGet: one decision log row by its id.</summary>
    public sealed class SapDecisionGetReq : BaseRequest
    {
        public long Id { get; set; }
    }

    /// <summary>SapEventList: filters.</summary>
    public sealed class SapEventListReq : BaseRequest
    {
        public string? From { get; set; }
        public string? To { get; set; }
        public string? EventType { get; set; }
        public string? Status { get; set; }
        public string? Q { get; set; }
        public int Max { get; set; } = 200;
    }

    /// <summary>SapEventGet: one event with its payload.</summary>
    public sealed class SapEventGetReq : BaseRequest
    {
        public long Id { get; set; }
    }

    /// <summary>SapTechUserAdd: a new technical user; empty Password = generate one (shown once).</summary>
    public sealed class SapTechUserAddReq : BaseRequest
    {
        public string? UserName { get; set; }
        public string? SourceSystem { get; set; }
        public string? Description { get; set; }
        public string? Password { get; set; }
    }

    /// <summary>SapTechUserUpdate: Action = "enable", "disable", "unlock" or "reset-password" (Password empty = generate).</summary>
    public sealed class SapTechUserUpdateReq : BaseRequest
    {
        public string? UserName { get; set; }
        public string? Action { get; set; }
        public string? Password { get; set; }
    }

    /// <summary>SapReasonCodeSave: add or change an exception reason code (GET /reason-codes).</summary>
    public sealed class SapReasonCodeSaveReq : BaseRequest
    {
        public string? Code { get; set; }
        public string? Text { get; set; }
        public bool Active { get; set; } = true;
        public int SortOrder { get; set; }
    }

    /// <summary>SapTestDecision: a /decisions request body (JSON text) run through the real decision path, logged as a test.</summary>
    public sealed class SapTestDecisionReq : BaseRequest
    {
        public string? RequestJson { get; set; }
    }

    // ── portal responses (ApiDataResponse<T>.Data) ──

    public sealed class SapOverviewResponse
    {
        public bool Enabled { get; set; }
        public bool Available { get; set; }
        public string? UnavailableReason { get; set; }
        public string Environment { get; set; } = "";
        public string RulesVersion { get; set; } = "";
        public string BlockVerdict { get; set; } = "";
        public string UnknownItemPolicy { get; set; } = "";
        public int DecisionTimeoutMs { get; set; }
        public int EventsLagLimitSeconds { get; set; }
        public bool EnforceIpAllowList { get; set; }
        public List<string> AllowedIps { get; set; } = new();
        public List<string> TrustedProxies { get; set; } = new();
        public int OnlyOnPort { get; set; }
        public SapEventStatus Events { get; set; } = new();
        public SapDecisionStats Decisions { get; set; } = new();
        public string? BwSnapshot { get; set; }
        /// <summary>Environment the API runs as (ASPNETCORE_ENVIRONMENT / ApiMaster:Environment): picks the API_BASE_URL rows.</summary>
        public string? ApiEnvironment { get; set; }
        /// <summary>Base of the verdict links (view_url) in use; null = no links are sent (see <see cref="VerdictLinkProblem"/>).</summary>
        public string? VerdictLinkBase { get; set; }
        public string? VerdictLinkProblem { get; set; }
    }

    public sealed class SapDecisionStats
    {
        public int Today { get; set; }
        public int TodayNotChecked { get; set; }
        public int Total { get; set; }
        public int NotChecked { get; set; }
        public int Allow { get; set; }
        public int Reduce { get; set; }
        public int Watch { get; set; }
        public int Hold { get; set; }
        public int Block { get; set; }
        public int Shadow { get; set; }
        public int Live { get; set; }
        public int? P50Ms { get; set; }
        public int? P95Ms { get; set; }
        public int? MaxMs { get; set; }
    }

    public sealed class SapDecisionListResponse
    {
        public List<SapDecisionLogRow> Items { get; set; } = new();
        public int Total { get; set; }
    }

    public sealed class SapDecisionDetailResponse
    {
        public SapDecisionLogRow? Decision { get; set; }
        public List<SapDecisionItemRow> Items { get; set; } = new();
        /// <summary>Final documents (MRN): the current one first, then replaced ones.</summary>
        public List<SapDocumentRow> Documents { get; set; } = new();
    }

    public sealed class SapDocumentReportResponse
    {
        public List<SapDocumentReportRow> Items { get; set; } = new();
        public int Total { get; set; }
    }

    /// <summary>
    /// The read-only verdict page behind a verdict link (/verdict/&lt;token&gt;, no sign-in): business fields only — no request /
    /// response JSON, technical user, IP or engine internals.
    /// </summary>
    public sealed class SapVerdictView
    {
        public string? DecisionId { get; set; }
        public string RequestId { get; set; } = "";
        public string? PoNumber { get; set; }
        public string? BuyerUser { get; set; }
        public string? Division { get; set; }
        public string? Mode { get; set; }
        public string? OverallVerdict { get; set; }
        public string? VerdictCode { get; set; }
        public string? RouteTo { get; set; }
        public bool BmHeadRequired { get; set; }
        public string? SummaryText { get; set; }
        public string? RulesVersion { get; set; }
        public string? SourceSystem { get; set; }
        /// <summary>data_as_of.bw_snapshot of the answer: time of the stock data the decision used.</summary>
        public string? BwSnapshot { get; set; }
        public bool DataDelayed { get; set; }
        public DateTimeOffset? RequestedAt { get; set; }
        public DateTimeOffset DecidedAt { get; set; }
        public DateTimeOffset? ExpiresAt { get; set; }
        public List<SapVerdictViewItem> Items { get; set; } = new();
        public SapDocumentRow? Document { get; set; }
    }

    public sealed class SapVerdictViewItem
    {
        public int PoItem { get; set; }
        public string? Article { get; set; }
        public string? GenericArticle { get; set; }
        public string? Colour { get; set; }
        public string? DeliveryMonth { get; set; }
        public double OrderQty { get; set; }
        public string? Verdict { get; set; }
        public double? MaxQty { get; set; }
        public string? ReasonCode { get; set; }
        public string? RuleId { get; set; }
        public string? ReasonText { get; set; }
        /// <summary>Figures behind the verdict (items[].metrics of the answer); null when they do not apply.</summary>
        public double? PlanQty { get; set; }
        public double? StockQty { get; set; }
        public double? OpenPoQty { get; set; }
        public double? HeadroomQty { get; set; }
        public double? CoverDays { get; set; }
        public double? SellThroughPct { get; set; }
        /// <summary>Total quantity of the items decided together (size variants of one generic article + colour + month).</summary>
        public double GroupQty { get; set; }
    }

    public sealed class SapEventListResponse
    {
        public List<SapEventRow> Items { get; set; } = new();
        public int Total { get; set; }
        public SapEventStatus Status { get; set; } = new();
    }

    public sealed class SapTechUsersResponse
    {
        public List<SapTechUser> Users { get; set; } = new();
        public int MinPasswordLength { get; set; }
    }

    /// <summary>Answer of add / reset-password: the generated password is returned once (never stored in clear).</summary>
    public sealed class SapTechUserResponse
    {
        public SapTechUser? User { get; set; }
        public string? GeneratedPassword { get; set; }
    }

    public sealed class SapReasonCodesListResponse
    {
        public List<SapReasonCodeRow> Items { get; set; } = new();
    }

    /// <summary>SapTestDecision: the HTTP status and the JSON body /decisions would have answered.</summary>
    public sealed class SapTestDecisionResponse
    {
        public int HttpStatus { get; set; }
        public string ResponseJson { get; set; } = "";
        public long? LogId { get; set; }
        public int DurationMs { get; set; }
        public string SampleRequestJson { get; set; } = "";
    }
}
