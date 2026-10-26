using System.Text.Json.Nodes;
using Application.Interfaces.V1;
using Application.Interfaces.V1.Sap;
using Application.Services.V1.Sap;
using DecisionEngine.Sap;
using DecisionEngine.Tool1;
using Domain.Entities.Response;
using Domain.Entities.Sap;
using Domain.Exceptions;
using Microsoft.Extensions.Logging.Abstractions;
using POGovernance.Tests.Tool1;

namespace POGovernance.Tests.Sap
{
    /// <summary>The verdict link (view_url) of a decision, the verdict page behind it, and POST /documents (MRN) + the PO document report.</summary>
    public class SapDocumentAndLinkTests
    {
        private sealed class FakeApi : IAPI
        {
            public string? Base = "http://portal:5090";
            public int Calls;
            public Task<ApiMasterRes> GetAllApi(CancellationToken ct = default) => throw new NotSupportedException();
            public Task<ApiMasterRes> GetSpecificApiData(string? taskName, string? param, CancellationToken ct = default) => throw new NotSupportedException();
            public Task<string?> GetBaseUrlAsync(string baseUrlKey, CancellationToken ct = default)
            {
                Calls++;
                return Task.FromResult(baseUrlKey == "POGOV_PORTAL" && Base != null ? Base.TrimEnd('/') + "/" : null);
            }
        }

        private sealed class Kit
        {
            public InMemorySapRepository Repo { get; } = new();
            public FakeAppClock Clock { get; } = new() { TodayValue = SapKit.Today };
            public FakeApi Api { get; } = new();
            public Domain.Entities.AppSettings.SapInterfaceSettings Cfg { get; } = new() { RulesVersion = "R-T", DecisionTimeoutMs = 30000 };

            public SapDecisionService Decisions() =>
                new(Repo, new FakeDatasetCache { Data = SapKit.Data }, SapKit.Store(), Clock, new SapDecisionEngine(new Tool1DecisionEngine()), new FakeEventStatus(),
                    Microsoft.Extensions.Options.Options.Create(Cfg), NullLogger<SapDecisionService>.Instance, Api);
            public SapDocumentService Documents() => new(Repo, Clock, NullLogger<SapDocumentService>.Instance);
            public SapVerdictViewService View() => new(Repo, Clock);

            public async Task<JsonObject> DecideAsync(string requestId)
            {
                var r = await Decisions().DecideAsync(SapKit.Request(requestId, SapKit.Item(10, "1101000101", "WHT", 100)).ToJsonString(), SapKit.Caller);
                Assert.Equal(200, r.Status);
                return JsonNode.Parse(r.Json)!.AsObject();
            }
        }

        private static JsonObject Parse(SapCallResult r) => JsonNode.Parse(r.Json)!.AsObject();

        // ── verdict link ──

        [Fact]
        public async Task An_answered_decision_carries_a_verdict_link_with_a_random_token()
        {
            var k = new Kit();
            var a = await k.DecideAsync("WI-LINK-1");
            var url = a["view_url"]!.GetValue<string>();
            Assert.StartsWith("http://portal:5090/verdict/", url);
            var token = url["http://portal:5090/verdict/".Length..];
            Assert.Matches("^[A-Za-z0-9_-]{43}$", token);
            var row = k.Repo.Decisions.Single().Row;
            Assert.Equal(token, row.ViewToken);
            Assert.Equal(k.Clock.Now.AddDays(90), row.ViewExpiresAt);
            Assert.Equal("2027-01-19T10:00:00+05:30", a["view_url_expires_at"]!.GetValue<string>());
            Assert.NotEqual(token, (await k.DecideAsync("WI-LINK-2"))["view_url"]!.GetValue<string>()[^43..]);
        }

        [Fact]
        public async Task A_replay_returns_the_same_link_and_the_base_url_is_cached()
        {
            var k = new Kit();
            var body = SapKit.Request("WI-LINK-R", SapKit.Item(10, "1101000101", "WHT", 100)).ToJsonString();
            var svc = k.Decisions();
            var first = Parse(await svc.DecideAsync(body, SapKit.Caller));
            var again = Parse(await svc.DecideAsync(body, SapKit.Caller));
            Assert.Equal(first["view_url"]!.GetValue<string>(), again["view_url"]!.GetValue<string>());
            Assert.Equal(1, k.Api.Calls);
        }

