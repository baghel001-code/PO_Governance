using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Domain.Entities.Sap;

namespace Application.Services.V1.Sap
{
    /// <summary>JSON of the SAP contract: the [JsonPropertyName] snake_case names of Domain.Entities.Sap, UTF-8, no naming policy.</summary>
    public static class SapJson
    {
        public static readonly JsonSerializerOptions Options = new()
        {
            PropertyNamingPolicy = null,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            PropertyNameCaseInsensitive = false,
            ReadCommentHandling = JsonCommentHandling.Disallow,
            AllowTrailingCommas = false
        };

        public static string Serialize<T>(T value) => JsonSerializer.Serialize(value, Options);

        public static T? Deserialize<T>(string json) => JsonSerializer.Deserialize<T>(json, Options);

        public static string Error(string errorCode, string message, string? traceId) =>
            Serialize(new SapError { ErrorCode = errorCode, Message = message, TraceId = traceId });

        /// <summary>The body re-written without insignificant whitespace (same member order) — what the idempotency hash covers.</summary>
        public static string Canonical(string body)
        {
            var node = JsonNode.Parse(body, documentOptions: new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Disallow });
            return node?.ToJsonString(new JsonSerializerOptions { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping }) ?? "null";
        }

        public static string Sha256Hex(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();

        /// <summary>"T-" + 6 hex characters (spec Error.trace_id example "T-7f3a9c").</summary>
        public static string NewTraceId() => "T-" + Convert.ToHexString(RandomNumberGenerator.GetBytes(3)).ToLowerInvariant();

        private static readonly Regex IsoWithOffset = new(@"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}(:\d{2}(\.\d{1,7})?)?(Z|[+-]\d{2}:\d{2})$", RegexOptions.Compiled);

        /// <summary>ISO 8601 date-time with an offset (spec §3), e.g. 2026-10-01T11:42:07+05:30.</summary>
        public static bool TryParseTimestamp(string? s, out DateTimeOffset value)
        {
            value = default;
            var t = (s ?? "").Trim();
            return IsoWithOffset.IsMatch(t) && DateTimeOffset.TryParse(t, CultureInfo.InvariantCulture, DateTimeStyles.None, out value);
        }

        /// <summary>yyyy-MM-ddTHH:mm:sszzz in the given offset (the format of the spec examples).</summary>
        public static string Timestamp(DateTimeOffset t) => t.ToString("yyyy-MM-dd'T'HH:mm:sszzz", CultureInfo.InvariantCulture);
    }
}
