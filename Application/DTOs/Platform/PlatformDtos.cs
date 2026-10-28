using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Domain.Entities.Store;

namespace Application.DTOs.Platform
{
    // ── status / health ─────────────────────────────────────────────
    public sealed class AiFlags
    {
        [JsonPropertyName("chat")] public bool Chat { get; set; }
        [JsonPropertyName("insights")] public bool Insights { get; set; }
    }

    /// <summary>server.py GET /api/status.</summary>
    public sealed class StatusResponse
    {
        [JsonPropertyName("version")] public string Version { get; set; } = "";
        [JsonPropertyName("serverTime")] public string ServerTime { get; set; } = "";
        [JsonPropertyName("today")] public string Today { get; set; } = "";
        [JsonPropertyName("dataVersion")] public long DataVersion { get; set; }
        [JsonPropertyName("paramsVersion")] public long ParamsVersion { get; set; }
        [JsonPropertyName("datasets")] public Dictionary<string, DatasetEntry> Datasets { get; set; } = new();
        [JsonPropertyName("params")] public JsonNode? Params { get; set; }
        [JsonPropertyName("reqSeq")] public int ReqSeq { get; set; }
        [JsonPropertyName("me")] public UserPublicDto? Me { get; set; }
        [JsonPropertyName("pendingApprovals")] public int PendingApprovals { get; set; }
        [JsonPropertyName("ai")] public AiFlags Ai { get; set; } = new();
    }

    public sealed class HealthResponse
    {
        [JsonPropertyName("ok")] public bool Ok { get; set; } = true;
        [JsonPropertyName("version")] public string Version { get; set; } = "";
        [JsonPropertyName("storage")] public string Storage { get; set; } = "";
        /// <summary>How people sign in (the portal's sign-in page shows Employee ID or Email from it).</summary>
        [JsonPropertyName("login")] public LoginInfo? Login { get; set; }
    }

    // ── audit / backups ─────────────────────────────────────────────
    public sealed class AuditItem
    {
        [JsonPropertyName("at")] public string At { get; set; } = "";
        [JsonPropertyName("who")] public string? Who { get; set; }
        [JsonPropertyName("action")] public string? Action { get; set; }
        [JsonPropertyName("detail")] public string? Detail { get; set; }
    }

    public sealed class AuditQueryResponse
    {
        [JsonPropertyName("items")] public List<AuditItem> Items { get; set; } = new();
        [JsonPropertyName("total")] public int Total { get; set; }
    }

    public sealed class BackupInfo
    {
        [JsonPropertyName("name")] public string Name { get; set; } = "";
        [JsonPropertyName("bytes")] public long Bytes { get; set; }
        [JsonPropertyName("at")] public string At { get; set; } = "";
    }

    public sealed class BackupListResponse
    {
        [JsonPropertyName("items")] public List<BackupInfo> Items { get; set; } = new();
        [JsonPropertyName("dir")] public string Dir { get; set; } = "";
        [JsonPropertyName("hour")] public int? Hour { get; set; }
        [JsonPropertyName("keepDays")] public int KeepDays { get; set; }
    }

    public sealed class BackupResponse
    {
        [JsonPropertyName("ok")] public bool Ok { get; set; } = true;
        [JsonPropertyName("name")] public string Name { get; set; } = "";
    }

    // ── approvals / requests ────────────────────────────────────────
    public sealed class ApprovalsResponse
    {
        [JsonPropertyName("pending")] public List<JsonObject> Pending { get; set; } = new();
        [JsonPropertyName("decided")] public List<JsonObject> Decided { get; set; } = new();
    }

    public sealed class SeqResponse
    {
        [JsonPropertyName("ok")] public bool Ok { get; set; } = true;
        [JsonPropertyName("seq")] public int Seq { get; set; }
    }

    public sealed class RequestsDayResponse
    {
        [JsonPropertyName("date")] public string Date { get; set; } = "";
        [JsonPropertyName("seq")] public int Seq { get; set; }
        [JsonPropertyName("requests")] public List<JsonObject> Requests { get; set; } = new();
        [JsonPropertyName("today")] public string Today { get; set; } = "";
    }

    public sealed class SaveRequestResponse
    {
        [JsonPropertyName("ok")] public bool Ok { get; set; } = true;
        [JsonPropertyName("id")] public long Id { get; set; }
        [JsonPropertyName("reqId")] public string ReqId { get; set; } = "";
        [JsonPropertyName("seq")] public int Seq { get; set; }
        [JsonPropertyName("approval")] public JsonNode? Approval { get; set; }
    }

