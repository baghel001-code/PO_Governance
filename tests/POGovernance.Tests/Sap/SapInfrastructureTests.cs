using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using Domain.Entities.AppSettings;
using Domain.Exceptions;
using Infrastructure.Authentication;
using Infrastructure.Middleware;
using Infrastructure.Services.V1;
using Infrastructure.Utilitys;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;

namespace POGovernance.Tests.Sap
{
    /// <summary>IP allow-list (CIDR, X-Forwarded-For only from trusted proxies), Basic header parsing, the SAP front-door middleware.</summary>
    public class SapInfrastructureTests
    {
        [Theory]
        [InlineData("10.10.20.15", "10.10.20.15", true)]
        [InlineData("10.10.20.15", "10.10.20.16", false)]
        [InlineData("10.10.21.0/24", "10.10.21.200", true)]
        [InlineData("10.10.21.0/24", "10.10.22.1", false)]
        [InlineData("10.0.0.0/8", "10.255.1.2", true)]
        [InlineData("10.10.21.77/24", "10.10.21.1", true)]              // host bits ignored
        [InlineData("0.0.0.0/0", "192.168.1.1", true)]
        [InlineData("10.10.20.15", "::ffff:10.10.20.15", true)]         // IPv4-mapped IPv6 (Kestrel dual-stack)
        [InlineData("::1", "::1", true)]
        [InlineData("fd00::/8", "fd12:3456::1", true)]
        [InlineData("fd00::/8", "fe80::1", false)]
        [InlineData("10.10.20.15", "not-an-ip", false)]
        public void Allow_list_with_CIDR(string entry, string ip, bool allowed) => Assert.Equal(allowed, new IpAllowList(new[] { entry }).Contains(ip));

        [Theory]
        [InlineData("10.10.20.300")]
        [InlineData("10.10.20.0/33")]
        [InlineData("abc")]
        [InlineData("10.0.0.0/8/9")]
        public void Invalid_entries_are_refused_at_start_up(string entry) => Assert.Throws<FormatException>(() => new IpAllowList(new[] { entry }));

        [Fact]
        public void Empty_list_allows_nobody() => Assert.False(new IpAllowList(Array.Empty<string>()).Contains("127.0.0.1"));

        [Theory]
        [InlineData("10.1.1.1", "", "10.1.1.1")]                                // no proxy configured: peer
        [InlineData("10.1.1.1", "203.0.113.9", "10.1.1.1")]                     // XFF from an untrusted peer is ignored
        [InlineData("127.0.0.1", "203.0.113.9", "203.0.113.9")]                 // trusted proxy: the forwarded client
        [InlineData("127.0.0.1", "198.51.100.1, 203.0.113.9", "203.0.113.9")]  // right-most untrusted hop
        [InlineData("127.0.0.1", "203.0.113.9, 127.0.0.2", "203.0.113.9")]     // proxy chain
        [InlineData("127.0.0.1", "203.0.113.9:51234", "203.0.113.9")]
        [InlineData("127.0.0.1", "garbage", "127.0.0.1")]
        public void X_Forwarded_For_only_from_trusted_proxies(string peer, string xff, string expected)
        {
            var proxies = new IpAllowList(peer == "10.1.1.1" ? Array.Empty<string>() : new[] { "127.0.0.0/8" });
            if (peer == "10.1.1.1" && xff.Length > 0) proxies = new IpAllowList(new[] { "127.0.0.1" });
            Assert.Equal(IPAddress.Parse(expected), IpAllowList.ClientAddress(IPAddress.Parse(peer), xff, proxies));
        }

        [Fact]
        public void Basic_header_parsing()
        {
            string B(string s) => "Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes(s));
            Assert.Equal(("sap_ecp", "pa:ss word"), SapBasicAuthenticationHandler.ParseBasic(B("sap_ecp:pa:ss word")));
            Assert.Null(SapBasicAuthenticationHandler.ParseBasic(B("nocolon")));
            Assert.Null(SapBasicAuthenticationHandler.ParseBasic(B(":onlypassword")));
            Assert.Null(SapBasicAuthenticationHandler.ParseBasic("Basic ***"));
            Assert.Null(SapBasicAuthenticationHandler.ParseBasic("Bearer abc"));
            Assert.Null(SapBasicAuthenticationHandler.ParseBasic(null));
        }

