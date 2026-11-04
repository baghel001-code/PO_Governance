using System.Text.Json.Serialization;

namespace Domain.Entities.Sap
{
    // ── POST /api/sap/v1/mra-status (SAP → tool) ─────────────────────────────

    /// <summary>A batch of MRA status changes from SAP (1–500 lines, applied in order).</summary>
    public sealed class SapMraStatusRequest
    {
        [JsonPropertyName("mras")] public List<SapMraLineIn>? Mras { get; set; }
    }

    /// <summary>One MRA line. Required: mra_no, qty, status. The other fields are kept when not sent.</summary>
    public sealed class SapMraLineIn
    {
        [JsonPropertyName("mra_no")] public string? MraNo { get; set; }
        [JsonPropertyName("qty")] public decimal? Qty { get; set; }
        [JsonPropertyName("status")] public string? Status { get; set; }
        /// <summary>Generic article + colour pick the line of an MRA with several lines.</summary>
        [JsonPropertyName("generic_article")] public string? GenericArticle { get; set; }
        [JsonPropertyName("colour")] public string? Colour { get; set; }
        /// <summary>When the change happened in SAP (ISO 8601 with offset); an older change than the stored one is ignored (STALE).</summary>
        [JsonPropertyName("changed_at")] public string? ChangedAt { get; set; }
        [JsonPropertyName("request_id")] public string? RequestId { get; set; }
        [JsonPropertyName("generic_art_desc")] public string? GenericArtDesc { get; set; }
        [JsonPropertyName("vendor_code")] public string? VendorCode { get; set; }
        [JsonPropertyName("vendor_name")] public string? VendorName { get; set; }
        [JsonPropertyName("brand_type")] public string? BrandType { get; set; }
        [JsonPropertyName("mc_code")] public string? McCode { get; set; }
        [JsonPropertyName("mc_name")] public string? McName { get; set; }
        [JsonPropertyName("mrp")] public decimal? Mrp { get; set; }
        [JsonPropertyName("planning_flag")] public string? PlanningFlag { get; set; }
        [JsonPropertyName("fit")] public string? Fit { get; set; }
        [JsonPropertyName("design")] public string? Design { get; set; }
        [JsonPropertyName("style")] public string? Style { get; set; }
        [JsonPropertyName("fabric")] public string? Fabric { get; set; }
        [JsonPropertyName("product_material")] public string? ProductMaterial { get; set; }
        [JsonPropertyName("po_value")] public decimal? PoValue { get; set; }
        /// <summary>YYYY-MM-DD.</summary>
        [JsonPropertyName("delivery_date")] public string? DeliveryDate { get; set; }
        [JsonPropertyName("approval_level")] public string? ApprovalLevel { get; set; }
        [JsonPropertyName("remark")] public string? Remark { get; set; }
    }

    /// <summary>POST /mra-status answer.</summary>
    public sealed class SapMraStatusResponse
    {
        [JsonPropertyName("batch_id")] public string BatchId { get; set; } = "";
        [JsonPropertyName("received")] public int Received { get; set; }
        [JsonPropertyName("created")] public int Created { get; set; }
        [JsonPropertyName("updated")] public int Updated { get; set; }
        [JsonPropertyName("unchanged")] public int Unchanged { get; set; }
        [JsonPropertyName("stale")] public int Stale { get; set; }
        [JsonPropertyName("ambiguous")] public int Ambiguous { get; set; }
        [JsonPropertyName("results")] public List<SapMraLineResult> Results { get; set; } = new();
        [JsonPropertyName("received_at")] public string ReceivedAt { get; set; } = "";
    }

