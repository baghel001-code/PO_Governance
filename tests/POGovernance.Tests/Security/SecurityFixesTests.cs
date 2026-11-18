using System.Reflection;
using System.Security.Claims;
using System.Text.Json.Nodes;
using Domain.Constants;
using Domain.Entities.AppSettings;
using Domain.Exceptions;
using Infrastructure.Middleware;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using POGovernance.Tests.Sap;

namespace POGovernance.Tests.Security
{
    /// <summary>docs/security/Python-vs-NET-Security-Review.md, gaps N1-N4.</summary>
    public class SecurityFixesTests
    {
        [Fact]
        public void N1_no_endpoint_saves_a_request_as_sent_by_the_caller()
        {
            var actions = typeof(POGovApi.Controllers.V1.ApiControllerBase).Assembly.GetTypes()
                .Where(t => typeof(ControllerBase).IsAssignableFrom(t))
                .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
                .Select(m => m.Name).ToList();
            Assert.DoesNotContain("RequestSave", actions);
            Assert.Contains("Tool1Validate", actions);                 // requests are saved through the server-side check
            Assert.DoesNotContain("PO_REQUEST_SAVE", File.ReadAllText(SapVerdictMapTests.RepoPath("Admin", "appsettings.json")));
        }

        [Theory]
        [InlineData(Roles.Buyer)]
        [InlineData(Roles.CatHead)]
        public async Task N2_buyers_and_category_heads_cannot_download_the_data_files(string role)
        {
            var c = new POGovApi.Controllers.V1.DataController(null!, Options.Create(new PoGovSettings()))
            {
                ControllerContext = new ControllerContext
                {
                    HttpContext = new DefaultHttpContext
                    {
                        User = new ClaimsPrincipal(new ClaimsIdentity(new[]
                        {
                            new Claim(CustomClaimTypes.Email, "b@x.in"), new Claim(ClaimTypes.Role, role)
                        }, "test"))
                    }
                }
            };
            var e = await Assert.ThrowsAsync<ForbiddenException>(() => c.DataGet(new Domain.Entities.Request.DataKeyReq { Key = "f1" }, default));
            Assert.Equal("Planner or admin only", e.Message);
        }

        [Fact]
        public async Task N3_an_unexpected_error_answers_without_internal_details_but_with_a_reference()
        {
            var mw = new ExceptionMiddleware(_ => throw new InvalidOperationException("SELECT * FROM dbo.SECRET_TABLE at D:\\app"),
                                             NullLogger<ExceptionMiddleware>.Instance);
            var ctx = new DefaultHttpContext();
            ctx.Request.Path = "/api/v1/SomeAction";
            ctx.Response.Body = new MemoryStream();
            await mw.InvokeAsync(ctx);
            ctx.Response.Body.Position = 0;
            var body = await new StreamReader(ctx.Response.Body).ReadToEndAsync();
            Assert.Equal(500, ctx.Response.StatusCode);
            Assert.DoesNotContain("SECRET_TABLE", body);
            Assert.DoesNotContain("D:\\\\app", body);
            Assert.Matches(@"reference E-[0-9A-F]{6}", body);
            Assert.StartsWith(ExceptionMiddleware.ServerError, JsonNode.Parse(body)!["responseMessage"]!.GetValue<string>());

            var (status, code, message) = SapInterfaceMiddleware.Map(new InvalidOperationException("SELECT * FROM dbo.SECRET_TABLE"));
            Assert.Equal((503, "INTERNAL_ERROR"), (status, code));
            Assert.DoesNotContain("SECRET_TABLE", message);
        }

        [Fact]
        public void N4_the_portal_sends_a_content_security_policy_without_inline_scripts()
        {
            var csp = Admin.Auth.PortalSecurity.ContentSecurityPolicy;
            var script = csp.Split(';').Select(d => d.Trim()).Single(d => d.StartsWith("script-src"));
            Assert.Equal("script-src 'self'", script);
            Assert.Contains("object-src 'none'", csp);
            Assert.Contains("frame-ancestors 'self'", csp);
            Assert.Contains("Admin.Auth.PortalSecurity.ContentSecurityPolicy", File.ReadAllText(SapVerdictMapTests.RepoPath("Admin", "Program.cs")));
            // no inline event handlers in the pages (they would be blocked by script-src 'self')
            foreach (var f in Directory.GetFiles(SapVerdictMapTests.RepoPath("Admin", "Components"), "*.razor", SearchOption.AllDirectories))
                Assert.DoesNotMatch(@"\s(onclick|onchange|onload|onsubmit|oninput)=""", File.ReadAllText(f));
        }
    }
}