        [Fact]
        public async Task A_decision_made_before_the_link_existed_gets_one_on_its_first_replay()
        {
            var k = new Kit();
            k.Cfg.VerdictLink.Enabled = false;                                    // decided before the upgrade
            var body = SapKit.Request("WI000004711-4500099001-V1", SapKit.Item(1, "1101000101", "WHT", 100)).ToJsonString();
            var before = Parse(await k.Decisions().DecideAsync(body, SapKit.Caller));
            Assert.Null(before["view_url"]);

            k.Cfg.VerdictLink.Enabled = true;                                     // after the upgrade SAP repeats the call
            var svc = k.Decisions();
            var replay = await svc.DecideAsync(body, SapKit.Caller);
            Assert.True(replay.Replayed);
            var j = Parse(replay);
            Assert.Equal(before["decision_id"]!.GetValue<string>(), j["decision_id"]!.GetValue<string>());
            var url = j["view_url"]!.GetValue<string>();
            Assert.StartsWith("http://portal:5090/verdict/", url);
            var keys = j.Select(kv => kv.Key).ToList();
            Assert.Equal(keys.IndexOf("items") - 2, keys.IndexOf("view_url"));    // same place as in a new answer
            var row = k.Repo.Decisions.Single().Row;
            Assert.Equal(url[^43..], row.ViewToken);
            Assert.Equal(replay.Json, row.ResponseJson);
            Assert.Equal(replay.Json, (await svc.DecideAsync(body, SapKit.Caller)).Json);   // later replays: the same answer
            Assert.Equal("WI000004711-4500099001-V1", (await k.View().GetAsync(row.ViewToken!)).RequestId);
        }

        [Fact]
        public async Task No_link_when_switched_off_or_without_a_portal_base_url()
        {
            var off = new Kit();
            off.Cfg.VerdictLink.Enabled = false;
            Assert.Null((await off.DecideAsync("WI-NOLINK-1"))["view_url"]);
            Assert.Null(off.Repo.Decisions.Single().Row.ViewToken);

            var noBase = new Kit();
            noBase.Api.Base = null;
            Assert.Null((await noBase.DecideAsync("WI-NOLINK-2"))["view_url"]);

            var fixedBase = new Kit();
            fixedBase.Cfg.VerdictLink.PortalBaseUrl = "https://pogov.example";
            Assert.StartsWith("https://pogov.example/verdict/", (await fixedBase.DecideAsync("WI-FIXED"))["view_url"]!.GetValue<string>());
            Assert.Equal(0, fixedBase.Api.Calls);
        }

        [Fact]
        public async Task The_verdict_page_shows_business_fields_and_refuses_unknown_or_expired_links()
        {
            var k = new Kit();
            var a = await k.DecideAsync("WI-VIEW");
            var token = a["view_url"]!.GetValue<string>()[^43..];
            var v = await k.View().GetAsync(token);
            Assert.Equal(a["decision_id"]!.GetValue<string>(), v.DecisionId);
            Assert.Equal("WI-VIEW", v.RequestId);
            Assert.Equal(a["overall_verdict"]!.GetValue<string>(), v.OverallVerdict);
            Assert.Equal(a["summary_text"]!.GetValue<string>(), v.SummaryText);
            var item = Assert.Single(v.Items);
            Assert.Equal(10, item.PoItem);
            var metrics = a["items"]![0]!["metrics"]!.AsObject();
            Assert.Equal(metrics["plan_qty"]?.GetValue<double>(), item.PlanQty);
            Assert.Equal(metrics["stock_qty"]?.GetValue<double>(), item.StockQty);
            Assert.Equal(a["items"]![0]!["rule_id"]!.GetValue<string>(), item.RuleId);
            Assert.Equal("R-T", v.RulesVersion);
            Assert.Equal(a["data_as_of"]!["bw_snapshot"]!.GetValue<string>(), v.BwSnapshot);
            Assert.Equal("ECP", v.SourceSystem);
            Assert.Null(v.Document);

            await Assert.ThrowsAsync<NotFoundException>(() => k.View().GetAsync("x"));
            await Assert.ThrowsAsync<NotFoundException>(() => k.View().GetAsync(new string('A', 43)));
            k.Clock.TodayValue = "2027-02-01";
            var ex = await Assert.ThrowsAsync<AppException>(() => k.View().GetAsync(token));
            Assert.Equal(410, ex.Status);
        }

