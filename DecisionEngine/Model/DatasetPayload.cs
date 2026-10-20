using System.IO.Compression;
using System.Text.Json;

namespace DecisionEngine.Model
{
    /// <summary>
    /// One stored data-file version: {"rows":[…], "meta":{…}} (gzip-compressed in the database,
    /// table dataset_versions — the same format v1.1.4 wrote, so existing data loads unchanged).
    /// </summary>
    public sealed class DatasetPayload
    {
        public List<SheetRow> Rows { get; set; } = new();
        /// <summary>Optional metadata, e.g. {"grcPeriodType":"week"} for the GRC file.</summary>
        public Dictionary<string, JsonElement>? Meta { get; set; }

        public static DatasetPayload FromJson(ReadOnlySpan<byte> json)
        {
            var reader = new Utf8JsonReader(json, new JsonReaderOptions { AllowTrailingCommas = true });
            using var doc = JsonDocument.ParseValue(ref reader);
            var p = new DatasetPayload();
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return p;
            if (root.TryGetProperty("rows", out var rows) && rows.ValueKind == JsonValueKind.Array)
                foreach (var r in rows.EnumerateArray())
                    if (r.ValueKind == JsonValueKind.Object) p.Rows.Add(SheetRow.FromJson(r));
            if (root.TryGetProperty("meta", out var meta) && meta.ValueKind == JsonValueKind.Object)
                p.Meta = meta.EnumerateObject().ToDictionary(x => x.Name, x => x.Value.Clone());
            return p;
        }

        public static DatasetPayload FromGzip(byte[] gz)
        {
            using var src = new GZipStream(new MemoryStream(gz), CompressionMode.Decompress);
            using var ms = new MemoryStream();
            src.CopyTo(ms);
            return FromJson(ms.ToArray());
        }

        public byte[] ToJson()
        {
            using var ms = new MemoryStream();
            using (var w = new Utf8JsonWriter(ms, new JsonWriterOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }))
            {
                w.WriteStartObject();
                w.WritePropertyName("rows");
                w.WriteStartArray();
                foreach (var r in Rows) r.WriteJson(w);
                w.WriteEndArray();
                if (Meta != null && Meta.Count > 0)
                {
                    w.WritePropertyName("meta");
                    w.WriteStartObject();
                    foreach (var kv in Meta) { w.WritePropertyName(kv.Key); kv.Value.WriteTo(w); }
                    w.WriteEndObject();
                }
                w.WriteEndObject();
            }
            return ms.ToArray();
        }

        public byte[] ToGzip()
        {
            var json = ToJson();
            using var ms = new MemoryStream();
            using (var gz = new GZipStream(ms, CompressionLevel.Optimal, leaveOpen: true)) gz.Write(json);
            return ms.ToArray();
        }

        public string? MetaString(string key) =>
            Meta != null && Meta.TryGetValue(key, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
    }
}
