using System.Net;
using System.Text;
using System.Text.Json;
using Application.Interfaces.V1;
using Domain.Entities.Common;
using Domain.Entities.Response;
using Infrastructure.Services.V1;
using Microsoft.Extensions.Logging.Abstractions;

namespace POGovernance.Tests.Api
{
    /// <summary>The HRMS employee API as POGovApi calls it (QPS HrpLoginReq / HrpLoginRes shapes).</summary>
    public class HrmsApiClientTests
    {
        private const string EncodedToken = "7VmtFgfO7g5YDVUW175UDqe7FIOUFq8cxpTI17P0fJiSHbePHN";   // My_Encode("PoGov-Static-Dev-Token-2026-Change-Me")

        private sealed class FakeApi : IAPI
        {
            public readonly Dictionary<string, ApiData> Rows = new();
            public readonly List<string> Asked = new();
            public string Environment => "Staging";
            public Task<ApiMasterRes> GetAllApi(CancellationToken ct = default) => throw new NotSupportedException();
            public Task<ApiMasterRes> GetSpecificApiData(string? taskName, string? param, CancellationToken ct = default) => throw new NotSupportedException();
            public Task<string?> GetBaseUrlAsync(string baseUrlKey, CancellationToken ct = default) => throw new NotSupportedException();
            public Task<ApiMasterRes> GetServerApiData(string taskName, CancellationToken ct = default)
            {
                Asked.Add(taskName);
                return Task.FromResult(Rows.TryGetValue(taskName, out var r)
                    ? new ApiMasterRes { ApiData = new List<ApiData> { r } }
                    : new ApiMasterRes { responseCode = 404, responseMessage = "No active API_MASTER row for task " + taskName });
            }
        }

        private sealed class Handler : HttpMessageHandler
        {
            public readonly List<(HttpRequestMessage Req, string Body)> Seen = new();
            public Func<string, (HttpStatusCode, string)> Answer = _ => (HttpStatusCode.OK, "{}");
            protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
            {
                var body = request.Content == null ? "" : await request.Content.ReadAsStringAsync(ct);
                Seen.Add((request, body));
                var (status, text) = Answer(body);
                return new HttpResponseMessage(status) { Content = new StringContent(text, Encoding.UTF8, "application/json") };
            }
        }

        private sealed class Factory : IHttpClientFactory
        {
            private readonly Handler _h;
            public Factory(Handler h) => _h = h;
            public HttpClient CreateClient(string name) => new(_h, false);
        }

        private static (HrmsApiClient Client, FakeApi Api, Handler Http) Make(bool configured = true)
        {
            var api = new FakeApi();
            if (configured)
            {
                api.Rows[HrmsTasks.Login] = new ApiData { Task_Name = HrmsTasks.Login, Api_Url = "https://hrms.test/api/EmpLogin", Api_Http_Method = "POST",
                                                          Api_Content_Type = "application/json", Api_Timeout = 10, Api_Authorization = EncodedToken, Api_Authorization_Type = "Bearer" };
                api.Rows[HrmsTasks.EmployeeByCode] = new ApiData { Task_Name = HrmsTasks.EmployeeByCode, Api_Url = "https://hrms.test/api/EmpByCode", Api_Http_Method = "POST",
                                                                   Api_Content_Type = "application/json", Api_Timeout = 10 };
            }
            var http = new Handler();
            return (new HrmsApiClient(api, new Factory(http), NullLogger<HrmsApiClient>.Instance), api, http);
        }

        private const string Ok = """{"responseCode":0,"responseMessage":"Success","employeeList":[{"empCode":"102345","empName":"Ravi Kumar","empMailID":"ravi.kumar@x.com","empDeptName":"Merchandising","isResign":false,"empGrade":"5","empStatus":true}]}""";

        [Fact]
        public async Task Login_sends_the_QPS_body_with_the_decoded_token_and_reads_the_employee()
        {
            var (c, _, http) = Make();
            http.Answer = _ => (HttpStatusCode.OK, Ok);
            var r = await c.VerifyPasswordAsync("102345", "Secret#1");
            Assert.Equal(HrmsStatus.Ok, r.Status);
            Assert.Equal("Ravi Kumar", r.Employee!.EmpName);
            Assert.Equal(5, r.Employee.EmpGrade);
            Assert.True(r.Employee.IsWorking);

            var (req, body) = http.Seen.Single();
            Assert.Equal("https://hrms.test/api/EmpLogin", req.RequestUri!.ToString());
            Assert.Equal(HttpMethod.Post, req.Method);
            Assert.Equal("Bearer", req.Headers.Authorization!.Scheme);
            Assert.Equal("PoGov-Static-Dev-Token-2026-Change-Me", req.Headers.Authorization.Parameter);
            using var doc = JsonDocument.Parse(body);
            Assert.Equal("102345", doc.RootElement.GetProperty("loginID").GetString());
            Assert.Equal("Secret#1", doc.RootElement.GetProperty("loginPassword").GetString());
            Assert.Equal("Y", doc.RootElement.GetProperty("isFirstRender").GetString());
        }

        [Fact]
        public async Task Login_maps_wrong_password_HRMS_lockout_and_failures()
        {
            var (c, _, http) = Make();
            http.Answer = _ => (HttpStatusCode.OK, """{"responseCode":101,"responseMessage":"Invalid credentials","employeeList":[]}""");
            Assert.Equal(HrmsStatus.Invalid, (await c.VerifyPasswordAsync("102345", "bad")).Status);

            http.Answer = _ => (HttpStatusCode.OK, """{"responseCode":104,"responseMessage":"Maximum login attempts limit exceeded","employeeList":[]}""");
            Assert.Equal(HrmsStatus.LockedInHrms, (await c.VerifyPasswordAsync("102345", "bad")).Status);

            http.Answer = _ => (HttpStatusCode.InternalServerError, "oops");
            Assert.Equal(HrmsStatus.Unavailable, (await c.VerifyPasswordAsync("102345", "x")).Status);

            http.Answer = _ => (HttpStatusCode.OK, "<html>maintenance</html>");
            Assert.Equal(HrmsStatus.Unavailable, (await c.VerifyPasswordAsync("102345", "x")).Status);
        }

        [Fact]
        public async Task Lookup_by_code_found_and_not_found()
        {
            var (c, api, http) = Make();
            http.Answer = body => body.Contains("102345") ? (HttpStatusCode.OK, Ok) : (HttpStatusCode.OK, """{"responseCode":1,"responseMessage":"No data found","employeeList":[]}""");
            var r = await c.GetEmployeeAsync("102345");
            Assert.Equal(HrmsStatus.Ok, r.Status);
            Assert.Equal("ravi.kumar@x.com", r.Employee!.EmpMailId);
            Assert.Null(http.Seen[0].Req.Headers.Authorization);   // no token configured for this row
            Assert.Equal(HrmsStatus.NotFound, (await c.GetEmployeeAsync("999")).Status);
            Assert.Equal(new[] { HrmsTasks.EmployeeByCode }, api.Asked);   // the row is cached
        }

        [Fact]
        public async Task Not_configured_is_unavailable_and_nothing_is_sent()
        {
            var (c, _, http) = Make(configured: false);
            var r = await c.VerifyPasswordAsync("102345", "x");
            Assert.Equal(HrmsStatus.Unavailable, r.Status);
            Assert.Contains("not configured", r.Message);
            Assert.Empty(http.Seen);
        }
    }
}
