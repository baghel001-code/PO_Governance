using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Application.Interfaces.V1;
using Domain.Constants;
using Domain.Entities.Base;
using Domain.Entities.Common;
using Domain.Entities.Response;
using Microsoft.Extensions.Logging;
using MyNewEncDec;

namespace Infrastructure.Services.V1
{
    /// <summary>
    /// QPS <c>Infrastructure/Services/V1/APICallService</c>: calls POGovApi by task name. The API master row
    /// (<see cref="AllApi.ApiDetails"/>, loaded at portal start-up from GetAllApi) gives the URL, HTTP method,
    /// content type, timeout (seconds), proxy (when <c>AppConfigurationSettings:ProxyValue</c> = 1) and the
    /// static API token (<c>Authorization: Bearer …</c>); the signed-in user's session token goes in
    /// <c>X-Session-Token</c> (<see cref="ISessionTokenAccessor"/>).
    /// <para>Differences from QPS (kept the behaviour PO Governance had before):</para>
    /// <list type="bullet">
    /// <item>an HTTP error answer is not turned into a generic 1004: its JSON body (responseCode / responseMessage,
    /// e.g. 401 "Please log in", 409 conflict data) is returned, with responseCode = the HTTP status;</item>
    /// <item>one shared connection pool per proxy instead of a new HttpClient per call;</item>
    /// <item>when the API master list could not be loaded at start-up it is loaded again on the next call;</item>
    /// <item>multipart uploads and file downloads (bytes + name + type + X-* headers) have their own methods.</item>
    /// </list>
    /// Never throws for HTTP / network errors (see <see cref="IAPICall"/> for the fallback codes); a cancelled
    /// <c>CancellationToken</c> still throws <see cref="OperationCanceledException"/>.
    /// </summary>
    public class APICallService : IAPICall
    {
        public const int CodeNoApi = 1000, CodeHtml = 1001, CodeEmpty = 1002, CodeUnreachable = 1003, CodeError = 1004;

        /// <summary>Response JSON: case-insensitive names (QPS ReadFromJsonAsync defaults).</summary>
        public static readonly JsonSerializerOptions ReadJson = new(JsonSerializerDefaults.Web);

        /// <summary>Request JSON: property names as declared (PropertyNamingPolicy = null, QPS JsonSerializer.Serialize).</summary>
        public static readonly JsonSerializerOptions WriteJson = new()
        {
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };

        private static readonly ConcurrentDictionary<string, HttpClient> Clients = new();
        private static readonly SemaphoreSlim MasterGate = new(1, 1);
        private static DateTime _lastMasterAttemptUtc = DateTime.MinValue;
        /// <summary>Why the API master list could not be loaded (shown instead of "task not found" while it is missing).</summary>
        private static (int Code, string Message)? _masterFailure;

        private readonly int _useProxy;
        private readonly ISessionTokenAccessor? _sessions;
        private readonly ILogger? _log;

        /// <summary>QPS constructor (the portal's start-up call of the API master list).</summary>
        public APICallService(int UseProxy) : this(UseProxy, null, null)
        {
        }

        public APICallService(int useProxy, ISessionTokenAccessor? sessions, ILogger? log)
        {
            _useProxy = useProxy;
            _sessions = sessions;
            _log = log;
        }

        /// <summary>How long a failed API master load waits before the next call tries again.</summary>
        public static TimeSpan MasterRetryInterval { get; set; } = TimeSpan.FromSeconds(10);

        // ── API master list ──────────────────────────────────────────────────

