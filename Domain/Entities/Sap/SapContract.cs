using System.Text.Json.Serialization;

namespace Domain.Entities.Sap
{
    // ─────────────────────────────────────────────────────────────────────────────
    // The SAP R/3 integration contract — legacy/python-app/docs/po-governance-sap-api.yaml (OpenAPI 3.0, spec v1.4).
    // Field names are snake_case exactly as in the YAML (NOT the portal's PascalCase QPS convention); every property
    // carries its [JsonPropertyName]. Request properties are nullable so a missing mandatory field can be reported
    // (400 VALIDATION_FAILED) instead of defaulting. tests/POGovernance.Tests/Sap/SapContractTests compares these
    // classes with the YAML schemas.
    // ─────────────────────────────────────────────────────────────────────────────

    /// <summary>YAML components/schemas/Error — the body of every non-200 answer on /api/sap/v1.</summary>
    public sealed class SapError
    {
        [JsonPropertyName("error_code")] public string ErrorCode { get; set; } = "";
        [JsonPropertyName("message")] public string Message { get; set; } = "";
        [JsonPropertyName("trace_id")] public string? TraceId { get; set; }
    }

    /// <summary>GET /ping (PingResponse).</summary>
    public sealed class SapPingResponse
    {
        [JsonPropertyName("status")] public string Status { get; set; } = "ok";
        [JsonPropertyName("server_time")] public string ServerTime { get; set; } = "";
        [JsonPropertyName("api_version")] public string ApiVersion { get; set; } = "";
        [JsonPropertyName("environment")] public string Environment { get; set; } = "";
        [JsonPropertyName("source_system_recognised")] public string SourceSystemRecognised { get; set; } = "";
    }

    /// <summary>ReasonCode (GET /reason-codes).</summary>
    public sealed class SapReasonCode
    {
        [JsonPropertyName("code")] public string Code { get; set; } = "";
        [JsonPropertyName("text")] public string Text { get; set; } = "";
        [JsonPropertyName("active")] public bool Active { get; set; }
    }

    /// <summary>GET /reason-codes 200 body.</summary>
    public sealed class SapReasonCodesResponse
    {
        [JsonPropertyName("version")] public string Version { get; set; } = "";
        [JsonPropertyName("reason_codes")] public List<SapReasonCode> ReasonCodes { get; set; } = new();
    }

    // ── POST /decisions ──────────────────────────────────────────────────────

    /// <summary>DecisionRequest.</summary>
    public sealed class SapDecisionRequest
    {
        [JsonPropertyName("request_id")] public string? RequestId { get; set; }
        [JsonPropertyName("mode")] public string? Mode { get; set; }
        [JsonPropertyName("source_system")] public string? SourceSystem { get; set; }
        [JsonPropertyName("client")] public string? Client { get; set; }
        [JsonPropertyName("otb_check")] public string? OtbCheck { get; set; }
        [JsonPropertyName("depth_check")] public string? DepthCheck { get; set; }
        [JsonPropertyName("previous_decision_id")] public string? PreviousDecisionId { get; set; }
        [JsonPropertyName("requested_at")] public string? RequestedAt { get; set; }
        [JsonPropertyName("purchase_order")] public SapDecisionPurchaseOrder? PurchaseOrder { get; set; }
    }

    /// <summary>DecisionRequest.purchase_order.</summary>
    public sealed class SapDecisionPurchaseOrder
    {
        [JsonPropertyName("po_number")] public string? PoNumber { get; set; }
        [JsonPropertyName("doc_type")] public string? DocType { get; set; }
        [JsonPropertyName("vendor")] public string? Vendor { get; set; }
        [JsonPropertyName("purch_org")] public string? PurchOrg { get; set; }
        [JsonPropertyName("purch_group")] public string? PurchGroup { get; set; }
        [JsonPropertyName("buyer_user")] public string? BuyerUser { get; set; }
        [JsonPropertyName("buyer_email")] public string? BuyerEmail { get; set; }
        [JsonPropertyName("division")] public string? Division { get; set; }
        [JsonPropertyName("currency")] public string? Currency { get; set; }
        [JsonPropertyName("items")] public List<SapDecisionRequestItem>? Items { get; set; }
    }

