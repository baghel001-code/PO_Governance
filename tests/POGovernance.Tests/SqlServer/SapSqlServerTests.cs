using System.Text.Json.Nodes;
using Application.Services.V1.Sap;
using Dapper;
using DecisionEngine.Sap;
using DecisionEngine.Tool1;
using Domain.Entities.Sap;
using Domain.Exceptions;
using Infrastructure.Repositories;
using Infrastructure.Utilitys;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging.Abstractions;
using POGovernance.Tests.Sap;
using POGovernance.Tests.Tool1;

namespace POGovernance.Tests.SqlServer
{
    /// <summary>
    /// database/sqlserver/05_sap_interface.sql and the PRO_SAP_* procedures through <see cref="SapSqlServerRepository"/> on a real
    /// SQL Server (POGOV_SQLSERVER_TEST): technical users + lockout, decision log + idempotency index + sequence, events
    /// de-duplication / LATE / lag, reason codes, and a full decision through the real repository.
    /// </summary>
    public class SapSqlServerTests : IClassFixture<SqlServerFixture>
    {
        private readonly SqlServerFixture _fx;
        public SapSqlServerTests(SqlServerFixture fx) => _fx = fx;

        private SapSqlServerRepository Repo() => new(new DapperHelper(_fx.ConnectionString));

        private async Task<T> Scalar<T>(string sql)
        {
            await using var c = new SqlConnection(_fx.ConnectionString);
            return await c.ExecuteScalarAsync<T>(sql);
        }

        [SqlServerFact]
        public async Task Schema_v3_seed_and_rerun()
        {
            Assert.Equal("4", await Scalar<string>("SELECT value FROM schema_info WHERE [key] = 'version'"));
            await SqlServerSchema.ApplyAsync(_fx.ConnectionString, new[] { SqlServerSchema.SapInterfaceScript });   // idempotent
            Assert.Equal(6, await Scalar<int>("SELECT COUNT(*) FROM SAP_REASON_CODES WHERE Code_Type = 'EXCEPTION'"));
            var verdictCodes = (await Repo().ListReasonCodesAsync("VERDICT")).Select(r => r.Code).OrderBy(x => x);
            Assert.Equal(SapVerdictMap.Table.Select(r => r.ReasonCode).Distinct().OrderBy(x => x), verdictCodes);
            foreach (var p in new[] { "PRO_SAP_TECH_USER", "PRO_SAP_DECISION_SAVE", "PRO_SAP_DECISION_GET", "PRO_SAP_EVENTS_SAVE", "PRO_SAP_EVENTS_GET", "PRO_SAP_REASON_CODES" })
                Assert.Equal(1, await Scalar<int>($"SELECT COUNT(*) FROM sys.procedures WHERE name = '{p}'"));
        }

        [SqlServerFact]
        public async Task Technical_users_lifecycle_and_lockout()
        {
            var r = Repo();
            var u = await r.AddTechUserAsync("sap_ecq_t", "ecq", "QA", "hash1", "admin");
            Assert.Equal("ECQ", u.SourceSystem);
            Assert.True(u.Active);
            var dup = await Assert.ThrowsAsync<SapStoreException>(() => r.AddTechUserAsync("SAP_ECQ_T", "ECQ", null, "h", "admin"));
            Assert.Equal(50409, dup.ErrorCode);
            for (var i = 0; i < 2; i++) Assert.Null((await r.LoginFailAsync("sap_ecq_t", 3, 15))!.LockedUntil);
            var locked = await r.LoginFailAsync("sap_ecq_t", 3, 15);
            Assert.Equal(3, locked!.FailedCount);
            Assert.True(locked.LockedUntil > DateTimeOffset.UtcNow.AddMinutes(14));
            Assert.Null((await r.UnlockTechUserAsync("sap_ecq_t")).LockedUntil);
            await r.LoginOkAsync("sap_ecq_t", "10.1.2.3");
            var got = (await r.GetTechUserAsync("sap_ecq_t"))!;
            Assert.Equal("10.1.2.3", got.LastCallIp);
            Assert.NotNull(got.LastCallAt);
            Assert.Equal("hash1", got.PasswordHash);
            Assert.Equal("hash2", (await r.SetTechUserPasswordAsync("sap_ecq_t", "hash2", "admin")).PasswordHash);
            Assert.False((await r.SetTechUserActiveAsync("sap_ecq_t", false, "admin")).Active);
            Assert.Contains(await r.ListTechUsersAsync(), x => x.UserName == "sap_ecq_t");
            Assert.Equal(50404, (await Assert.ThrowsAsync<SapStoreException>(() => r.UnlockTechUserAsync("nobody"))).ErrorCode);
            Assert.Null(await r.GetTechUserAsync("nobody"));
            Assert.Null(await r.LoginFailAsync("nobody", 3, 15));
        }

