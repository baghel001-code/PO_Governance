using System.Text;
using Application.Interfaces.V1;
using Domain.Constants;
using Domain.Entities.Base;
using Domain.Entities.Common;
using Domain.Entities.Response;
using Infrastructure.Middleware;
using Infrastructure.Services.V1;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace POGovernance.Tests.Api
{
    /// <summary>
    /// The portal's HTTP client (QPS APICallService) against a small Kestrel app behind the real StaticTokenMiddleware:
    /// task name → API_MASTER row, static bearer + X-Session-Token, error bodies, fallback codes 1000-1004, files,
    /// and the lazy reload of the API master list. (AllApi / ApiMasterData are static, so everything is in one class.)
    /// </summary>
    public sealed class ApiCallServiceTests : IAsyncLifetime
    {
        private const string Token = "PoGov-Static-Dev-Token-2026-Change-Me";
        private WebApplication _app = null!;
        private string _base = "";
        private int _masterCalls;

        private sealed class Accessor : ISessionTokenAccessor
        {
            public string? Value;
            public Task<string?> GetSessionTokenAsync() => Task.FromResult(Value);
        }

        private sealed class Res : BaseResponse
        {
            public string? Auth { get; set; }
            public string? Session { get; set; }
            public string? Body { get; set; }
        }

        public async Task InitializeAsync()
        {
            var b = WebApplication.CreateBuilder();
            b.Logging.ClearProviders();
            b.WebHost.UseUrls("http://127.0.0.1:0");
            b.Configuration.AddInMemoryCollection(new Dictionary<string, string?> { ["JwtSettings:SecretKey"] = StaticTokenMiddleware.EncodeToken(Token) });
            _app = b.Build();
            _app.UseMiddleware<StaticTokenMiddleware>();
            _app.MapGet("/api/v1/GetAllApi", () =>
            {
                Interlocked.Increment(ref _masterCalls);
                return Results.Json(new ApiMasterRes { ApiData = Rows(_base) }, new System.Text.Json.JsonSerializerOptions());
            });
            _app.MapPost("/api/v1/Echo", async (HttpContext c) =>
            {
                var body = await new StreamReader(c.Request.Body).ReadToEndAsync();
                return Results.Json(new Res
                {
                    Auth = c.Request.Headers.Authorization.ToString(), Session = c.Request.Headers[AppConstants.SessionHeader].ToString(), Body = body
                });
            });
            _app.MapPost("/api/v1/Denied", () => Results.Json(ApiDataResponse<object>.Fail(403, "Planner or admin only", new { mustChange = true }), statusCode: 403));
            _app.MapPost("/api/v1/Html", () => Results.Content("<html>proxy error</html>", "text/html"));
            _app.MapPost("/api/v1/Empty", () => Results.Content("", "application/json"));
            _app.MapPost("/api/v1/File", (HttpContext c) =>
            {
                c.Response.Headers["X-Message"] = Uri.EscapeDataString("Exported 3 rows ✓");
                return Results.File(Encoding.UTF8.GetBytes("xlsx-bytes"), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "Rows.xlsx");
            });
            _app.MapPost("/api/v1/Upload", async (HttpContext c) =>
            {
                var form = await c.Request.ReadFormAsync();
                var f = form.Files[0];
                return Results.Json(new Res { Body = $"{form["Key"]}|{f.Name}|{f.FileName}|{f.Length}", Session = c.Request.Headers[AppConstants.SessionHeader].ToString() });
            });
            _app.MapGet("/api/v1/Health", () => Results.Json(new Res { Body = "ok" }));
            await _app.StartAsync();
            _base = _app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First().TrimEnd('/') + "/";
            ApiMasterData.apiData = new ApiData
            {
                Task_Name = "All", Api_Url = _base + "api/v1/GetAllApi", Api_Http_Method = "GET", Api_Timeout = 10, Api_Authorization = Token
            };
            AllApi.ApiDetails = null;
            APICallService.MasterRetryInterval = TimeSpan.Zero;
        }

        public async Task DisposeAsync()
        {
            AllApi.ApiDetails = null;
            ApiMasterData.apiData = null;
            APICallService.MasterRetryInterval = TimeSpan.FromSeconds(10);
            await _app.DisposeAsync();
        }

        private static List<ApiData> Rows(string b)
        {
            ApiData R(string task, string action, string method = "POST", string? auth = null) => new()
            {
                Task_Name = task, Api_Name = action, Api_Url = b + "api/v1/" + action, Api_Http_Method = method, Api_Content_Type = "application/json",
                Api_Timeout = 10, Api_Authorization_Type = "Bearer", Api_Authorization = auth ?? StaticTokenMiddleware.EncodeToken(Token)
            };
            return new()
            {
                R("T_ECHO", "Echo"), R("T_DENIED", "Denied"), R("T_HTML", "Html"), R("T_EMPTY", "Empty"), R("T_FILE", "File"), R("T_UPLOAD", "Upload"),
                R("T_HEALTH", "Health", "GET"), R("T_BADTOKEN", "Echo", auth: StaticTokenMiddleware.EncodeToken("wrong")),
                new ApiData { Task_Name = "T_DOWN", Api_Url = "http://127.0.0.1:1/api/v1/Echo", Api_Http_Method = "POST", Api_Timeout = 5 }
            };
        }

        [Fact]
        public async Task Calls_by_task_name_with_static_token_and_session_header()
        {
            var acc = new Accessor { Value = "user-session-1" };
            var api = new APICallService(0, acc, null);

            // the master list is loaded lazily on the first call (the API was "down" at start-up) and the row tokens are decoded
            var r = await api.CallingAPI<Res, object>("T_ECHO", new { Key = "f1", Version = "v3" });
            Assert.Equal(0, r.responseCode);
            Assert.Equal(1, _masterCalls);
            Assert.Equal("Bearer " + Token, r.Auth);
            Assert.Equal("user-session-1", r.Session);
            Assert.Equal("{\"Key\":\"f1\",\"Version\":\"v3\"}", r.Body);   // property names as declared (QPS)

            // explicit session (sign-in / revalidation) wins; "" = none
            Assert.Equal("explicit", (await api.CallingAPI<Res, object>("T_ECHO", new { }, "explicit")).Session);
            Assert.Equal("", (await api.CallingAPI<Res, object>("T_ECHO", new { }, "")).Session);
            Assert.Equal(1, _masterCalls);

            // the static token middleware refuses a wrong token, with the JSON envelope
            var bad = await api.CallingAPI<Res, object>("T_BADTOKEN", new { });
            Assert.Equal(401, bad.responseCode);
            Assert.Equal(StaticTokenMiddleware.Unauthorized, bad.responseMessage);

            // error body: code = HTTP status, message from the API, payload that does not fit RepT tolerated
            var denied = await api.CallingAPI<ApiDataResponse<List<string>>, object>("T_DENIED", new { });
            Assert.Equal(403, denied.responseCode);
            Assert.Equal("Planner or admin only", denied.responseMessage);
            Assert.False(denied.IsSuccess);

            // QPS fallback codes
            Assert.Equal(APICallService.CodeNoApi, (await api.CallingAPI<Res, object>("T_NOPE", new { })).responseCode);
            Assert.Equal(APICallService.CodeHtml, (await api.CallingAPI<Res, object>("T_HTML", new { })).responseCode);
            Assert.Equal(APICallService.CodeEmpty, (await api.CallingAPI<Res, object>("T_EMPTY", new { })).responseCode);
            var down = await api.CallingAPI<Res, object>("T_DOWN", new { });
            Assert.Equal(APICallService.CodeUnreachable, down.responseCode);
            Assert.StartsWith("Cannot reach the server (http://127.0.0.1:1/)", down.responseMessage);

            // GET row, no body; Health needs no token
            Assert.Equal("ok", (await api.CallingAPI<Res, string>("T_HEALTH", "")).Body);

            // files: bytes, name, type, X-* headers; as base64 for RepT = string (QPS)
            var file = await api.CallingAPIDownload("T_FILE", new { AsOfPeriod = "01.2026" });
            Assert.True(file.IsSuccess, file.responseCode + " " + file.responseMessage);
            Assert.Equal("Rows.xlsx", file.FileName);
            Assert.Equal("xlsx-bytes", Encoding.UTF8.GetString(file.FileBytes));
            Assert.Equal("Exported 3 rows ✓", file.Header("X-Message"));
            Assert.Equal(Convert.ToBase64String(Encoding.UTF8.GetBytes("xlsx-bytes")), await api.CallingAPI<string, object>("T_FILE", new { }));
            var fileErr = await api.CallingAPIDownload("T_DENIED", new { });
            Assert.Equal(403, fileErr.responseCode);

            // multipart upload with fields
            var up = await api.CallingAPIUpload<Res>("T_UPLOAD", new[] { new ApiUploadFile("f1.xlsx", new MemoryStream(new byte[] { 1, 2, 3 })) },
                new Dictionary<string, string> { ["Key"] = "f1" });
            Assert.Equal("f1|files|f1.xlsx|3", up.Body);
            Assert.Equal("user-session-1", up.Session);
        }

        [Fact]
        public async Task While_the_API_master_cannot_be_loaded_calls_say_why()
        {
            ApiMasterData.apiData!.Api_Url = "http://127.0.0.1:1/api/v1/GetAllApi";
            var api = new APICallService(0, new Accessor(), null);
            Assert.False(await api.LoadApiMasterAsync());
            var r = await api.CallingAPI<Res, object>("T_ECHO", new { });
            Assert.Equal(APICallService.CodeUnreachable, r.responseCode);
            Assert.StartsWith("Cannot reach the server (http://127.0.0.1:1/)", r.responseMessage);

            ApiMasterData.apiData!.Api_Url = _base + "api/v1/GetAllApi";   // the API is up again: loaded on the next call
            Assert.Equal(0, (await api.CallingAPI<Res, object>("T_ECHO", new { })).responseCode);
            Assert.Equal(APICallService.CodeNoApi, (await api.CallingAPI<Res, object>("T_NOPE", new { })).responseCode);
        }
    }
}
