using System.Text.Json;
using System.Text.Json.Serialization;

namespace Domain.Entities.Store
{
    /// <summary>
    /// "uploads" log line (table upload_log): a manifest entry plus key / action
    /// ("upload" | "restore" | "clear") and, for restores, restoredFrom.
    /// </summary>
    public sealed class UploadLogEntry
    {
        [JsonPropertyName("version")] public string? Version { get; set; }
        [JsonPropertyName("rows"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public int? Rows { get; set; }
        [JsonPropertyName("bytes"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public long? Bytes { get; set; }
        [JsonPropertyName("label"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Label { get; set; }
        [JsonPropertyName("updatedBy")] public string? UpdatedBy { get; set; }
        [JsonPropertyName("updatedAt")] public string? UpdatedAt { get; set; }
        [JsonPropertyName("uploadId")] public string? UploadId { get; set; }
        [JsonPropertyName("restoredBy"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? RestoredBy { get; set; }
        [JsonPropertyName("restoredAt"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? RestoredAt { get; set; }
        [JsonPropertyName("key")] public string? Key { get; set; }
        [JsonPropertyName("action")] public string? Action { get; set; }
        [JsonPropertyName("restoredFrom"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? RestoredFrom { get; set; }
        [JsonExtensionData] public Dictionary<string, JsonElement>? Extra { get; set; }
    }

    /// <summary>"batches" log line (table upload_batches): one upload action = one Upload ID.</summary>
    public sealed class UploadBatch
    {
        [JsonPropertyName("uploadId")] public string UploadId { get; set; } = "";
        [JsonPropertyName("at")] public string? At { get; set; }
        [JsonPropertyName("by")] public string? By { get; set; }
        [JsonPropertyName("source")] public string? Source { get; set; }
        [JsonPropertyName("sourceFiles")] public List<string> SourceFiles { get; set; } = new();
        [JsonPropertyName("note")] public string Note { get; set; } = "";
        [JsonExtensionData] public Dictionary<string, JsonElement>? Extra { get; set; }
    }

    /// <summary>
    /// "runs" log line (table run_index): the client's run meta (lines, …) plus id / at / by / bytes /
    /// runNo — or a deletion marker {id, deleted:true, by, at}. Kept as raw JSON fields in <see cref="Extra"/>.
    /// </summary>
    public sealed class RunIndexEntry
    {
        [JsonPropertyName("id")] public string Id { get; set; } = "";
        [JsonPropertyName("at")] public string? At { get; set; }
        [JsonPropertyName("by")] public string? By { get; set; }
        [JsonPropertyName("runNo"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? RunNo { get; set; }
        [JsonPropertyName("deleted"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public bool Deleted { get; set; }
        [JsonExtensionData] public Dictionary<string, JsonElement>? Extra { get; set; }
    }

    /// <summary>
    /// "feedback" log line (table feedback_log): type "feedback" (a new item) or type "status"
    /// (an admin's status change for an item).
    /// </summary>
    public sealed class FeedbackRecord
    {
        [JsonPropertyName("type")] public string? Type { get; set; }
        [JsonPropertyName("id")] public string Id { get; set; } = "";
        [JsonPropertyName("at")] public string? At { get; set; }
        [JsonPropertyName("by")] public string? By { get; set; }
        [JsonPropertyName("byName")] public string? ByName { get; set; }
        [JsonPropertyName("roles"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public List<string>? Roles { get; set; }
        [JsonPropertyName("kind"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Kind { get; set; }
        [JsonPropertyName("screen"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Screen { get; set; }
        [JsonPropertyName("article"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Article { get; set; }
        [JsonPropertyName("message"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Message { get; set; }
        [JsonPropertyName("rating")] public int? Rating { get; set; }
        [JsonPropertyName("view"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? View { get; set; }
        [JsonPropertyName("status")] public string? Status { get; set; }
        [JsonPropertyName("note"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Note { get; set; }
        [JsonExtensionData] public Dictionary<string, JsonElement>? Extra { get; set; }
    }
}