        private static SapDecisionLogRow Row(string requestId, int status = 200, string? decisionId = null) => new()
        {
            DecisionId = decisionId, RequestId = requestId, BodyHash = new string('a', 64), Mode = "SHADOW", SourceSystem = "ECP", Client = "300",
            PoNumber = "4500099001", BuyerUser = "BUYER07", Division = "MENS", OverallVerdict = status == 200 ? "REDUCE" : null,
            VerdictCode = status == 200 ? "R" : null, RouteTo = status == 200 ? "BUYER" : null, HttpStatus = status, ErrorCode = status == 200 ? null : "X",
            ItemCount = 1, DurationMs = 42, TechUser = "sap_ecp", ClientIp = "10.0.0.1", TraceId = "T-abcdef", DecidedAt = DateTimeOffset.Now,
            RequestJson = "{\"request_id\":\"" + requestId + "\"}", ResponseJson = status == 200 ? "{\"decision_id\":\"" + decisionId + "\"}" : null
        };

        [SqlServerFact]
        public async Task Decision_log_sequence_items_and_one_answer_per_request_id()
        {
            var r = Repo();
            var s1 = await r.NextDecisionSeqAsync();
            var s2 = await r.NextDecisionSeqAsync();
            Assert.Equal(s1 + 1, s2);
            var items = new[]
            {
                new SapDecisionItemRow { PoItem = 10, Article = "a", Gm = "1101000101", Verdict = "REDUCE", VerdictCode = "R", MaxQty = 500, ReasonCode = "COVER_ABOVE_NORM",
                                         RuleId = "COVER-01", ReasonText = "text", Engine = "B", EngineDecision = "Hold — Don't Raise", EngineReason = new string('r', 5000),
                                         MetricsJson = "{\"plan_qty\":2000}", OrderQty = 1200, GroupQty = 1200 },
                new SapDecisionItemRow { PoItem = 20, Verdict = "ALLOW", VerdictCode = "A", MetricsJson = "{}" }
            };
            var id = await r.SaveDecisionAsync(Row("SQL-REQ-1", 200, "D-20261021-" + s2.ToString("D6")), items);
            await Assert.ThrowsAsync<SapRequestIdTakenException>(() => r.SaveDecisionAsync(Row("SQL-REQ-1", 200, "D-x"), items));
            await r.SaveDecisionAsync(Row("SQL-REQ-1", 409), Array.Empty<SapDecisionItemRow>());     // errors: any number of rows
            await r.SaveDecisionAsync(Row("SQL-REQ-1", 503), Array.Empty<SapDecisionItemRow>());

            var byReq = (await r.GetDecisionByRequestAsync("SQL-REQ-1"))!;
            Assert.Equal(id, byReq.Id);
            Assert.Equal(new string('a', 64), byReq.BodyHash);
            Assert.StartsWith("{\"decision_id\"", byReq.ResponseJson);
            var detail = await r.GetDecisionAsync(id);
            Assert.Equal("4500099001", detail.Decision!.PoNumber);
            Assert.Equal(2, detail.Items.Count);
            Assert.Equal(500, detail.Items[0].MaxQty);
            Assert.Equal(5000, detail.Items[0].EngineReason!.Length);
            Assert.Equal("Hold — Don't Raise", detail.Items[0].EngineDecision);

            var list = await r.ListDecisionsAsync(new SapDecisionListReq { PoNumber = "45000990", Status = "error", Max = 10 }, 330);
            Assert.True(list.Total >= 2);
            Assert.All(list.Items, x => Assert.NotEqual(200, x.HttpStatus));
            var ok = await r.ListDecisionsAsync(new SapDecisionListReq { Verdict = "REDUCE", Mode = "SHADOW", From = DateTime.Now.AddDays(-1).ToString("yyyy-MM-dd") }, 330);
            Assert.Contains(ok.Items, x => x.Id == id);
            var stats = await r.DecisionStatsAsync(DateTimeOffset.Now.ToOffset(TimeSpan.FromMinutes(330)).ToString("yyyy-MM-dd"), 330);
            Assert.True(stats.Total >= 3 && stats.NotChecked >= 2 && stats.Reduce >= 1 && stats.Today >= 3);
            Assert.Equal(42, stats.P50Ms);
        }