        private static async Task<(int Status, JsonObject? Body, HttpContext Ctx, bool NextCalled)> Run(
            Action<SapInterfaceSettings>? cfg = null, string peer = "127.0.0.1", bool available = true, bool ready = true, string path = "/api/sap/v1/ping",
            Exception? throwInNext = null, int localPort = 5080)
        {
            var repo = new InMemorySapRepository { IsAvailable = available };
            var readiness = new DatabaseReadiness();
            if (ready) readiness.MarkReady();
            var nextCalled = false;
            var mw = new SapInterfaceMiddleware(_ =>
            {
                nextCalled = true;
                if (throwInNext != null) throw throwInNext;
                return Task.CompletedTask;
            }, SapKit.Options(cfg), repo, readiness, NullLogger<SapInterfaceMiddleware>.Instance);
            var ctx = new DefaultHttpContext();
            ctx.Request.Path = path;
            ctx.Connection.RemoteIpAddress = IPAddress.Parse(peer);
            ctx.Connection.LocalPort = localPort;
            ctx.Response.Body = new MemoryStream();
            await mw.InvokeAsync(ctx);
            ctx.Response.Body.Position = 0;
            var text = new StreamReader(ctx.Response.Body).ReadToEnd();
            return (ctx.Response.StatusCode, text.Length > 0 ? JsonNode.Parse(text)!.AsObject() : null, ctx, nextCalled);
        }

        [Fact]
        public async Task An_allowed_caller_passes_with_a_trace_id()
        {
            var (status, body, ctx, next) = await Run();
            Assert.True(next);
            Assert.Equal(200, status);
            Assert.Null(body);
            Assert.StartsWith("T-", ctx.Response.Headers["X-Trace-Id"].ToString());
            Assert.Equal("127.0.0.1", ctx.Items[SapInterfaceMiddleware.ClientIpItem]);
        }

        [Fact]
        public async Task A_caller_outside_the_allow_list_gets_401_IP_NOT_ALLOWED()
        {
            var (status, body, ctx, next) = await Run(peer: "10.9.9.9");
            Assert.False(next);
            Assert.Equal(401, status);
            Assert.Equal("IP_NOT_ALLOWED", body!["error_code"]!.GetValue<string>());
            Assert.Equal(ctx.Response.Headers["X-Trace-Id"].ToString(), body["trace_id"]!.GetValue<string>());
            Assert.StartsWith("Basic", ctx.Response.Headers.WWWAuthenticate.ToString());
        }

        [Fact]
        public async Task With_the_allow_list_switched_off_any_caller_ip_passes_to_authentication()
        {
            var (status, _, ctx, next) = await Run(c => c.EnforceIpAllowList = false, peer: "10.9.9.9");
            Assert.True(next);
            Assert.Equal(200, status);
            Assert.Equal("10.9.9.9", ctx.Items[SapInterfaceMiddleware.ClientIpItem]);
        }

        [Fact]
        public async Task With_the_allow_list_switched_on_but_empty_every_caller_is_refused()
        {
            var (status, body, _, next) = await Run(c => { c.EnforceIpAllowList = true; c.AllowedIps.Clear(); });
            Assert.False(next);
            Assert.Equal(401, status);
            Assert.Equal("IP_NOT_ALLOWED", body!["error_code"]!.GetValue<string>());
            Assert.Contains("lists no addresses", body["message"]!.GetValue<string>());
        }

        [Fact]
        public async Task Disabled_storage_readiness_port_and_body_size_answer_with_the_SAP_error_body()
        {
            Assert.Equal((503, "SAP_INTERFACE_DISABLED"), Code(await Run(c => c.Enabled = false)));
            Assert.Equal((503, "STORAGE_NOT_SUPPORTED"), Code(await Run(available: false)));
            Assert.Equal((503, "DATABASE_UNAVAILABLE"), Code(await Run(ready: false)));
            Assert.Equal((404, "NOT_FOUND"), Code(await Run(c => c.OnlyOnPort = 8443)));
            Assert.True((await Run(c => c.OnlyOnPort = 8443, localPort: 8443)).NextCalled);
            Assert.Equal((503, "STORAGE_NOT_SUPPORTED"), Code(await Run(throwInNext: new SapApiException(503, "STORAGE_NOT_SUPPORTED", "SAP interface requires SQL Server storage"))));
            Assert.Equal((503, "INTERNAL_ERROR"), Code(await Run(throwInNext: new InvalidOperationException("boom"))));
        }

        private static (int, string) Code((int Status, JsonObject? Body, HttpContext Ctx, bool NextCalled) r) => (r.Status, r.Body!["error_code"]!.GetValue<string>());

        [Fact]
        public async Task Other_paths_are_not_touched()
        {
            var (status, body, ctx, next) = await Run(peer: "10.9.9.9", path: "/api/v1/Health");
            Assert.True(next);
            Assert.Null(body);
            Assert.False(ctx.Response.Headers.ContainsKey("X-Trace-Id"));
        }
    }
}