    /// <summary>409 payload of a stale save (server.py api_POST_requests).</summary>
    public sealed class RequestConflict
    {
        [JsonPropertyName("conflict")] public bool Conflict { get; set; } = true;
        [JsonPropertyName("byOthers")] public string ByOthers { get; set; } = "";
        [JsonPropertyName("seq")] public int Seq { get; set; }
    }

    public sealed class MustChangePayload
    {
        [JsonPropertyName("mustChange")] public bool MustChange { get; set; } = true;
    }

    // ── data / uploads / history ────────────────────────────────────
    public sealed class DataSaveResponse
    {
        [JsonPropertyName("ok")] public bool Ok { get; set; } = true;
        [JsonPropertyName("dataVersion")] public long DataVersion { get; set; }
        [JsonPropertyName("entry")] public DatasetEntry? Entry { get; set; }
        [JsonPropertyName("users"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public ScrSyncResult? Users { get; set; }
        [JsonPropertyName("usersError"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? UsersError { get; set; }
    }

    public sealed class DataVersionResponse
    {
        [JsonPropertyName("ok")] public bool Ok { get; set; } = true;
        [JsonPropertyName("dataVersion")] public long DataVersion { get; set; }
    }

    public sealed class UploadStartRequest
    {
        [JsonPropertyName("source")] public string? Source { get; set; }
        [JsonPropertyName("sourceFiles")] public List<string?>? SourceFiles { get; set; }
    }

    public sealed class UploadStartResponse
    {
        [JsonPropertyName("ok")] public bool Ok { get; set; } = true;
        [JsonPropertyName("uploadId")] public string UploadId { get; set; } = "";
    }

    public sealed class UploadRestoreResponse
    {
        [JsonPropertyName("ok")] public bool Ok { get; set; } = true;
        [JsonPropertyName("uploadId")] public string UploadId { get; set; } = "";
        [JsonPropertyName("dataVersion")] public long DataVersion { get; set; }
        [JsonPropertyName("keys")] public List<string> Keys { get; set; } = new();
    }

    /// <summary>One upload action in the history (server.py api_GET_history uploads group).</summary>
    public sealed class UploadHistoryGroup
    {
        [JsonPropertyName("uploadId")] public string UploadId { get; set; } = "";
        [JsonPropertyName("at")] public string? At { get; set; }
        [JsonPropertyName("by")] public string? By { get; set; }
        [JsonPropertyName("source")] public string? Source { get; set; }
        [JsonPropertyName("sourceFiles")] public List<string> SourceFiles { get; set; } = new();
        [JsonPropertyName("note")] public string? Note { get; set; }
        /// <summary>Upload log lines with "stored" / "current" flags added.</summary>
        [JsonPropertyName("files")] public List<JsonObject> Files { get; set; } = new();
    }

    public sealed class UploadHistoryResponse
    {
        [JsonPropertyName("items")] public List<UploadHistoryGroup> Items { get; set; } = new();
        [JsonPropertyName("storageBytes")] public long StorageBytes { get; set; }
    }

    public sealed class ItemsResponse
    {
        [JsonPropertyName("items")] public List<JsonObject> Items { get; set; } = new();
    }

    // ── runs / params / feedback ────────────────────────────────────
    public sealed class RunSaveResponse
    {
        [JsonPropertyName("ok")] public bool Ok { get; set; } = true;
        [JsonPropertyName("id")] public string Id { get; set; } = "";
        [JsonPropertyName("meta")] public JsonObject Meta { get; set; } = new();
    }

    public sealed class ParamsSaveResponse
    {
        [JsonPropertyName("ok")] public bool Ok { get; set; } = true;
        [JsonPropertyName("paramsVersion")] public long ParamsVersion { get; set; }
    }

    public sealed class FeedbackCreatedResponse
    {
        [JsonPropertyName("ok")] public bool Ok { get; set; } = true;
        [JsonPropertyName("id")] public string Id { get; set; } = "";
    }

    // ── AI ──────────────────────────────────────────────────────────
    /// <summary>Upstream answer: HTTP status + JSON body (Anthropic shape for chat, OpenAI shape for insights).</summary>
    public sealed record AiResult(int Status, JsonNode Data);
}
