using Domain.Entities.Response;

namespace Admin.Interfaces
{
    /// <summary>
    /// The portal's only way to reach data: HTTP calls to POGovApi with the signed-in user's session
    /// token. Never throws for HTTP errors — failures come back as ApiResponse with ResponseCode = status.
    /// </summary>
    public interface IAPICallService
    {
        Task<ApiResponse<T>> GetAsync<T>(string path, CancellationToken ct = default);
        Task<ApiResponse<T>> PostAsync<T>(string path, object? body = null, CancellationToken ct = default);
        Task<ApiResponse<T>> PutAsync<T>(string path, object? body = null, CancellationToken ct = default);
        Task<ApiResponse<T>> DeleteAsync<T>(string path, CancellationToken ct = default);
        /// <summary>multipart/form-data upload (field "files").</summary>
        Task<ApiResponse<T>> UploadAsync<T>(string path, IEnumerable<UploadFile> files, IDictionary<string, string>? fields = null, CancellationToken ct = default);
        /// <summary>Downloads a file (Excel exports). Null data on failure.</summary>
        Task<ApiResponse<DownloadedFile>> DownloadAsync(string path, object? body = null, CancellationToken ct = default);
        /// <summary>Calls without a user token (login).</summary>
        Task<ApiResponse<T>> PostAnonymousAsync<T>(string path, object? body, CancellationToken ct = default);
        /// <summary>Calls with an explicit token (during sign-in, before the cookie exists).</summary>
        Task<ApiResponse<T>> SendWithTokenAsync<T>(HttpMethod method, string path, string token, object? body = null, CancellationToken ct = default);
    }

    public sealed record UploadFile(string FileName, Stream Content, string ContentType = "application/octet-stream");
    public sealed record DownloadedFile(string FileName, string ContentType, byte[] Bytes);
}