    public sealed class SapMraLineResult
    {
        /// <summary>Position in the request (0-based).</summary>
        [JsonPropertyName("index")] public int Index { get; set; }
        [JsonPropertyName("mra_no")] public string MraNo { get; set; } = "";
        [JsonPropertyName("generic_article")] public string? GenericArticle { get; set; }
        [JsonPropertyName("colour")] public string? Colour { get; set; }
        /// <summary>CREATED | UPDATED | UNCHANGED | STALE | AMBIGUOUS.</summary>
        [JsonPropertyName("result")] public string Result { get; set; } = "";
        [JsonPropertyName("previous_status")] public string? PreviousStatus { get; set; }
        /// <summary>The line's status now (after this message).</summary>
        [JsonPropertyName("status")] public string? Status { get; set; }
        [JsonPropertyName("qty")] public decimal? Qty { get; set; }
    }

    public static class MraApplyResults
    {
        public const string Created = "CREATED";
        public const string Updated = "UPDATED";
        public const string Unchanged = "UNCHANGED";
        public const string Stale = "STALE";
        public const string Ambiguous = "AMBIGUOUS";
    }

    /// <summary>Matches the spellings of an MRA status to the configured values (SapInterface:MraStatuses).</summary>
    public static class MraStatuses
    {
        private static readonly Dictionary<string, string> Aliases = new(StringComparer.Ordinal)
        {
            ["ALLOWED"] = "ALLOW", ["WATCH"] = "WATCHLIST", ["WATCH_LIST"] = "WATCHLIST",
            ["CANCEL"] = "CANCELLED", ["CANCELED"] = "CANCELLED", ["DISCARD"] = "DISCARDED",
            ["REJECT"] = "REJECTED", ["SELF_REJECT"] = "SELF_REJECTED", ["SELFREJECT"] = "SELF_REJECTED", ["SELFREJECTED"] = "SELF_REJECTED"
        };

        /// <summary>"Self Reject" / "self-rejected" → SELF_REJECTED; null when it is none of <paramref name="allowed"/>.</summary>
        public static string? Normalize(string? value, IReadOnlyCollection<string> allowed)
        {
            var v = (value ?? "").Trim().ToUpperInvariant();
            if (v.Length == 0) return null;
            v = string.Join("_", v.Split(new[] { ' ', '-', '_', '/' }, StringSplitOptions.RemoveEmptyEntries));
            if (Aliases.TryGetValue(v, out var a)) v = a;
            var set = allowed.Select(x => x.Trim().ToUpperInvariant()).ToHashSet(StringComparer.Ordinal);
            return set.Contains(v) ? v : null;
        }
    }

    // ── storage ──────────────────────────────────────────────────────────────

    /// <summary>One MRA line as stored (SAP_MRA_STATUS) — also the line read from the morning file or sent by SAP.</summary>
    public sealed class MraLine
    {
        public long Id { get; set; }
        public string MraNo { get; set; } = "";
        public string GenericArticle { get; set; } = "";
        public string Colour { get; set; } = "";
        public string? GenericArtDesc { get; set; }
        public string? VendorCode { get; set; }
        public string? VendorName { get; set; }
        public string? BrandType { get; set; }
        public string? McCode { get; set; }
        public string? McName { get; set; }
        public decimal? Mrp { get; set; }
        public string? PlanningFlag { get; set; }
        public string? Fit { get; set; }
        public string? Design { get; set; }
        public string? Style { get; set; }
        public string? Fabric { get; set; }
        public string? ProductMaterial { get; set; }
        public decimal Qty { get; set; }
        public decimal? PoValue { get; set; }
        public DateTime? DeliveryDate { get; set; }
        public string Status { get; set; } = "";
        public string? ApprovalLevel { get; set; }
        public string? RequestId { get; set; }
        public string? Remark { get; set; }
        /// <summary>UPLOAD (morning file) | SAP (during the day).</summary>
        public string? Source { get; set; }
        public string? UploadId { get; set; }
        public DateTimeOffset? ChangedAt { get; set; }
        public DateTimeOffset? StatusChangedAt { get; set; }
        public DateTimeOffset? UpdatedAt { get; set; }
        public string? UpdatedBy { get; set; }
    }