        /// <summary>
        /// Loads <see cref="AllApi.ApiDetails"/> through <see cref="AllApiNames.MasterApi"/> (MasterApiSettings).
        /// Each row's Api_Authorization is My_Decode-d (stored encoded, like JwtSettings:SecretKey); an empty one
        /// falls back to the MasterApiSettings token. Returns false (and logs why) when the API did not answer.
        /// </summary>
        public async Task<bool> LoadApiMasterAsync(CancellationToken ct = default)
        {
            var master = ApiMasterData.apiData;
            if (master == null || string.IsNullOrWhiteSpace(master.Api_Url))
            {
                _log?.LogError("MasterApiSettings:Api_Url is not set in appsettings.json - the portal cannot find POGovApi (GetAllApi).");
                return false;
            }
            var res = await CallingAPI<ApiMasterRes, EmptyBody>(AllApiNames.MasterApi, new EmptyBody(), null, ct);
            if (res.responseCode != 0 || res.ApiData == null || res.ApiData.Count == 0)
            {
                _masterFailure = (res.responseCode != 0 ? res.responseCode : CodeNoApi,
                    res.responseCode == CodeUnreachable ? res.responseMessage ?? "" : "The portal could not load its API list (" + res.responseMessage + ").");
                _log?.LogError("Could not load the API master list from {Url} ({Code}: {Message}). Is POGovApi running, does " +
                               "MasterApiSettings:Api_Authorization match JwtSettings:SecretKey, and was database/sqlserver/04_api_master.sql run? " +
                               "The portal tries again on the next call.", master.Api_Url, res.responseCode, res.responseMessage);
                return false;
            }
            var enc = new New_Enc_Dec();
            var badTokens = new List<string>();
            foreach (var row in res.ApiData)
            {
                if (!string.IsNullOrWhiteSpace(row.Api_Authorization))
                {
                    string? decoded = null;
                    try { decoded = enc.My_Decode(row.Api_Authorization.Trim()); }
                    catch (Exception e)
                    {
                        _log?.LogWarning(e, "API_MASTER {Task}: Api_Authorization is not My_Encode text - using MasterApiSettings:Api_Authorization", row.Task_Name);
                    }
                    if (decoded != null && !IsHeaderSafe(decoded))
                    {
                        // My_Decode of text that was not My_Encode-d with this key (a plain token, another key, a copy from
                        // another system) yields non-ASCII characters, which HttpClient refuses to put in a header.
                        badTokens.Add(row.Task_Name ?? "?");
                        decoded = null;
                    }
                    row.Api_Authorization = decoded ?? master.Api_Authorization;
                }
                else row.Api_Authorization = master.Api_Authorization;
            }
            if (badTokens.Count > 0)
                _log?.LogWarning("API_MASTER.Api_Authorization of {Count} task(s) (e.g. {Example}) does not decode to a valid token - it must be " +
                                 "My_Encode of the static API token, the same text as JwtSettings:SecretKey. Using MasterApiSettings:Api_Authorization " +
                                 "for them. Fix with: UPDATE dbo.API_MASTER SET Api_Authorization = N'<JwtSettings:SecretKey>'",
                                 badTokens.Count, badTokens[0]);
            AllApi.ApiDetails = res.ApiData;
            _masterFailure = null;
            _log?.LogInformation("API master: {Count} APIs loaded from {Url}", res.ApiData.Count, master.Api_Url);
            return true;
        }

        private sealed class EmptyBody { }

        private async Task<ApiData?> FindAsync(string apiName, CancellationToken ct)
        {
            if (apiName == AllApiNames.MasterApi || apiName == "All") return ApiMasterData.apiData;
            var row = AllApi.ApiDetails?.FirstOrDefault(x => x.Task_Name == apiName);
            if (row != null) return row;
            // Lazy retry: the API was down when the portal started (or the list is from an older API_MASTER).
            if (DateTime.UtcNow - _lastMasterAttemptUtc < MasterRetryInterval) return null;
            await MasterGate.WaitAsync(ct);
            try
            {
                row = AllApi.ApiDetails?.FirstOrDefault(x => x.Task_Name == apiName);
                if (row != null || DateTime.UtcNow - _lastMasterAttemptUtc < MasterRetryInterval) return row;
                _lastMasterAttemptUtc = DateTime.UtcNow;
                await LoadApiMasterAsync(ct);
                return AllApi.ApiDetails?.FirstOrDefault(x => x.Task_Name == apiName);
            }
            finally { MasterGate.Release(); }
        }

        // ── IAPICall ─────────────────────────────────────────────────────────

        public Task<RepT> CallingAPI<RepT, ReqT>(string ApiName, ReqT requestBody) => CallingAPI<RepT, ReqT>(ApiName, requestBody, null, default);