        [SqlServerFact]
        public async Task Verdict_link_token_final_document_and_report()
        {
            var r = Repo();
            var req = "SQL-DOC-" + Guid.NewGuid().ToString("N")[..8];
            var token = Application.Services.V1.Sap.SapDecisionService.NewViewToken();
            var row = Row(req, 200, "D-DOC-" + req[^8..]);
            row.ViewToken = token;
            row.ViewExpiresAt = DateTimeOffset.Now.AddDays(90);
            var id = await r.SaveDecisionAsync(row, new[] { new SapDecisionItemRow { PoItem = 10, Verdict = "REDUCE", OrderQty = 1200, GroupQty = 1200, MetricsJson = "{}" } });
            await r.SaveDecisionAsync(Row(req + "-nodoc", 200, "D-ND-" + req[^8..]), new[] { new SapDecisionItemRow { PoItem = 10, OrderQty = 5, MetricsJson = "{}" } });

            var byToken = (await r.GetDecisionByViewTokenAsync(token))!;
            Assert.Equal(id, byToken.Decision!.Id);
            Assert.Equal(token, byToken.Decision.ViewToken);
            Assert.NotNull(byToken.Decision.ViewExpiresAt);
            Assert.Null(await r.GetDecisionByViewTokenAsync(SapDecisionServiceToken()));

            SapDocumentInsert Doc(string no, bool replace = false) => new()
            {
                RequestId = req, DocumentNo = no, DocumentType = "MRN", DocumentDate = "2026-10-26", Replace = replace, TechUser = "sap_ecp", SourceSystem = "ECP"
            };
            var s1 = await r.SaveDocumentAsync(Doc("5000123456"));
            Assert.Equal(("STORED", row.DecisionId), (s1.Result, s1.DecisionId));
            Assert.Equal("UNCHANGED", (await r.SaveDocumentAsync(Doc("5000123456"))).Result);
            var c = await r.SaveDocumentAsync(Doc("5000999999"));
            Assert.True(c.Conflict);
            Assert.Equal("5000123456", c.CurrentDocumentNo);
            Assert.Equal("REPLACED", (await r.SaveDocumentAsync(Doc("5000999999", replace: true))).Result);
            Assert.True((await r.SaveDocumentAsync(new SapDocumentInsert { RequestId = "SQL-NO-SUCH-REQUEST", DocumentNo = "1" })).NotFound);

            var detail = await r.GetDecisionAsync(id);
            Assert.Equal(new[] { ("5000999999", true), ("5000123456", false) }, detail.Documents.Select(d => (d.DocumentNo, d.IsCurrent)));
            Assert.Equal("4500099001", detail.Documents[0].PoNumber);   // taken from the decision
            Assert.Equal("2026-10-26", detail.Documents[0].DocumentDate);
            Assert.Equal("5000999999", (await r.GetDecisionByViewTokenAsync(token))!.Documents[0].DocumentNo);

            var with = await r.DocumentReportAsync(new SapDocumentReportReq { PoNumber = req, HasDocument = "yes" }, 330);
            var line = Assert.Single(with.Items);
            Assert.Equal((req, "5000999999", "MRN", 1200d), (line.RequestId, line.DocumentNo, line.DocumentType, line.OrderQty));
            var byDocNo = await r.DocumentReportAsync(new SapDocumentReportReq { PoNumber = "5000999999", HasDocument = "all" }, 330);
            Assert.Contains(byDocNo.Items, x => x.RequestId == req);
            var without = await r.DocumentReportAsync(new SapDocumentReportReq { PoNumber = req, HasDocument = "no" }, 330);
            Assert.Equal(req + "-nodoc", Assert.Single(without.Items).RequestId);
        }

        private static string SapDecisionServiceToken() => Application.Services.V1.Sap.SapDecisionService.NewViewToken();

        private static SapEventInsert Ev(string msg, long seq, string status = "ACCEPTED", string type = "GR_POSTED") => new()
        {
            MsgId = msg, Seq = seq, EventType = type, OccurredAt = DateTimeOffset.Now.AddSeconds(-5).ToString("yyyy-MM-dd'T'HH:mm:ss.fffffffzzz"), SapUser = "DCUSER01",
            PoNumber = "4500098765", PoItem = 10, MaterialDoc = "5000123456", Status = status, ErrorCode = status == "PARKED" ? "VALIDATION_FAILED" : null,
            ErrorMessage = status == "PARKED" ? "goods_movement.article is required" : null, PayloadJson = "{\"msg_id\":\"" + msg + "\"}"
        };

