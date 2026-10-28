using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Application.Interfaces.V1;
using Domain.Entities.Common;
using Infrastructure.Middleware;
using Microsoft.Extensions.Logging;
using MyNewEncDec;

namespace Infrastructure.Services.V1
{
    /// <summary>
    /// The HRMS employee API (QPS EmployeeApiVerifier / UserDirectoryService, moved behind POGovApi). URL, method, time-out,
    /// proxy and authorization come from the API_MASTER rows <see cref="HrmsTasks.Login"/> and <see cref="HrmsTasks.EmployeeByCode"/>
    /// of this environment (Api_Scope = SERVER: the portal never receives them). The password is sent to HRMS only and is
    /// never logged.
    /// </summary>
    public sealed class HrmsApiClient : IHrmsClient
    {
        public const string HttpClientName = "pogov-hrms";
        /// <summary>HRMS: "Maximum login attempts limit exceeded, account is locked for 28 minutes".</summary>
        public const int HrmsLockedCode = 104;
        private static readonly TimeSpan RowCacheTime = TimeSpan.FromMinutes(5);
        private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true, NumberHandling = JsonNumberHandling.AllowReadingFromString };

        private readonly IAPI _api;
        private readonly IHttpClientFactory _http;
        private readonly ILogger<HrmsApiClient> _log;
        private readonly ConcurrentDictionary<string, (ApiData Row, DateTime Until)> _rows = new();
        private readonly ConcurrentDictionary<string, HttpClient> _proxied = new();

        public HrmsApiClient(IAPI api, IHttpClientFactory http, ILogger<HrmsApiClient> log)
        {
            _api = api;
            _http = http;
            _log = log;
        }

        // Same body as QPS HrpLoginReq (captchaText / currentTab are not used by HRMS for this call).
        private sealed record LoginBody(string loginID, string loginPassword, string? captchaText = null, string? currentTab = null, string isFirstRender = "Y");
        private sealed record CodeBody(string loginID);

        private sealed class HrmsAnswer
        {
            [JsonPropertyName("responseCode")] public int ResponseCode { get; set; }
            [JsonPropertyName("responseMessage")] public string? ResponseMessage { get; set; }
            [JsonPropertyName("employeeList")] public List<HrmsEmployee>? EmployeeList { get; set; }
            public HrmsEmployee? First => EmployeeList is { Count: > 0 } l ? l[0] : null;
            public bool Success => ResponseCode == 0 && string.Equals(ResponseMessage?.Trim(), "success", StringComparison.OrdinalIgnoreCase) && First != null;
        }

        public async Task<HrmsResult> VerifyPasswordAsync(string empCode, string password, CancellationToken ct = default)
        {
            var (answer, error) = await CallAsync(HrmsTasks.Login, new LoginBody(empCode, password), ct);
            if (answer == null) return HrmsResult.Unavailable(error!);
            if (answer.Success) return new HrmsResult(HrmsStatus.Ok, answer.First);
            if (answer.ResponseCode == HrmsLockedCode) return new HrmsResult(HrmsStatus.LockedInHrms, null, answer.ResponseMessage);
            _log.LogInformation("HRMS refused the password of employee {EmpCode}: {Code} {Message}", empCode, answer.ResponseCode, answer.ResponseMessage);
            return new HrmsResult(HrmsStatus.Invalid, null, answer.ResponseMessage);
        }

        public async Task<HrmsResult> GetEmployeeAsync(string empCode, CancellationToken ct = default)
        {
            var (answer, error) = await CallAsync(HrmsTasks.EmployeeByCode, new CodeBody(empCode), ct);
            if (answer == null) return HrmsResult.Unavailable(error!);
            return answer.Success ? new HrmsResult(HrmsStatus.Ok, answer.First) : new HrmsResult(HrmsStatus.NotFound, null, answer.ResponseMessage);
        }