    /// <summary>DecisionRequest.purchase_order.items[].</summary>
    public sealed class SapDecisionRequestItem
    {
        [JsonPropertyName("po_item")] public int? PoItem { get; set; }
        [JsonPropertyName("article")] public string? Article { get; set; }
        [JsonPropertyName("generic_article")] public string? GenericArticle { get; set; }
        [JsonPropertyName("colour")] public string? Colour { get; set; }
        [JsonPropertyName("merch_category")] public string? MerchCategory { get; set; }
        [JsonPropertyName("season")] public string? Season { get; set; }
        [JsonPropertyName("site")] public string? Site { get; set; }
        [JsonPropertyName("order_qty")] public double? OrderQty { get; set; }
        [JsonPropertyName("uom")] public string? Uom { get; set; }
        [JsonPropertyName("net_value")] public double? NetValue { get; set; }
        [JsonPropertyName("delivery_date")] public string? DeliveryDate { get; set; }
    }

    /// <summary>DecisionResponse.</summary>
    public sealed class SapDecisionResponse
    {
        [JsonPropertyName("decision_id")] public string DecisionId { get; set; } = "";
        [JsonPropertyName("request_id")] public string RequestId { get; set; } = "";
        [JsonPropertyName("mode")] public string Mode { get; set; } = "";
        [JsonPropertyName("overall_verdict")] public string OverallVerdict { get; set; } = "";
        [JsonPropertyName("verdict_code")] public string VerdictCode { get; set; } = "";
        [JsonPropertyName("route_to")] public string RouteTo { get; set; } = "";
        [JsonPropertyName("bm_head_required")] public bool BmHeadRequired { get; set; }
        [JsonPropertyName("summary_text")] public string SummaryText { get; set; } = "";
        [JsonPropertyName("rules_version")] public string RulesVersion { get; set; } = "";
        [JsonPropertyName("data_delayed")] public bool DataDelayed { get; set; }
        [JsonPropertyName("data_as_of")] public SapDataAsOf DataAsOf { get; set; } = new();
        [JsonPropertyName("decided_at")] public string DecidedAt { get; set; } = "";
        /// <summary>
        /// Link to the verdict page on the portal (/verdict/&lt;token&gt;, random unguessable token, read-only, no sign-in) for the
        /// buyer / approver; omitted when the verdict link is switched off or the portal base URL is not configured.
        /// </summary>
        [JsonPropertyName("view_url"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? ViewUrl { get; set; }
        /// <summary>Until when <see cref="ViewUrl"/> works.</summary>
        [JsonPropertyName("view_url_expires_at"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? ViewUrlExpiresAt { get; set; }
        [JsonPropertyName("items")] public List<SapDecisionResponseItem> Items { get; set; } = new();
    }

    /// <summary>POST /documents: the final SAP document (MRN) of a request_id that was decided.</summary>
    public sealed class SapDocumentRequest
    {
        [JsonPropertyName("request_id")] public string? RequestId { get; set; }
        [JsonPropertyName("document_no")] public string? DocumentNo { get; set; }
        /// <summary>Default MRN.</summary>
        [JsonPropertyName("document_type")] public string? DocumentType { get; set; }
        /// <summary>Default: the PO number of the decision.</summary>
        [JsonPropertyName("po_number")] public string? PoNumber { get; set; }
        /// <summary>YYYY-MM-DD.</summary>
        [JsonPropertyName("document_date")] public string? DocumentDate { get; set; }
        /// <summary>true = correct a document already stored for the request_id (the old one is kept in the history).</summary>
        [JsonPropertyName("replace")] public bool Replace { get; set; }
        [JsonPropertyName("note")] public string? Note { get; set; }
    }

    /// <summary>POST /documents answer.</summary>
    public sealed class SapDocumentResponse
    {
        [JsonPropertyName("request_id")] public string RequestId { get; set; } = "";
        [JsonPropertyName("decision_id")] public string? DecisionId { get; set; }
        [JsonPropertyName("document_no")] public string DocumentNo { get; set; } = "";
        [JsonPropertyName("document_type")] public string DocumentType { get; set; } = "";
        /// <summary>STORED (first document) | UNCHANGED (same document again) | REPLACED (replace = true).</summary>
        [JsonPropertyName("status")] public string Status { get; set; } = "";
        [JsonPropertyName("received_at")] public string ReceivedAt { get; set; } = "";
    }

    /// <summary>DecisionResponse.data_as_of.</summary>
    public sealed class SapDataAsOf
    {
        /// <summary>Upload time of the stock file (F1) — the day's base until the BW 04:00 feed exists.</summary>
        [JsonPropertyName("bw_snapshot"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? BwSnapshot { get; set; }
        [JsonPropertyName("events_lag_seconds")] public long EventsLagSeconds { get; set; }
    }

    /// <summary>DecisionResponse.items[].</summary>
    public sealed class SapDecisionResponseItem
    {
        [JsonPropertyName("po_item")] public int PoItem { get; set; }
        [JsonPropertyName("verdict")] public string Verdict { get; set; } = "";
        [JsonPropertyName("verdict_code")] public string VerdictCode { get; set; } = "";
        /// <summary>Maximum allowed qty when the verdict is REDUCE; null otherwise (nullable in the YAML).</summary>
        [JsonPropertyName("max_qty")] public double? MaxQty { get; set; }
        [JsonPropertyName("reason_code")] public string ReasonCode { get; set; } = "";
        [JsonPropertyName("reason_text")] public string ReasonText { get; set; } = "";
        [JsonPropertyName("rule_id")] public string RuleId { get; set; } = "";
        [JsonPropertyName("metrics")] public SapDecisionMetrics Metrics { get; set; } = new();
    }

    /// <summary>DecisionResponse.items[].metrics — a figure the engine path does not produce is left out.</summary>
    public sealed class SapDecisionMetrics
    {
        [JsonPropertyName("plan_qty"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public double? PlanQty { get; set; }
        [JsonPropertyName("stock_qty"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public double? StockQty { get; set; }
        [JsonPropertyName("open_po_qty"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public double? OpenPoQty { get; set; }
        [JsonPropertyName("headroom_qty"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public double? HeadroomQty { get; set; }
        [JsonPropertyName("cover_days"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public double? CoverDays { get; set; }
        [JsonPropertyName("sell_through_pct"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public double? SellThroughPct { get; set; }
    }

    // ── POST /events ─────────────────────────────────────────────────────────

    /// <summary>EventBatch.</summary>
    public sealed class SapEventBatch
    {
        [JsonPropertyName("source_system")] public string? SourceSystem { get; set; }
        [JsonPropertyName("client")] public string? Client { get; set; }
        [JsonPropertyName("queue")] public string? Queue { get; set; }
        [JsonPropertyName("sent_at")] public string? SentAt { get; set; }
        [JsonPropertyName("events")] public List<System.Text.Json.Nodes.JsonObject?>? Events { get; set; }
    }

    /// <summary>Event (validated one by one: a bad event is parked, never the whole batch).</summary>
    public sealed class SapEvent
    {
        [JsonPropertyName("msg_id")] public string? MsgId { get; set; }
        [JsonPropertyName("seq")] public long? Seq { get; set; }
        [JsonPropertyName("event_type")] public string? EventType { get; set; }
        [JsonPropertyName("occurred_at")] public string? OccurredAt { get; set; }
        [JsonPropertyName("sap_user")] public string? SapUser { get; set; }
        [JsonPropertyName("purchase_order")] public SapPurchaseOrderState? PurchaseOrder { get; set; }
        [JsonPropertyName("goods_movement")] public SapGoodsMovement? GoodsMovement { get; set; }
        [JsonPropertyName("governance_outcome")] public SapGovernanceOutcome? GovernanceOutcome { get; set; }
    }

    /// <summary>PurchaseOrderState.</summary>
    public sealed class SapPurchaseOrderState
    {
        [JsonPropertyName("po_number")] public string? PoNumber { get; set; }
        [JsonPropertyName("doc_type")] public string? DocType { get; set; }
        [JsonPropertyName("vendor")] public string? Vendor { get; set; }
        [JsonPropertyName("purch_org")] public string? PurchOrg { get; set; }
        [JsonPropertyName("purch_group")] public string? PurchGroup { get; set; }
        [JsonPropertyName("created_on")] public string? CreatedOn { get; set; }
        [JsonPropertyName("created_by")] public string? CreatedBy { get; set; }
        [JsonPropertyName("release_status")] public string? ReleaseStatus { get; set; }
        [JsonPropertyName("release_indicator")] public string? ReleaseIndicator { get; set; }
        [JsonPropertyName("currency")] public string? Currency { get; set; }
        [JsonPropertyName("deleted")] public bool? Deleted { get; set; }
        [JsonPropertyName("items")] public List<SapPurchaseOrderItem>? Items { get; set; }
    }

    /// <summary>PurchaseOrderItem.</summary>
    public sealed class SapPurchaseOrderItem
    {
        [JsonPropertyName("po_item")] public int? PoItem { get; set; }
        [JsonPropertyName("article")] public string? Article { get; set; }
        [JsonPropertyName("generic_article")] public string? GenericArticle { get; set; }
        [JsonPropertyName("colour")] public string? Colour { get; set; }
        [JsonPropertyName("merch_category")] public string? MerchCategory { get; set; }
        [JsonPropertyName("site")] public string? Site { get; set; }
        [JsonPropertyName("order_qty")] public double? OrderQty { get; set; }
        [JsonPropertyName("received_qty")] public double? ReceivedQty { get; set; }
        [JsonPropertyName("returned_qty")] public double? ReturnedQty { get; set; }
        [JsonPropertyName("open_qty")] public double? OpenQty { get; set; }
        [JsonPropertyName("uom")] public string? Uom { get; set; }
        [JsonPropertyName("net_price")] public double? NetPrice { get; set; }
        [JsonPropertyName("price_unit")] public double? PriceUnit { get; set; }
        [JsonPropertyName("delivery_date")] public string? DeliveryDate { get; set; }
        [JsonPropertyName("deletion_flag")] public bool? DeletionFlag { get; set; }
        [JsonPropertyName("delivery_completed")] public bool? DeliveryCompleted { get; set; }
    }

    /// <summary>GoodsMovement.</summary>
    public sealed class SapGoodsMovement
    {
        [JsonPropertyName("material_doc")] public string? MaterialDoc { get; set; }
        [JsonPropertyName("doc_year")] public int? DocYear { get; set; }
        [JsonPropertyName("doc_item")] public int? DocItem { get; set; }
        [JsonPropertyName("movement_type")] public string? MovementType { get; set; }
        [JsonPropertyName("debit_credit")] public string? DebitCredit { get; set; }
        [JsonPropertyName("posting_date")] public string? PostingDate { get; set; }
        [JsonPropertyName("article")] public string? Article { get; set; }
        [JsonPropertyName("generic_article")] public string? GenericArticle { get; set; }
        [JsonPropertyName("colour")] public string? Colour { get; set; }
        [JsonPropertyName("site")] public string? Site { get; set; }
        [JsonPropertyName("storage_location")] public string? StorageLocation { get; set; }
        [JsonPropertyName("quantity")] public double? Quantity { get; set; }
        [JsonPropertyName("uom")] public string? Uom { get; set; }
        [JsonPropertyName("po_number")] public string? PoNumber { get; set; }
        [JsonPropertyName("po_item")] public int? PoItem { get; set; }
        [JsonPropertyName("reversal_of")] public string? ReversalOf { get; set; }
    }

    /// <summary>GovernanceOutcome.</summary>
    public sealed class SapGovernanceOutcome
    {
        [JsonPropertyName("decision_id")] public string? DecisionId { get; set; }
        [JsonPropertyName("po_number")] public string? PoNumber { get; set; }
        [JsonPropertyName("po_item")] public int? PoItem { get; set; }
        [JsonPropertyName("outcome")] public string? Outcome { get; set; }
        [JsonPropertyName("by_user")] public string? ByUser { get; set; }
        [JsonPropertyName("by_role")] public string? ByRole { get; set; }
        [JsonPropertyName("at")] public string? At { get; set; }
        [JsonPropertyName("new_qty")] public double? NewQty { get; set; }
        [JsonPropertyName("reason_code")] public string? ReasonCode { get; set; }
        [JsonPropertyName("note")] public string? Note { get; set; }
    }

    /// <summary>EventBatchResult.</summary>
    public sealed class SapEventBatchResult
    {
        [JsonPropertyName("received_at")] public string ReceivedAt { get; set; } = "";
        [JsonPropertyName("accepted")] public List<string> Accepted { get; set; } = new();
        [JsonPropertyName("duplicates")] public List<string> Duplicates { get; set; } = new();
        [JsonPropertyName("parked")] public List<SapParkedEvent> Parked { get; set; } = new();
    }

    /// <summary>EventBatchResult.parked[].</summary>
    public sealed class SapParkedEvent
    {
        [JsonPropertyName("msg_id")] public string MsgId { get; set; } = "";
        [JsonPropertyName("error_code")] public string ErrorCode { get; set; } = "";
        [JsonPropertyName("message")] public string Message { get; set; } = "";
    }

    /// <summary>The enums of the contract (YAML) — the validators and the contract tests use these lists.</summary>
    public static class SapEnums
    {
        public static readonly string[] Modes = { "SHADOW", "LIVE" };
        public static readonly string[] OtbCheck = { "PASSED" };
        public static readonly string[] DepthCheck = { "PASSED", "BYPASSED" };
        public static readonly string[] EventTypes =
        {
            "PO_CREATED", "PO_CHANGED", "PO_RELEASED", "PO_RELEASE_REVOKED", "PO_CLOSED", "PO_DELETED",
            "GR_POSTED", "GR_REVERSED", "RETURN_POSTED", "RETURN_REVERSED", "GOV_OUTCOME"
        };
        public static readonly string[] PoEventTypes = { "PO_CREATED", "PO_CHANGED", "PO_RELEASED", "PO_RELEASE_REVOKED", "PO_CLOSED", "PO_DELETED" };
        public static readonly string[] GoodsMovementEventTypes = { "GR_POSTED", "GR_REVERSED", "RETURN_POSTED", "RETURN_REVERSED" };
        public static readonly string[] ReleaseStatus = { "NOT_RELEASED", "IN_RELEASE", "RELEASED", "REFUSED" };
        public static readonly string[] DebitCredit = { "S", "H" };
        public static readonly string[] Outcomes =
        {
            "BUYER_ACCEPTED_REDUCTION", "EXCEPTION_REQUESTED", "EXCEPTION_APPROVED", "EXCEPTION_REJECTED",
            "PO_RELEASED", "PO_WITHDRAWN", "NOT_CHECKED_TIMEOUT"
        };
        public static readonly string[] ByRoles = { "BUYER", "CATHEAD", "BMHEAD", "SYSTEM" };
        public static readonly string[] Environments = { "PRODUCTION", "TEST" };
    }

    /// <summary>error_code values this server sends (Error.error_code is a free string in the YAML).</summary>
    public static class SapErrorCodes
    {
        public const string Unauthorized = "UNAUTHORIZED";
        public const string UserLocked = "USER_LOCKED";
        public const string IpNotAllowed = "IP_NOT_ALLOWED";
        public const string InvalidJson = "INVALID_JSON";
        public const string ValidationFailed = "VALIDATION_FAILED";
        public const string RequestIdConflict = "REQUEST_ID_CONFLICT";
        public const string CannotDecide = "CANNOT_DECIDE";
        public const string BatchTooLarge = "BATCH_TOO_LARGE";
        public const string DecisionTimeout = "DECISION_TIMEOUT";
        public const string DataNotLoaded = "DATA_NOT_LOADED";
        public const string InterfaceDisabled = "SAP_INTERFACE_DISABLED";
        public const string StorageNotSupported = "STORAGE_NOT_SUPPORTED";
        public const string DatabaseUnavailable = "DATABASE_UNAVAILABLE";
        public const string NotFound = "NOT_FOUND";
        /// <summary>POST /documents: no answered decision for the request_id.</summary>
        public const string RequestNotFound = "REQUEST_NOT_FOUND";
        /// <summary>POST /documents: another document is already stored for the request_id (send replace = true).</summary>
        public const string DocumentConflict = "DOCUMENT_CONFLICT";
        public const string InternalError = "INTERNAL_ERROR";
    }
}
