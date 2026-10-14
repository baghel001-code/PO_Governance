using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Admin.Interfaces;
using Domain.Constants;
using Domain.Entities.Response;
using Microsoft.AspNetCore.Components.Authorization;

namespace Admin.Services
{
    public sealed class APICallService : IAPICallService
    {
        public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

        private readonly IHttpClientFactory _factory;
        private readonly AuthenticationStateProvider _auth;
        private readonly ILogger<APICallService> _log;

        public APICallService(IHttpClientFactory factory, AuthenticationStateProvider auth, ILogger<APICallService> log)
        {
            _factory = factory;
            _auth = auth;
            _log = log;
        }

        private async Task<string?> TokenAsync()
        {
            var state = await _auth.GetAuthenticationStateAsync();
            return state.User.FindFirst(CustomClaimTypes.SessionToken)?.Value;
        }

        public Task<ApiResponse<T>> GetAsync<T>(string path, CancellationToken ct = default) => SendAsync<T>(HttpMethod.Get, path, null, ct);
        public Task<ApiResponse<T>> PostAsync<T>(string path, object? body = null, CancellationToken ct = default) => SendAsync<T>(HttpMethod.Post, path, body, ct);
        public Task<ApiResponse<T>> PutAsync<T>(string path, object? body = null, CancellationToken ct = default) => SendAsync<T>(HttpMethod.Put, path, body, ct);
        public Task<ApiResponse<T>> DeleteAsync<T>(string path, CancellationToken ct = default) => SendAsync<T>(HttpMethod.Delete, path, null, ct);

        public Task<ApiResponse<T>> PostAnonymousAsync<T>(string path, object? body, CancellationToken ct = default) =>
            SendCoreAsync<T>(HttpMethod.Post, path, body, null, ct);

        public Task<ApiResponse<T>> SendWithTokenAsync<T>(HttpMethod method, string path, string token, object? body = null, CancellationToken ct = default) =>
            SendCoreAsync<T>(method, path, body, token, ct);

        private async Task<ApiResponse<T>> SendAsync<T>(HttpMethod method, string path, object? body, CancellationToken ct) =>
            await SendCoreAsync<T>(method, path, body, await TokenAsync(), ct);

        private async Task<ApiResponse<T>> SendCoreAsync<T>(HttpMethod method, string path, object? body, string? token, CancellationToken ct)
        {
            using var req = new HttpRequestMessage(method, path.TrimStart('/'));
            if (token != null) req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            if (body != null) req.Content = JsonContent.Create(body, body.GetType(), options: Json);
            return await ExecuteAsync<T>(req, ct);
        }

        public async Task<ApiResponse<T>> UploadAsync<T>(string path, IEnumerable<UploadFile> files, IDictionary<string, string>? fields = null, CancellationToken ct = default)
        {
            using var form = new MultipartFormDataContent();
            foreach (var f in files)
            {
                var sc = new StreamContent(f.Content);
                sc.Headers.ContentType = new MediaTypeHeaderValue(f.ContentType);
                form.Add(sc, "files", f.FileName);
            }
            if (fields != null)
                foreach (var kv in fields) form.Add(new StringContent(kv.Value), kv.Key);
            using var req = new HttpRequestMessage(HttpMethod.Post, path.TrimStart('/')) { Content = form };
            var token = await TokenAsync();
            if (token != null) req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            return await ExecuteAsync<T>(req, ct);
        }

        public async Task<ApiResponse<DownloadedFile>> DownloadAsync(string path, object? body = null, CancellationToken ct = default)
        {
            using var req = new HttpRequestMessage(body == null ? HttpMethod.Get : HttpMethod.Post, path.TrimStart('/'));
            var token = await TokenAsync();
            if (token != null) req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            if (body != null) req.Content = JsonContent.Create(body, body.GetType(), options: Json);
            try
            {
                using var res = await _factory.CreateClient(AppConstants.ApiClientName).SendAsync(req, ct);
                if (!res.IsSuccessStatusCode) return await ErrorFromAsync<DownloadedFile>(res, ct);
                var bytes = await res.Content.ReadAsByteArrayAsync(ct);
                var name = res.Content.Headers.ContentDisposition?.FileNameStar ?? res.Content.Headers.ContentDisposition?.FileName ?? "download";
                return ApiResponse<DownloadedFile>.Ok(new DownloadedFile(name.Trim('"'), res.Content.Headers.ContentType?.MediaType ?? "application/octet-stream", bytes));
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                _log.LogWarning(ex, "API download {Path} failed", path);
                return ApiResponse<DownloadedFile>.Fail(503, "Cannot reach the server. Check your network and try again.");
            }
        }

        private async Task<ApiResponse<T>> ExecuteAsync<T>(HttpRequestMessage req, CancellationToken ct)
        {
            try
            {
                using var res = await _factory.CreateClient(AppConstants.ApiClientName).SendAsync(req, ct);
                if (!res.IsSuccessStatusCode) return await ErrorFromAsync<T>(res, ct);
                if (res.StatusCode == HttpStatusCode.NoContent) return ApiResponse<T>.Ok(default);
                var body = await res.Content.ReadFromJsonAsync<ApiResponse<T>>(Json, ct);
                return body ?? ApiResponse<T>.Ok(default);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
            {
                _log.LogWarning(ex, "API call {Method} {Path} failed", req.Method, req.RequestUri);
                return ApiResponse<T>.Fail(503, "Cannot reach the server. Check your network and try again.");
            }
            catch (JsonException ex)
            {
                _log.LogWarning(ex, "API call {Method} {Path} returned unexpected content", req.Method, req.RequestUri);
                return ApiResponse<T>.Fail(500, "The server returned an unexpected response.");
            }
        }

        private static async Task<ApiResponse<T>> ErrorFromAsync<T>(HttpResponseMessage res, CancellationToken ct)
        {
            var code = (int)res.StatusCode;
            try
            {
                var body = await res.Content.ReadFromJsonAsync<ApiResponse<T>>(Json, ct);
                if (body != null)
                {
                    if (body.ResponseCode == 0) body.ResponseCode = code;
                    return body;
                }
            }
            catch { /* not an ApiResponse body */ }
            var msg = code switch
            {
                401 => "Please log in",
                403 => "Not allowed",
                404 => "Not found",
                503 => "The database is not reachable right now - please try again in a minute.",
                _ => "Server error (" + code + ")"
            };
            return ApiResponse<T>.Fail(code, msg);
        }
    }
}