        public async Task<RepT> CallingAPI<RepT, ReqT>(string ApiName, ReqT requestBody, string? sessionToken, CancellationToken ct = default)
        {
            var api = await FindAsync(ApiName, ct);
            if (api == null) return NotFound<RepT>(ApiName);
            var method = Method(api);
            HttpContent? content = null;
            if (method != HttpMethod.Get)
            {
                var json = requestBody is string s && s.Length == 0 ? "{}" : JsonSerializer.Serialize(requestBody, WriteJson);
                content = new StringContent(json, Encoding.UTF8, JsonContentType(api));
            }
            return await SendAsync<RepT>(api, method, content, sessionToken, ct);
        }

        public async Task<RepT> CallingAPIUpload<RepT>(string ApiName, IEnumerable<ApiUploadFile> files, IDictionary<string, string>? fields = null, CancellationToken ct = default)
        {
            var api = await FindAsync(ApiName, ct);
            if (api == null) return NotFound<RepT>(ApiName);
            var form = new MultipartFormDataContent();
            foreach (var f in files)
            {
                var sc = new StreamContent(f.Content);
                sc.Headers.ContentType = new MediaTypeHeaderValue(string.IsNullOrEmpty(f.ContentType) ? "application/octet-stream" : f.ContentType);
                form.Add(sc, "files", f.FileName);
            }
            if (fields != null)
                foreach (var kv in fields) form.Add(new StringContent(kv.Value ?? ""), kv.Key);
            return await SendAsync<RepT>(api, HttpMethod.Post, form, null, ct);
        }

        public async Task<ApiFileResponse> CallingAPIDownload<ReqT>(string ApiName, ReqT requestBody, CancellationToken ct = default)
        {
            var api = await FindAsync(ApiName, ct);
            if (api == null) return NotFound<ApiFileResponse>(ApiName);
            var method = Method(api);
            HttpContent? content = method == HttpMethod.Get
                ? null
                : new StringContent(JsonSerializer.Serialize(requestBody, WriteJson), Encoding.UTF8, JsonContentType(api));
            return await SendAsync<ApiFileResponse>(api, method, content, null, ct);
        }

        /// <summary>Printable ASCII only — what an HTTP header value may contain.</summary>
        internal static bool IsHeaderSafe(string? value) =>
            !string.IsNullOrEmpty(value) && value.All(c => c >= 0x20 && c < 0x7F);

        /// <summary>1000 "task not found" — or, while the API master list is missing, why it could not be loaded (e.g. 1003 unreachable).</summary>
        private static RepT NotFound<RepT>(string apiName) =>
            (AllApi.ApiDetails == null || AllApi.ApiDetails.Count == 0) && _masterFailure is { } f
                ? Fallback<RepT>(f.Code, f.Message)
                : Fallback<RepT>(CodeNoApi, $"API data for task '{apiName}' not found.");

        // ── HTTP ─────────────────────────────────────────────────────────────

        private static HttpMethod Method(ApiData api) => (api.Api_Http_Method ?? "POST").Trim().ToUpperInvariant() switch
        {
            "GET" => HttpMethod.Get,
            "PUT" => HttpMethod.Put,
            "PATCH" => HttpMethod.Patch,
            "DELETE" => HttpMethod.Delete,
            _ => HttpMethod.Post
        };

        private static string JsonContentType(ApiData api) =>
            api.Api_Content_Type is { } t && t.Contains("json", StringComparison.OrdinalIgnoreCase) ? t.Split(';')[0].Trim() : "application/json";

        private HttpClient Client(ApiData api)
        {
            var useProxy = _useProxy == 1 && !string.IsNullOrWhiteSpace(api.Api_Proxy);
            var key = useProxy ? $"{api.Api_Proxy}:{api.Api_Port}|{api.Api_Username}" : "direct";
            return Clients.GetOrAdd(key, _ =>
            {
                var handler = new SocketsHttpHandler { PooledConnectionLifetime = TimeSpan.FromMinutes(5), UseProxy = useProxy };
                if (useProxy)
                {
                    var proxyUri = api.Api_Proxy!.Contains("://") ? api.Api_Proxy! : "http://" + api.Api_Proxy;
                    var proxy = new WebProxy(api.Api_Port > 0 ? $"{proxyUri.TrimEnd('/')}:{api.Api_Port}" : proxyUri, true);
                    if (!string.IsNullOrEmpty(api.Api_Username) && !string.IsNullOrEmpty(api.Api_Pwd))
                        proxy.Credentials = new NetworkCredential(api.Api_Username, api.Api_Pwd);
                    handler.Proxy = proxy;
                }
                return new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
            });
        }