        [SqlServerFact]
        public async Task Events_are_stored_once_with_order_flags_and_lag()
        {
            var r = Repo();
            var q = "ZQ_" + Guid.NewGuid().ToString("N")[..6];
            var res = await r.SaveEventsAsync(new[] { Ev(q + "-1", 10), Ev(q + "-2", 20), Ev(q + "-P", 21, "PARKED"), Ev(q + "-1", 10) }, q, "ECP", "300", "sap_ecp", "B-1");
            Assert.Equal(new[] { "ACCEPTED", "ACCEPTED", "PARKED", "DUPLICATE" }, res.Select(x => x.Result));
            var again = await r.SaveEventsAsync(new[] { Ev(q + "-2", 20), Ev(q + "-3", 15), Ev(q + "-4", 30) }, q, "ECP", "300", "sap_ecp", "B-2");
            Assert.Equal(new[] { "DUPLICATE", "ACCEPTED", "ACCEPTED" }, again.Select(x => x.Result));
            var list = await r.ListEventsAsync(new SapEventListReq { Q = q, Max = 50 }, 330);
            Assert.Equal(5, list.Total);
            var byMsg = list.Items.ToDictionary(e => e.MsgId);
            Assert.Equal("LATE", byMsg[q + "-3"].SeqStatus);
            Assert.Equal("IN_ORDER", byMsg[q + "-4"].SeqStatus);
            Assert.Equal(2, byMsg[q + "-1"].DupCount + byMsg[q + "-2"].DupCount);
            Assert.Equal("PARKED", byMsg[q + "-P"].Status);
            Assert.All(list.Items, e => Assert.False(e.Applied));
            Assert.InRange(byMsg[q + "-4"].LagSeconds, 3, 60);
            var one = (await r.GetEventAsync(byMsg[q + "-4"].Id))!;
            Assert.Equal("{\"msg_id\":\"" + q + "-4\"}", one.PayloadJson);
            Assert.Equal(1, (await r.ListEventsAsync(new SapEventListReq { Q = q, Status = "LATE" }, 330)).Total);
            Assert.Equal(1, (await r.ListEventsAsync(new SapEventListReq { Q = q, Status = "PARKED" }, 330)).Total);
            var st = await r.EventStatusAsync(DateTimeOffset.Now.ToOffset(TimeSpan.FromMinutes(330)).ToString("yyyy-MM-dd"), 330);
            Assert.True(st.Total >= 5 && st.Parked >= 1 && st.Late >= 1 && st.NotApplied >= 4);
            Assert.InRange(st.LagSeconds, 3, 60);
        }

        [SqlServerFact]
        public async Task Exception_reason_codes_save_and_active_list()
        {
            var r = Repo();
            await r.SaveReasonCodeAsync(new SapReasonCodeSaveReq { Code = "zz1", Text = "Test code", Active = true }, "planner");
            var (codes, version) = await r.ActiveExceptionCodesAsync(330);
            Assert.Contains(codes, c => c.Code == "ZZ1");
            Assert.Matches(@"^\d{4}-\d{2}-\d{2}$", version);
            await r.SaveReasonCodeAsync(new SapReasonCodeSaveReq { Code = "ZZ1", Text = "Test code", Active = false }, "planner");
            Assert.DoesNotContain((await r.ActiveExceptionCodesAsync(330)).Codes, c => c.Code == "ZZ1");
            var tooLong = await Assert.ThrowsAsync<SapStoreException>(() => r.SaveReasonCodeAsync(new SapReasonCodeSaveReq { Code = "TOOLONG", Text = "x" }, "p"));
            Assert.Equal(547, tooLong.ErrorCode);   // CHECK constraint: spec maxLength 4
        }

        [SqlServerFact]
        public async Task A_decision_through_the_real_repository_is_idempotent()
        {
            var repo = Repo();
            var svc = new SapDecisionService(repo, new FakeDatasetCache { Data = SapKit.Data }, SapKit.Store(), new FakeAppClock { TodayValue = SapKit.Today },
                new SapDecisionEngine(new Tool1DecisionEngine()), new FakeEventStatus(), SapKit.Options(), NullLogger<SapDecisionService>.Instance);
            var rid = "SQL-E2E-" + Guid.NewGuid().ToString("N")[..8];
            var body = SapKit.Request(rid, SapKit.Item(10, "1101000101", "WHT", 50000), SapKit.Item(20, "2203000201", "OWH", 100)).ToJsonString();
            var first = await svc.DecideAsync(body, SapKit.Caller);
            Assert.Equal(200, first.Status);
            var all = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => svc.DecideAsync(body, SapKit.Caller)));
            Assert.All(all, a => Assert.Equal(first.Json, a.Json));
            Assert.Equal(409, (await svc.DecideAsync(body.Replace("50000", "50001"), SapKit.Caller)).Status);
            var j = JsonNode.Parse(first.Json)!;
            Assert.Equal("HOLD", j["overall_verdict"]!.GetValue<string>());
            Assert.Matches(@"^D-20261021-\d{6,}$", j["decision_id"]!.GetValue<string>());
            var stored = (await repo.GetDecisionAsync(first.LogId!.Value));
            Assert.Equal(2, stored.Items.Count);
            Assert.Equal("BLOCK", stored.Items[1].ReasonCode);
            Assert.Equal(1, await Scalar<int>($"SELECT COUNT(*) FROM SAP_DECISION_LOG WHERE Request_Id = '{rid}' AND Http_Status = 200"));
        }
    }
}