        // ── POST /documents ──

        private static string Doc(string requestId, string docNo, bool replace = false, string? date = "2026-10-26") =>
            new JsonObject { ["request_id"] = requestId, ["document_no"] = docNo, ["document_date"] = date, ["replace"] = replace }.ToJsonString();

        [Fact]
        public async Task A_document_is_stored_once_per_request_and_can_be_corrected()
        {
            var k = new Kit();
            var a = await k.DecideAsync("WI-DOC");
            var svc = k.Documents();

            var r1 = await svc.ReceiveAsync(Doc("WI-DOC", "5000123456"), SapKit.Caller);
            Assert.Equal(200, r1.Status);
            var j1 = Parse(r1);
            Assert.Equal(("STORED", "MRN", a["decision_id"]!.GetValue<string>()), (j1["status"]!.GetValue<string>(), j1["document_type"]!.GetValue<string>(), j1["decision_id"]!.GetValue<string>()));
            Assert.EndsWith("+05:30", j1["received_at"]!.GetValue<string>());

            Assert.Equal("UNCHANGED", Parse(await svc.ReceiveAsync(Doc("WI-DOC", "5000123456"), SapKit.Caller))["status"]!.GetValue<string>());

            var conflict = await svc.ReceiveAsync(Doc("WI-DOC", "5000999999"), SapKit.Caller).ContinueWith(t => t.Exception?.InnerException);
            var sx = Assert.IsType<SapApiException>(conflict);
            Assert.Equal((409, SapErrorCodes.DocumentConflict), (sx.Status, sx.ErrorCode));
            Assert.Contains("5000123456", sx.Message);

            Assert.Equal("REPLACED", Parse(await svc.ReceiveAsync(Doc("WI-DOC", "5000999999", replace: true), SapKit.Caller))["status"]!.GetValue<string>());
            Assert.Equal(new[] { ("5000123456", false), ("5000999999", true) }, k.Repo.Documents.Select(d => (d.DocumentNo, d.IsCurrent)));

            var view = await k.View().GetAsync(a["view_url"]!.GetValue<string>()[^43..]);
            Assert.Equal("5000999999", view.Document!.DocumentNo);
        }

        [Theory]
        [InlineData("{\"document_no\":\"1\"}", 400, "request_id is required")]
        [InlineData("{\"request_id\":\"WI-X\"}", 400, "document_no is required")]
        [InlineData("{\"request_id\":\"WI-X\",\"document_no\":\"1\",\"document_date\":\"26.10.2026\"}", 400, "document_date must be a date YYYY-MM-DD")]
        [InlineData("{\"request_id\":", 400, "Invalid JSON")]
        [InlineData("{\"request_id\":\"WI-UNKNOWN\",\"document_no\":\"1\"}", 404, "No answered decision for request_id WI-UNKNOWN")]
        public async Task Bad_or_unknown_document_requests_are_refused(string body, int status, string message)
        {
            var k = new Kit();
            var ex = await Assert.ThrowsAsync<SapApiException>(() => k.Documents().ReceiveAsync(body, SapKit.Caller));
            Assert.Equal(status, ex.Status);
            Assert.StartsWith(message, ex.Message);
            Assert.Equal(status == 404 ? SapErrorCodes.RequestNotFound : status == 400 && message.StartsWith("Invalid") ? SapErrorCodes.InvalidJson : SapErrorCodes.ValidationFailed, ex.ErrorCode);
        }

        [Fact]
        public async Task The_report_lists_POs_with_and_without_a_final_document()
        {
            var k = new Kit();
            await k.DecideAsync("WI-R1");
            await k.DecideAsync("WI-R2");
            await k.Documents().ReceiveAsync(Doc("WI-R1", "MRN-1"), SapKit.Caller);
            var with = await k.Repo.DocumentReportAsync(new SapDocumentReportReq { HasDocument = "yes" }, 330);
            Assert.Equal(("WI-R1", "MRN-1"), (Assert.Single(with.Items).RequestId, with.Items[0].DocumentNo));
            var without = await k.Repo.DocumentReportAsync(new SapDocumentReportReq { HasDocument = "no" }, 330);
            Assert.Equal("WI-R2", Assert.Single(without.Items).RequestId);
        }
    }
}