        private async Task<RepT> SendAsync<RepT>(ApiData api, HttpMethod method, HttpContent? content, string? sessionToken, CancellationToken ct)
        {
            if (!Uri.TryCreate(api.Api_Url, UriKind.Absolute, out var uri))
                return Fallback<RepT>(CodeNoApi, $"API_MASTER {api.Task_Name}: Api_Url '{api.Api_Url}' is not an absolute URL.");
            var timeout = TimeSpan.FromSeconds(api.Api_Timeout > 0 ? api.Api_Timeout : 120);
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(timeout);
            try
            {
                using var req = new HttpRequestMessage(method, uri) { Content = content };
                if (!string.IsNullOrEmpty(api.Api_Authorization))
                {
                    var tok = api.Api_Authorization.Trim();
                    if (tok.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)) tok = tok[7..].Trim();
                    if (!IsHeaderSafe(tok))
                        return Fallback<RepT>(CodeError, $"The API token for '{api.Task_Name}' is not valid: MasterApiSettings:Api_Authorization (Admin) and " +
                                                         "API_MASTER.Api_Authorization must hold My_Encode of the static API token - the same text as " +
                                                         "JwtSettings:SecretKey in POGovApi/appsettings.json.");
                    req.Headers.Authorization = new AuthenticationHeaderValue(string.IsNullOrWhiteSpace(api.Api_Authorization_Type) ? "Bearer" : api.Api_Authorization_Type.Trim(), tok);
                }
                sessionToken ??= _sessions == null ? null : await _sessions.GetSessionTokenAsync();
                if (!string.IsNullOrEmpty(sessionToken)) req.Headers.TryAddWithoutValidation(AppConstants.SessionHeader, sessionToken);
                req.Headers.TryAddWithoutValidation(AppConstants.RestrictedHeader, "1");

                using var res = await Client(api).SendAsync(req, HttpCompletionOption.ResponseHeadersRead, cts.Token);
                var mediaType = res.Content.Headers.ContentType?.MediaType ?? "";
                var isJson = mediaType.Contains("json", StringComparison.OrdinalIgnoreCase);
                var isText = mediaType.StartsWith("text/", StringComparison.OrdinalIgnoreCase) && !mediaType.Equals("text/csv", StringComparison.OrdinalIgnoreCase)
                             || mediaType.Equals("application/xml", StringComparison.OrdinalIgnoreCase)
                             || mediaType.EndsWith("+xml", StringComparison.OrdinalIgnoreCase);

                if (res.IsSuccessStatusCode && !isJson && !isText && mediaType.Length > 0 && res.StatusCode != HttpStatusCode.NoContent)
                {
                    var bytes = await res.Content.ReadAsByteArrayAsync(cts.Token);
                    if (typeof(RepT) == typeof(ApiFileResponse)) return (RepT)(object)FileFrom(res, bytes, mediaType);
                    if (typeof(RepT) == typeof(string)) return (RepT)(object)Convert.ToBase64String(bytes);   // QPS: file answers as base64
                    return Fallback<RepT>(CodeError, "The server returned a file where data was expected.");
                }

                var text = await res.Content.ReadAsStringAsync(cts.Token);
                var trimmed = text.TrimStart();
                if (!res.IsSuccessStatusCode) return ErrorFrom<RepT>((int)res.StatusCode, trimmed, api);
                if (trimmed.Length == 0) return Fallback<RepT>(CodeEmpty, "The server returned an empty answer.");
                if (trimmed.StartsWith('<')) return Fallback<RepT>(CodeHtml, "The server returned a web page instead of data (check the API_MASTER URL).");
                try
                {
                    return JsonSerializer.Deserialize<RepT>(text, ReadJson)!;
                }
                catch (JsonException e)
                {
                    _log?.LogWarning(e, "{Task} ({Url}) returned unexpected content", api.Task_Name, api.Api_Url);
                    return Fallback<RepT>(CodeError, "The server returned an unexpected response.");
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (OperationCanceledException)
            {
                _log?.LogWarning("{Task} ({Url}) did not answer within {Seconds} s", api.Task_Name, api.Api_Url, timeout.TotalSeconds);
                return Fallback<RepT>(CodeUnreachable, $"The server did not answer within {timeout.TotalSeconds:0} seconds. Please try again.");
            }
            catch (HttpRequestException e)
            {
                var at = uri.GetLeftPart(UriPartial.Authority) + "/";
                _log?.LogError("POGovApi is not reachable at {Url} ({Task}: {Error}). Start POGovApi or correct the URL in API_MASTER / MasterApiSettings:Api_Url.",
                    at, api.Task_Name, e.Message);
                return Fallback<RepT>(CodeUnreachable, $"Cannot reach the server ({at}). Check that POGovApi is running and the API master URL points to it.");
            }
            catch (Exception e)
            {
                _log?.LogError(e, "{Task} ({Url}) failed", api.Task_Name, api.Api_Url);
                return Fallback<RepT>(CodeError, e.Message);
            }
            finally
            {
                content?.Dispose();
            }
        }

        private static ApiFileResponse FileFrom(HttpResponseMessage res, byte[] bytes, string mediaType)
        {
            var cd = res.Content.Headers.ContentDisposition;
            var name = (cd?.FileNameStar ?? cd?.FileName ?? "download").Trim('"');
            var file = new ApiFileResponse { FileName = name, ContentType = mediaType, FileBytes = bytes };
            foreach (var h in res.Headers.Concat(res.Content.Headers))
                if (h.Key.StartsWith("X-", StringComparison.OrdinalIgnoreCase))
                    file.Headers[h.Key] = Uri.UnescapeDataString(string.Join(",", h.Value));
            return file;
        }

        /// <summary>A non-2xx answer: the API's JSON body (responseCode forced to the HTTP status when 0), else a text by status.</summary>
        private RepT ErrorFrom<RepT>(int status, string text, ApiData api)
        {
            if (text.StartsWith('{'))
            {
                try
                {
                    var body = JsonSerializer.Deserialize<RepT>(text, ReadJson);
                    if (body is BaseResponse b)
                    {
                        if (b.responseCode == 0) b.responseCode = status;
                        if (string.IsNullOrEmpty(b.responseMessage) || b.responseMessage == "SUCCESS") b.responseMessage = StatusText(status);
                        return body;
                    }
                }
                catch (JsonException)
                {
                    // The Data payload of the error does not fit RepT — keep code and message only.
                }
                try
                {
                    using var doc = JsonDocument.Parse(text);
                    string? msg = null;
                    foreach (var p in doc.RootElement.EnumerateObject())
                        if (string.Equals(p.Name, "responseMessage", StringComparison.OrdinalIgnoreCase) && p.Value.ValueKind == JsonValueKind.String)
                            msg = p.Value.GetString();
                    if (!string.IsNullOrEmpty(msg)) return Fallback<RepT>(status, msg);
                }
                catch (JsonException) { }
            }
            if (status >= 500) _log?.LogWarning("{Task} ({Url}) answered HTTP {Status}", api.Task_Name, api.Api_Url, status);
            return Fallback<RepT>(status, StatusText(status));
        }

        private static string StatusText(int status) => status switch
        {
            401 => "Please log in",
            403 => "Not allowed",
            404 => "Not found",
            503 => "The database is not reachable right now - please try again in a minute.",
            _ => "Server error (" + status + ")"
        };

        /// <summary>QPS error JSON fallback: {"responseCode":code,"responseMessage":message} as RepT.</summary>
        public static RepT Fallback<RepT>(int code, string message)
        {
            if (typeof(BaseResponse).IsAssignableFrom(typeof(RepT)) && !typeof(RepT).IsAbstract)
            {
                var r = (BaseResponse)Activator.CreateInstance(typeof(RepT))!;
                r.responseCode = code;
                r.responseMessage = message;
                return (RepT)(object)r;
            }
            try
            {
                return JsonSerializer.Deserialize<RepT>(JsonSerializer.Serialize(new { responseCode = code, responseMessage = message }), ReadJson)!;
            }
            catch (Exception)
            {
                return default!;
            }
        }
    }
}