    public sealed class MraApplyRow
    {
        public int Idx { get; set; }
        public string MraNo { get; set; } = "";
        public string GenericArticle { get; set; } = "";
        public string Colour { get; set; } = "";
        public string Result { get; set; } = "";
        public string? OldStatus { get; set; }
        public string? Status { get; set; }
        public decimal? Qty { get; set; }
    }

    public sealed class MraHistoryRow
    {
        public long Id { get; set; }
        public string MraNo { get; set; } = "";
        public string GenericArticle { get; set; } = "";
        public string Colour { get; set; } = "";
        public string Action { get; set; } = "";
        public string? OldStatus { get; set; }
        public string NewStatus { get; set; } = "";
        public decimal? OldQty { get; set; }
        public decimal NewQty { get; set; }
        public DateTimeOffset? ChangedAt { get; set; }
        public DateTimeOffset ReceivedAt { get; set; }
        public string? TechUser { get; set; }
        public string? SourceSystem { get; set; }
        public string? BatchId { get; set; }
    }

    public sealed class MraStatusCount
    {
        public string Status { get; set; } = "";
        public int Lines { get; set; }
        public decimal Qty { get; set; }
    }

    public sealed class MraSummary
    {
        public int Lines { get; set; }
        public int Mras { get; set; }
        public decimal Qty { get; set; }
        public List<MraStatusCount> ByStatus { get; set; } = new();
        public string? LastUploadId { get; set; }
        public string? LastUploadFile { get; set; }
        public int? LastUploadRows { get; set; }
        public DateTimeOffset? LastUploadAt { get; set; }
        public string? LastUploadBy { get; set; }
        public DateTimeOffset? LastSapChangeAt { get; set; }
        /// <summary>SAP changes received since the start of today (business time).</summary>
        public int SapChangesToday { get; set; }
    }
}

namespace Domain.Entities.Sap
{
    // ── portal (Data Setup → MRA status) ────────────────────────────────────

    public sealed class MraListReq
    {
        public string? Status { get; set; }
        public string? Search { get; set; }
        public int Page { get; set; } = 1;
        public int PageSize { get; set; } = 100;
    }

    public sealed class MraListResponse
    {
        public List<MraLine> Items { get; set; } = new();
        public int Total { get; set; }
        public int Page { get; set; }
        public int PageSize { get; set; }
    }

    public sealed class MraNoReq
    {
        public string? MraNo { get; set; }
    }

    public sealed class MraHistoryResponse
    {
        public string MraNo { get; set; } = "";
        public List<MraHistoryRow> Items { get; set; } = new();
    }

    public sealed class MraSummaryResponse
    {
        public MraSummary Summary { get; set; } = new();
        /// <summary>The statuses the tool accepts (SapInterface:MraStatuses).</summary>
        public List<string> Statuses { get; set; } = new();
        public bool Available { get; set; } = true;
        public string? UnavailableReason { get; set; }
    }

    /// <summary>The morning file: what was read, what was refused.</summary>
    public sealed class MraUploadResponse
    {
        public bool Ok { get; set; }
        public string? UploadId { get; set; }
        public string FileName { get; set; } = "";
        public string? Sheet { get; set; }
        /// <summary>Lines stored (the table was emptied first).</summary>
        public int Rows { get; set; }
        /// <summary>Lines of the previous data that were replaced.</summary>
        public int Replaced { get; set; }
        /// <summary>Empty rows of the file that were skipped.</summary>
        public int SkippedEmpty { get; set; }
        /// <summary>Why the file was refused (nothing changed): "Row 7: Qty is missing" …</summary>
        public List<string> Errors { get; set; } = new();
        /// <summary>Header → field the importer used (for checking a new layout).</summary>
        public Dictionary<string, string> Columns { get; set; } = new();
        public List<MraStatusCount> ByStatus { get; set; } = new();
    }
}
