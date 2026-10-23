using Domain.Entities.Common;
using Infrastructure.Middleware;
using Infrastructure.Services.V1;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;

namespace POGovernance.Tests.Api
{
    /// <summary>API token per path and environment: ApiTokenRegistry (grace for replaced tokens), the API_AUTHORIZATION overrides, StaticTokenMiddleware.</summary>
    public class ApiTokenRegistryTests
    {
        private const string Static = "static-token";
        private DateTime _now = new(2026, 10, 23, 10, 0, 0, DateTimeKind.Utc);
        private ApiTokenRegistry Registry() => new(Static, () => _now);

        [Fact]
        public void Without_overrides_every_path_takes_the_static_token()
        {
            var r = Registry();
            Assert.True(r.IsValid("/api/v1/AuthLogin", Static));
            Assert.False(r.IsValid("/api/v1/AuthLogin", "other"));
        }

        [Fact]
        public void An_override_replaces_the_static_token_for_its_path_after_the_grace_period()
        {
            var r = Registry();
            r.Update(new Dictionary<string, string> { ["/api/v1/Tool1Validate"] = "tool1-prod" });
            Assert.True(r.IsValid("/api/v1/tool1validate/", "tool1-prod"));   // path case / final slash do not matter
            Assert.True(r.IsValid("/api/v1/Tool1Validate", Static));          // grace: the portal may not have reloaded yet
            Assert.True(r.IsValid("/api/v1/AuthLogin", Static));
            Assert.False(r.IsValid("/api/v1/AuthLogin", "tool1-prod"));
            _now += ApiTokenRegistry.Grace + TimeSpan.FromSeconds(1);
            r.Update(new Dictionary<string, string> { ["/api/v1/Tool1Validate"] = "tool1-prod" });
            Assert.False(r.IsValid("/api/v1/Tool1Validate", Static));
            Assert.True(r.IsValid("/api/v1/Tool1Validate", "tool1-prod"));
        }

        [Fact]
        public void A_rotated_or_removed_token_stays_valid_for_the_grace_period()
        {
            var r = Registry();
            r.Update(new Dictionary<string, string> { ["/api/v1/X"] = "v1" });
            _now += ApiTokenRegistry.Grace + TimeSpan.FromMinutes(1);
            r.Update(new Dictionary<string, string> { ["/api/v1/X"] = "v2" });
            Assert.True(r.IsValid("/api/v1/X", "v1"));
            Assert.True(r.IsValid("/api/v1/X", "v2"));
            Assert.False(r.IsValid("/api/v1/X", Static));
            _now += TimeSpan.FromMinutes(1);
            r.Update(new Dictionary<string, string>());                       // override deleted: back to the static token
            Assert.True(r.IsValid("/api/v1/X", Static));
            Assert.True(r.IsValid("/api/v1/X", "v2"));
            _now += ApiTokenRegistry.Grace + TimeSpan.FromMinutes(1);
            r.Update(new Dictionary<string, string>());
            Assert.False(r.IsValid("/api/v1/X", "v2"));
            Assert.Equal(0, r.Count);
        }

        [Fact]
        public void Only_valid_API_AUTHORIZATION_tokens_become_overrides()
        {
            var rows = new[]
            {
                new ApiData { Task_Name = "PO_A", Api_Url = "http://h:5080/api/v1/A", Auth_Source = "TASK", Api_Authorization = StaticTokenMiddleware.EncodeToken("a-token") },
                new ApiData { Task_Name = "PO_B", Api_Url = "http://h:5080/api/v1/B", Auth_Source = "ENVIRONMENT", Api_Authorization = StaticTokenMiddleware.EncodeToken("env-token") },
                new ApiData { Task_Name = "PO_C", Api_Url = "http://h:5080/api/v1/C", Auth_Source = "MASTER", Api_Authorization = StaticTokenMiddleware.EncodeToken("x") },
                new ApiData { Task_Name = "PO_GET_ALL_API", Api_Url = "http://h:5080/api/v1/GetAllApi", Auth_Source = "TASK", Api_Authorization = StaticTokenMiddleware.EncodeToken("x") },
                new ApiData { Task_Name = "PO_BAD", Api_Url = "http://h:5080/api/v1/Bad", Auth_Source = "TASK", Api_Authorization = "plain token, not My_Encode" },
            };
            var (map, skipped) = ApiTokenRefreshService.Overrides(rows);
            Assert.Equal(new Dictionary<string, string> { ["/api/v1/A"] = "a-token", ["/api/v1/B"] = "env-token" }, map);
            Assert.Equal(new[] { "PO_BAD" }, skipped);
        }

        [Fact]
        public async Task The_middleware_checks_the_token_of_the_requested_path()
        {
            var cfg = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["JwtSettings:SecretKey"] = StaticTokenMiddleware.EncodeToken(Static) }).Build();
            var r = Registry();
            r.Update(new Dictionary<string, string> { ["/api/v1/A"] = "a-token" });
            _now += ApiTokenRegistry.Grace + TimeSpan.FromSeconds(1);
            r.Update(new Dictionary<string, string> { ["/api/v1/A"] = "a-token" });
            async Task<int> Call(string path, string token)
            {
                var passed = false;
                var mw = new StaticTokenMiddleware(_ => { passed = true; return Task.CompletedTask; }, cfg, r);
                var ctx = new DefaultHttpContext();
                ctx.Response.Body = new MemoryStream();
                ctx.Request.Path = path;
                ctx.Request.Headers.Authorization = "Bearer " + token;
                await mw.InvokeAsync(ctx);
                return passed ? 200 : ctx.Response.StatusCode;
            }
            Assert.Equal(200, await Call("/api/v1/A", "a-token"));
            Assert.Equal(401, await Call("/api/v1/A", Static));
            Assert.Equal(200, await Call("/api/v1/B", Static));
            Assert.Equal(401, await Call("/api/v1/B", "a-token"));
        }
    }
}