        private async Task<(HrmsAnswer? Answer, string? Error)> CallAsync<TBody>(string task, TBody body, CancellationToken ct)
        {
            var row = await RowAsync(task, ct);
            if (row == null || string.IsNullOrWhiteSpace(row.Api_Url))
                return (null, $"HRMS API {task} is not configured for environment {_api.Environment} (API_MASTER / API_BASE_URL HRMS_API)");
            try
            {
                var method = new HttpMethod(string.IsNullOrWhiteSpace(row.Api_Http_Method) ? "POST" : row.Api_Http_Method.Trim().ToUpperInvariant());
                using var req = new HttpRequestMessage(method, row.Api_Url)
                {
                    Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8,
                                                string.IsNullOrWhiteSpace(row.Api_Content_Type) ? "application/json" : row.Api_Content_Type.Trim())
                };
                Authorize(req, row);
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                cts.CancelAfter(TimeSpan.FromSeconds(row.Api_Timeout > 0 ? row.Api_Timeout : 30));
                using var resp = await Client(row).SendAsync(req, cts.Token);
                var text = await resp.Content.ReadAsStringAsync(cts.Token);
                if (!resp.IsSuccessStatusCode)
                {
                    _log.LogWarning("HRMS API {Task} answered HTTP {Status}", task, (int)resp.StatusCode);
                    return (null, $"HRMS answered HTTP {(int)resp.StatusCode}");
                }
                var answer = JsonSerializer.Deserialize<HrmsAnswer>(text, Json);
                if (answer == null) return (null, "HRMS sent an empty answer");
                return (answer, null);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (OperationCanceledException)
            {
                _log.LogWarning("HRMS API {Task} timed out", task);
                return (null, "HRMS did not answer in time");
            }
            catch (Exception e) when (e is HttpRequestException or JsonException or InvalidOperationException or UriFormatException)
            {
                _log.LogWarning(e, "HRMS API {Task} failed", task);
                return (null, "HRMS could not be reached: " + e.Message);
            }
        }

        private async Task<ApiData?> RowAsync(string task, CancellationToken ct)
        {
            if (_rows.TryGetValue(task, out var c) && c.Until > DateTime.UtcNow) return c.Row;
            var res = await _api.GetServerApiData(task, ct);
            var row = res.responseCode == 0 ? res.ApiData?.FirstOrDefault(r => r.Task_Name == task) : null;
            if (row == null)
            {
                _log.LogError("HRMS API {Task}: {Message}", task, res.responseMessage);
                return null;
            }
            _rows[task] = (row, DateTime.UtcNow + RowCacheTime);
            return row;
        }

        /// <summary>API_AUTHORIZATION / API_MASTER token (My_Encode-d, as every token in the API master), else Basic with Api_Username / Api_Pwd.</summary>
        private static void Authorize(HttpRequestMessage req, ApiData row)
        {
            if (!string.IsNullOrWhiteSpace(row.Api_Authorization))
            {
                var token = Decode(row.Api_Authorization);
                if (token.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)) token = token[7..].Trim();
                req.Headers.Authorization = new AuthenticationHeaderValue(string.IsNullOrWhiteSpace(row.Api_Authorization_Type) ? "Bearer" : row.Api_Authorization_Type.Trim(), token);
            }
            else if (!string.IsNullOrWhiteSpace(row.Api_Username) && !string.IsNullOrWhiteSpace(row.Api_Pwd))
            {
                var basic = Convert.ToBase64String(Encoding.UTF8.GetBytes(row.Api_Username.Trim() + ":" + Decode(row.Api_Pwd)));
                req.Headers.Authorization = new AuthenticationHeaderValue("Basic", basic);
            }
        }

        private static string Decode(string encoded)
        {
            try { return StaticTokenMiddleware.DecodeToken(encoded); }
            catch (Exception) { return encoded.Trim(); }
        }

        /// <summary>The row's proxy (Api_Proxy:Api_Port), else a plain client.</summary>
        private HttpClient Client(ApiData row)
        {
            if (string.IsNullOrWhiteSpace(row.Api_Proxy)) return _http.CreateClient(HttpClientName);
            var address = row.Api_Port > 0 ? $"{row.Api_Proxy.Trim()}:{row.Api_Port}" : row.Api_Proxy.Trim();
            return _proxied.GetOrAdd(address, a => new HttpClient(new HttpClientHandler { Proxy = new WebProxy(a, true), UseProxy = true })
            {
                Timeout = Timeout.InfiniteTimeSpan   // the per-call time-out of the row applies
            });
        }
    }
}
