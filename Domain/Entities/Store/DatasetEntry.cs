using System.Text.Json;
using System.Text.Json.Serialization;

namespace Domain.Entities.Store
{
    /// <summary>
    /// manifest.datasets[key] — which stored version of a data file is current (same JSON as v1.1.4).
    /// </summary>
    public sealed class DatasetEntry
    {
        [JsonPropertyName("version")] public string Version { get; set; } = "";
        [JsonPropertyName("rows")] public int Rows { get; set; }
        [JsonPropertyName("bytes")] public long Bytes { get; set; }
        [JsonPropertyName("label")] public string? Label { get; set; }
        [JsonPropertyName("updatedBy")] public string? UpdatedBy { get; set; }
        [JsonPropertyName("updatedAt")] public string? UpdatedAt { get; set; }
        [JsonPropertyName("uploadId")] public string? UploadId { get; set; }
        [JsonPropertyName("restoredBy")] public string? RestoredBy { get; set; }
        [JsonPropertyName("restoredAt")] public string? RestoredAt { get; set; }
        [JsonExtensionData] public Dictionary<string, JsonElement>? Extra { get; set; }
    }

    /// <summary>The "manifest" document.</summary>
    public sealed class Manifest
    {
        [JsonPropertyName("dataVersion")] public long DataVersion { get; set; }
        [JsonPropertyName("paramsVersion")] public long ParamsVersion { get; set; }
        [JsonPropertyName("datasets")] public Dictionary<string, DatasetEntry> Datasets { get; set; } = new();
        [JsonExtensionData] public Dictionary<string, JsonElement>? Extra { get; set; }
    }
}
