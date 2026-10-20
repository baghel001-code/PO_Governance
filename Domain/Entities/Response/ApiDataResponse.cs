using Domain.Entities.Base;
using Domain.Entities.Common;

namespace Domain.Entities.Response
{
    /// <summary>
    /// Response of every POGovApi action: <see cref="BaseResponse"/> (responseCode, responseMessage, recordId)
    /// plus the action's result in <c>Data</c>. On the wire (PropertyNamingPolicy = null, as in QPS):
    /// <c>{"responseCode":0,"responseMessage":"SUCCESS","recordId":0,"Data":{…}}</c>; the DTOs inside
    /// Data keep their own [JsonPropertyName] names (v1.1.4 camelCase).
    /// </summary>
    public class ApiDataResponse<T> : BaseResponse
    {
        public T? Data { get; set; }

        public static ApiDataResponse<T> Ok(T? data) => new() { Data = data };

        public static ApiDataResponse<T> Fail(int code, string? message, T? data = default) =>
            new() { responseCode = code, responseMessage = message, Data = data };
    }

    /// <summary>Response with no data (sign-out, delete …).</summary>
    public class GenRes : BaseResponse
    {
    }

    /// <summary>GetAllApi / GetSpecificApi (QPS ApiMasterRes / APIRes): the rows of API_MASTER.</summary>
    public class ApiMasterRes : BaseResponse
    {
        public List<ApiData>? ApiData { get; set; }
    }

    /// <summary>
    /// A file returned by the API (Excel / CSV / zip exports) — what <c>IAPICall.CallingAPIDownload</c> returns.
    /// <see cref="Headers"/> holds the X-* response headers (e.g. X-Export-Message, X-Message), URL-decoded.
    /// </summary>
    public class ApiFileResponse : BaseResponse
    {
        public string FileName { get; set; } = "download";
        public string ContentType { get; set; } = "application/octet-stream";
        public byte[] FileBytes { get; set; } = Array.Empty<byte>();
        /// <summary>The file as base64 (QPS CallingAPI returned file answers this way).</summary>
        public string Base64 => Convert.ToBase64String(FileBytes);
        public Dictionary<string, string> Headers { get; set; } = new(StringComparer.OrdinalIgnoreCase);

        public string? Header(string name) => Headers.TryGetValue(name, out var v) && v.Length > 0 ? v : null;
    }
}
