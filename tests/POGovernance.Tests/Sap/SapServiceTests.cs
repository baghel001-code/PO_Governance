using System.Diagnostics;
using System.Text.Json.Nodes;
using Application.Interfaces.V1.Sap;
using Application.Services.V1.Platform;
using Application.Services.V1.Sap;
using DecisionEngine.Model;
using DecisionEngine.Sap;
using DecisionEngine.Tool1;
using Domain.Entities.AppSettings;
using Domain.Entities.Sap;
using Domain.Exceptions;
using Microsoft.Extensions.Logging.Abstractions;
using POGovernance.Tests.Platform;
using POGovernance.Tests.Tool1;

namespace POGovernance.Tests.Sap
{
    /// <summary>POST /decisions orchestration: idempotency, 409, validation, 422, timeout → 503, overall verdict / route, data_as_of.</summary>
    public class SapDecisionServiceTests
    {
        private sealed class Kit
        {
            public InMemorySapRepository Repo { get; } = new();
            public FakeEventStatus Events { get; } = new();
            public FakeAppClock Clock { get; } = new() { TodayValue = SapKit.Today };
            public FakeDatasetCache Cache { get; } = new() { Data = SapKit.Data };
            public FakeStore Store { get; } = SapKit.Store();
            public SapInterfaceSettings Cfg { get; } = new() { RulesVersion = "R-2026-10-21", DecisionTimeoutMs = 30000 };

            public SapDecisionService Service(ISapDecisionEngine? engine = null) =>
                new(Repo, Cache, Store, Clock, engine ?? new SapDecisionEngine(new Tool1DecisionEngine()), Events,
                    Microsoft.Extensions.Options.Options.Create(Cfg), NullLogger<SapDecisionService>.Instance);
        }

        private static JsonObject Parse(SapCallResult r) => JsonNode.Parse(r.Json)!.AsObject();

        [Fact]
        public async Task A_decision_follows_the_contract()
        {
            var k = new Kit();
            var body = SapKit.Request("WI000004711-4500099001-V1", SapKit.Item(10, "1101000101", "WHT", 50000), SapKit.Item(20, "1101000101", "GRY", 100)).ToJsonString();
            var r = await k.Service().DecideAsync(body, SapKit.Caller);
            Assert.Equal(200, r.Status);
            var j = Parse(r);
            Assert.Equal("D-20261021-000001", j["decision_id"]!.GetValue<string>());
            Assert.Equal("WI000004711-4500099001-V1", j["request_id"]!.GetValue<string>());
            Assert.Equal("SHADOW", j["mode"]!.GetValue<string>());
            Assert.Equal("WATCH", j["overall_verdict"]!.GetValue<string>());     // WATCH > REDUCE
            Assert.Equal("W", j["verdict_code"]!.GetValue<string>());
            Assert.Equal("CATHEAD", j["route_to"]!.GetValue<string>());
            Assert.False(j["bm_head_required"]!.GetValue<bool>());
            Assert.Equal("R-2026-10-21", j["rules_version"]!.GetValue<string>());
            Assert.False(j["data_delayed"]!.GetValue<bool>());
            Assert.Equal("2026-10-21T04:15:00+05:30", j["data_as_of"]!["bw_snapshot"]!.GetValue<string>());
            Assert.Equal("2026-10-21T10:00:00+05:30", j["decided_at"]!.GetValue<string>());
            var items = j["items"]!.AsArray();
            Assert.Equal("REDUCE", items[0]!["verdict"]!.GetValue<string>());
            Assert.True(items[0]!["max_qty"]!.GetValue<double>() > 0);
            Assert.Null(items[1]!["max_qty"]);
            // logged: request + response JSON, body hash, items, mode, duration
            var (row, rows) = Assert.Single(k.Repo.Decisions);
            Assert.Equal(200, row.HttpStatus);
            Assert.Equal(r.Json, row.ResponseJson);
            Assert.Equal(body, row.RequestJson);
            Assert.Equal(64, row.BodyHash!.Length);
            Assert.Equal("SHADOW", row.Mode);
            Assert.Equal("WATCH", row.OverallVerdict);
            Assert.Equal(2, rows.Count);
            Assert.Equal("1101000101", rows[0].Gm);
            Assert.Equal("no-leading-zeros", rows[0].GmMatch);
            Assert.Contains("Hold", rows[0].EngineDecision);
        }

        [Fact]
        public async Task Same_request_id_and_body_returns_the_same_decision()
        {
            var k = new Kit();
            var svc = k.Service();
            var req = SapKit.Request("WI-1", SapKit.Item(10, "1101000101", "WHT", 100));
            var first = await svc.DecideAsync(req.ToJsonString(), SapKit.Caller);
            var pretty = req.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true });   // same JSON, other whitespace
            var again = await svc.DecideAsync(pretty, SapKit.Caller);
            Assert.Equal(200, again.Status);
            Assert.True(again.Replayed);
            Assert.Equal(first.Json, again.Json);
            Assert.Equal(Parse(first)["decision_id"]!.GetValue<string>(), Parse(again)["decision_id"]!.GetValue<string>());
            Assert.Single(k.Repo.Decisions);
            Assert.Equal(1, k.Repo.Seq);
        }

        [Fact]
        public async Task Same_request_id_with_a_different_body_is_409()
        {
            var k = new Kit();
            var svc = k.Service();
            await svc.DecideAsync(SapKit.Request("WI-1", SapKit.Item(10, "1101000101", "WHT", 100)).ToJsonString(), SapKit.Caller);
            var r = await svc.DecideAsync(SapKit.Request("WI-1", SapKit.Item(10, "1101000101", "WHT", 101)).ToJsonString(), SapKit.Caller);
            Assert.Equal(409, r.Status);
            Assert.Equal("REQUEST_ID_CONFLICT", Parse(r)["error_code"]!.GetValue<string>());
            Assert.Equal("T-000001", Parse(r)["trace_id"]!.GetValue<string>());
            Assert.Equal(new[] { 200, 409 }, k.Repo.Decisions.Select(d => d.Row.HttpStatus));
        }

        [Fact]
        public async Task A_concurrent_identical_call_gets_the_stored_answer()
        {
            var k = new Kit();
            var svc = k.Service();
            var body = SapKit.Request("WI-PAR", SapKit.Item(10, "1101000101", "WHT", 100)).ToJsonString();
            var all = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => svc.DecideAsync(body, SapKit.Caller)));
            Assert.All(all, r => Assert.Equal(200, r.Status));
            Assert.Single(all.Select(r => Parse(r)["decision_id"]!.GetValue<string>()).Distinct());
            Assert.Single(k.Repo.Decisions.Where(d => d.Row.HttpStatus == 200));
        }

        public static IEnumerable<object[]> BadRequests()
        {
            JsonObject R() => SapKit.Request("WI-BAD", SapKit.Item(10, "1101000101", "WHT", 100));
            JsonObject With(Action<JsonObject> f) { var r = R(); f(r); return r; }
            yield return new object[] { With(r => r.Remove("request_id")), "request_id is required" };
            yield return new object[] { With(r => r["request_id"] = new string('x', 81)), "request_id must be at most 80 characters" };
            yield return new object[] { With(r => r["mode"] = "TEST"), "mode must be one of SHADOW, LIVE" };
            yield return new object[] { With(r => r.Remove("source_system")), "source_system is required" };
            yield return new object[] { With(r => r["otb_check"] = "FAILED"), "otb_check must be PASSED" };
            yield return new object[] { With(r => r["depth_check"] = "FAILED"), "depth_check must be one of PASSED, BYPASSED" };
            yield return new object[] { With(r => r["requested_at"] = "2026-10-21 11:50"), "requested_at must be an ISO 8601 timestamp" };
            yield return new object[] { With(r => r.Remove("purchase_order")), "purchase_order is required" };
            yield return new object[] { With(r => r["purchase_order"]!.AsObject().Remove("buyer_user")), "purchase_order.buyer_user is required" };
            yield return new object[] { With(r => r["purchase_order"]!["items"] = new JsonArray()), "purchase_order.items must contain at least one item" };
            yield return new object[] { With(r => r["purchase_order"]!["items"]![0]!["order_qty"] = 0), "purchase_order.items[0].order_qty must be > 0" };
            yield return new object[] { With(r => r["purchase_order"]!["items"]![0]!.AsObject().Remove("uom")), "purchase_order.items[0].uom is required" };
            yield return new object[] { With(r => r["purchase_order"]!["items"]![0]!["delivery_date"] = "20.11.2026"), "purchase_order.items[0].delivery_date must be a date YYYY-MM-DD" };
            yield return new object[] { With(r => r["purchase_order"]!["items"]!.AsArray().Add(SapKit.Item(10, "1101000101", "RED", 5))), "purchase_order.items[1].po_item 10 appears more than once" };
        }

        [Theory]
        [MemberData(nameof(BadRequests))]
        public async Task Invalid_requests_are_400_VALIDATION_FAILED(JsonObject body, string message)
        {
            var k = new Kit();
            var r = await k.Service().DecideAsync(body.ToJsonString(), SapKit.Caller);
            Assert.Equal(400, r.Status);
            var j = Parse(r);
            Assert.Equal("VALIDATION_FAILED", j["error_code"]!.GetValue<string>());
            Assert.StartsWith(message, j["message"]!.GetValue<string>());
            Assert.Equal(400, Assert.Single(k.Repo.Decisions).Row.HttpStatus);
        }

        [Theory]
        [InlineData("{\"request_id\": ")]
        [InlineData("not json")]
        [InlineData("{\"request_id\":\"X\",\"purchase_order\":{\"items\":[{\"po_item\":\"ten\"}]}}")]
        public async Task Malformed_json_is_400_INVALID_JSON(string body)
        {
            var r = await new Kit().Service().DecideAsync(body, SapKit.Caller);
            Assert.Equal(400, r.Status);
            Assert.Equal("INVALID_JSON", Parse(r)["error_code"]!.GetValue<string>());
        }

        [Fact]
        public async Task An_unknown_article_is_422_and_logged_with_its_items()
        {
            var k = new Kit();
            var r = await k.Service().DecideAsync(SapKit.Request("WI-U", SapKit.Item(10, "9999999999", "BLK", 100)).ToJsonString(), SapKit.Caller);
            Assert.Equal(422, r.Status);
            Assert.Equal("CANNOT_DECIDE", Parse(r)["error_code"]!.GetValue<string>());
            var (row, items) = Assert.Single(k.Repo.Decisions);
            Assert.Equal(422, row.HttpStatus);
            Assert.Null(row.DecisionId);
            Assert.Contains("unknown to the tool", Assert.Single(items).ReasonText);
        }

        private sealed class SlowEngine : ISapDecisionEngine
        {
            public SapDecisionResult Decide(EngineData data, IReadOnlyList<JsonObject> todayLedger, string today, IReadOnlyList<SapItemInput> items, SapDecisionOptions options)
            {
                Thread.Sleep(1500);
                return new SapDecisionEngine(new Tool1DecisionEngine()).Decide(data, todayLedger, today, items, options);
            }
        }

        [Fact]
        public async Task Past_DecisionTimeoutMs_the_answer_is_503_so_SAP_continues_as_NOT_CHECKED()
        {
            var k = new Kit();
            k.Cfg.DecisionTimeoutMs = 200;
            var sw = Stopwatch.StartNew();
            var r = await k.Service(new SlowEngine()).DecideAsync(SapKit.Request("WI-SLOW", SapKit.Item(10, "1101000101", "WHT", 100)).ToJsonString(), SapKit.Caller);
            Assert.True(sw.ElapsedMilliseconds < 1200, sw.ElapsedMilliseconds + " ms");
            Assert.Equal(503, r.Status);
            Assert.Equal("DECISION_TIMEOUT", Parse(r)["error_code"]!.GetValue<string>());
            await Task.Delay(1800);   // the late engine run finishes: nothing answered 200 is stored for the timed-out call
            Assert.DoesNotContain(k.Repo.Decisions, d => d.Row.HttpStatus == 200);
            Assert.Contains(k.Repo.Decisions, d => d.Row.HttpStatus == 503 && d.Row.ErrorCode == "DECISION_TIMEOUT");
        }

        [Fact]
        public async Task Feed_lag_above_the_limit_sets_data_delayed()
        {
            var k = new Kit();
            k.Cfg.EventsLagLimitSeconds = 60;
            k.Events.Status = new SapEventStatus { LagSeconds = 75 };
            var j = Parse(await k.Service().DecideAsync(SapKit.Request("WI-LAG", SapKit.Item(10, "1101000101", "WHT", 100)).ToJsonString(), SapKit.Caller));
            Assert.True(j["data_delayed"]!.GetValue<bool>());
            Assert.Equal(75, j["data_as_of"]!["events_lag_seconds"]!.GetValue<long>());
        }

        [Fact]
        public async Task LIVE_mode_is_echoed_and_logged()
        {
            var k = new Kit();
            var req = SapKit.Request("WI-LIVE", SapKit.Item(10, "2203000201", "OWH", 100));
            req["mode"] = "LIVE";
            var j = Parse(await k.Service().DecideAsync(req.ToJsonString(), SapKit.Caller));
            Assert.Equal("LIVE", j["mode"]!.GetValue<string>());
            Assert.Equal("HOLD", j["overall_verdict"]!.GetValue<string>());
            Assert.Equal("BLOCK", j["items"]![0]!["reason_code"]!.GetValue<string>());
            Assert.Equal("LIVE", k.Repo.Decisions[0].Row.Mode);
        }

        [Fact]
        public async Task BlockVerdict_BLOCK_in_the_settings_switches_the_answer()
        {
            var k = new Kit();
            k.Cfg.BlockVerdict = "BLOCK";
            var j = Parse(await k.Service().DecideAsync(SapKit.Request("WI-B", SapKit.Item(10, "2203000201", "OWH", 100)).ToJsonString(), SapKit.Caller));
            Assert.Equal("BLOCK", j["overall_verdict"]!.GetValue<string>());
            Assert.Equal("B", j["verdict_code"]!.GetValue<string>());
        }

        [Fact]
        public async Task Without_SQL_Server_storage_the_answer_is_503()
        {
            var k = new Kit();
            k.Repo.IsAvailable = false;
            var r = await k.Service().DecideAsync(SapKit.Request("WI-PG", SapKit.Item(10, "1101000101", "WHT", 100)).ToJsonString(), SapKit.Caller);
            Assert.Equal(503, r.Status);
            Assert.Equal("STORAGE_NOT_SUPPORTED", Parse(r)["error_code"]!.GetValue<string>());
            Assert.Equal("SAP interface requires SQL Server storage", Parse(r)["message"]!.GetValue<string>());
        }

        [Fact]
        public async Task Without_the_data_files_the_answer_is_503_DATA_NOT_LOADED()
        {
            var k = new Kit();
            k.Cache.Data = new EngineData(new Dictionary<string, IReadOnlyList<SheetRow>?>(), new EngineParams(), 0, 0);
            var r = await k.Service().DecideAsync(SapKit.Request("WI-ND", SapKit.Item(10, "1101000101", "WHT", 100)).ToJsonString(), SapKit.Caller);
            Assert.Equal(503, r.Status);
            Assert.Equal("DATA_NOT_LOADED", Parse(r)["error_code"]!.GetValue<string>());
        }

        [Fact]
        public async Task Rules_version_defaults_to_the_parameters_version()
        {
            var k = new Kit();
            k.Cfg.RulesVersion = "";
            var j = Parse(await k.Service().DecideAsync(SapKit.Request("WI-RV", SapKit.Item(10, "1101000101", "WHT", 100)).ToJsonString(), SapKit.Caller));
            Assert.Equal("R-P" + SapKit.Data.ParamsVersion, j["rules_version"]!.GetValue<string>());
        }
    }

    /// <summary>POST /events: envelope 400 / 413, per-event parking, de-duplication by msg_id, seq order flag, 200 only after the store.</summary>
    public class SapEventServiceTests
    {
        private static (SapEventService Svc, InMemorySapRepository Repo) Service(Action<SapInterfaceSettings>? cfg = null)
        {
            var repo = new InMemorySapRepository();
            return (new SapEventService(repo, new FakeAppClock { TodayValue = SapKit.Today }, SapKit.Options(cfg), NullLogger<SapEventService>.Instance), repo);
        }

        private static JsonObject Gr(string msg, long seq, string? article = "000000001101000101") => new()
        {
            ["msg_id"] = msg, ["seq"] = seq, ["event_type"] = "GR_POSTED", ["occurred_at"] = "2026-10-21T11:42:07+05:30", ["sap_user"] = "DCUSER01",
            ["goods_movement"] = new JsonObject
            {
                ["material_doc"] = "5000123456", ["doc_year"] = 2026, ["doc_item"] = 1, ["movement_type"] = "101", ["debit_credit"] = "S",
                ["posting_date"] = "2026-10-21", ["article"] = article, ["quantity"] = 240, ["uom"] = "PC", ["po_number"] = "4500098765", ["po_item"] = 10
            }
        };

        private static JsonObject Po(string msg, long seq, string type = "PO_RELEASED") => new()
        {
            ["msg_id"] = msg, ["seq"] = seq, ["event_type"] = type, ["occurred_at"] = "2026-10-21T10:15:02+05:30",
            ["purchase_order"] = new JsonObject
            {
                ["po_number"] = "4500098765", ["release_status"] = "RELEASED",
                ["items"] = new JsonArray(new JsonObject { ["po_item"] = 10, ["article"] = "000000001101000101", ["order_qty"] = 600, ["uom"] = "PC", ["delivery_date"] = "2026-11-10" })
            }
        };

        private static JsonObject Outcome(string msg, long seq, string outcome = "EXCEPTION_APPROVED") => new()
        {
            ["msg_id"] = msg, ["seq"] = seq, ["event_type"] = "GOV_OUTCOME", ["occurred_at"] = "2026-10-21T15:05:12+05:30",
            ["governance_outcome"] = new JsonObject
            {
                ["decision_id"] = "D-20261021-000001", ["po_number"] = "4500099001", ["po_item"] = 10, ["outcome"] = outcome, ["by_user"] = "CATHEAD03",
                ["by_role"] = "CATHEAD", ["at"] = "2026-10-21T15:05:12+05:30", ["reason_code"] = "FEST"
            }
        };

        private static string Batch(params JsonObject[] events) => new JsonObject
        {
            ["source_system"] = "ECP", ["client"] = "300", ["queue"] = "ZPOGOV_EVENTS", ["sent_at"] = "2026-10-21T11:42:08+05:30",
            ["events"] = new JsonArray(events.Select(e => (JsonNode)e).ToArray())
        }.ToJsonString();

        private static JsonObject Parse(SapCallResult r) => JsonNode.Parse(r.Json)!.AsObject();
        private static string[] Ids(JsonObject j, string key) => j[key]!.AsArray().Select(x => x is JsonObject o ? o["msg_id"]!.GetValue<string>() : x!.GetValue<string>()).ToArray();

        [Fact]
        public async Task Every_event_type_is_accepted_bad_events_are_parked_and_resending_gives_duplicates()
        {
            var (svc, repo) = Service();
            var batch = Batch(Po("PO-1", 1, "PO_CREATED"), Po("PO-2", 2, "PO_CHANGED"), Po("PO-3", 3), Po("PO-4", 4, "PO_RELEASE_REVOKED"), Po("PO-5", 5, "PO_CLOSED"),
                              Po("PO-6", 6, "PO_DELETED"), Gr("GR-1", 7), Gr("GR-BAD", 8, article: null), Outcome("OUT-1", 9), Outcome("OUT-BAD", 10, "MAYBE"));
            var r = await svc.ReceiveAsync(batch, SapKit.Caller);
            Assert.Equal(200, r.Status);
            var j = Parse(r);
            Assert.Equal(new[] { "PO-1", "PO-2", "PO-3", "PO-4", "PO-5", "PO-6", "GR-1", "OUT-1" }, Ids(j, "accepted"));
            Assert.Empty(Ids(j, "duplicates"));
            Assert.Equal(new[] { "GR-BAD", "OUT-BAD" }, Ids(j, "parked"));
            Assert.Equal("goods_movement.article is required", j["parked"]![0]!["message"]!.GetValue<string>());
            Assert.Equal("VALIDATION_FAILED", j["parked"]![0]!["error_code"]!.GetValue<string>());
            Assert.Equal(10, repo.Events.Count);
            Assert.All(repo.Events, e => Assert.False(e.Applied));     // applying to the live position is pending
            Assert.Equal("D-20261021-000001", repo.Events.Single(e => e.MsgId == "OUT-1").DecisionId);

            var again = Parse(await svc.ReceiveAsync(batch, SapKit.Caller));
            Assert.Empty(Ids(again, "accepted"));
            Assert.Equal(10, Ids(again, "duplicates").Length);
            Assert.Empty(Ids(again, "parked"));
            Assert.Equal(10, repo.Events.Count);
        }

        [Fact]
        public async Task Other_movement_types_and_return_events()
        {
            var (svc, repo) = Service();
            var rev = Gr("GR-REV", 1); rev["event_type"] = "GR_REVERSED"; rev["goods_movement"]!["movement_type"] = "102"; rev["goods_movement"]!["reversal_of"] = "5000123456";
            var ret = Gr("RT-1", 2); ret["event_type"] = "RETURN_POSTED"; ret["goods_movement"]!["movement_type"] = "122"; ret["goods_movement"]!["debit_credit"] = "H";
            var retRev = Gr("RT-REV", 3); retRev["event_type"] = "RETURN_REVERSED"; retRev["goods_movement"]!["movement_type"] = "161";
            var j = Parse(await svc.ReceiveAsync(Batch(rev, ret, retRev), SapKit.Caller));
            Assert.Equal(3, Ids(j, "accepted").Length);
        }

        public static IEnumerable<object[]> ParkedEvents()
        {
            JsonObject With(JsonObject e, Action<JsonObject> f) { f(e); return e; }
            yield return new object[] { With(Gr("P1", 1), e => e.Remove("seq")), "seq is required" };
            yield return new object[] { With(Gr("P2", 1), e => e["event_type"] = "PO_SHIPPED"), "event_type PO_SHIPPED is not one of" };
            yield return new object[] { With(Gr("P3", 1), e => e["occurred_at"] = "2026-10-21 11:42"), "occurred_at must be an ISO 8601 timestamp" };
            yield return new object[] { With(Gr("P4", 1), e => e.Remove("goods_movement")), "GR_POSTED needs goods_movement" };
            yield return new object[] { With(Gr("P5", 1), e => e["goods_movement"]!["debit_credit"] = "X"), "goods_movement.debit_credit must be S or H" };
            yield return new object[] { With(Gr("P6", 1), e => e["goods_movement"]!["quantity"] = -1), "goods_movement.quantity must be positive" };
            yield return new object[] { With(Gr("P7", 1), e => e["purchase_order"] = new JsonObject { ["po_number"] = "1", ["items"] = new JsonArray() }), "exactly one of purchase_order" };
            yield return new object[] { With(Po("P8", 1), e => e["purchase_order"]!["release_status"] = "DONE"), "purchase_order.release_status must be one of" };
            yield return new object[] { With(Po("P9", 1), e => e["purchase_order"]!.AsObject().Remove("items")), "purchase_order.items is required" };
            yield return new object[] { With(Po("P10", 1), e => e["purchase_order"]!["items"]![0]!.AsObject().Remove("uom")), "purchase_order.items[0].uom is required" };
            yield return new object[] { With(Outcome("P11", 1), e => e["governance_outcome"]!["by_role"] = "CEO"), "governance_outcome.by_role must be one of" };
            yield return new object[] { With(Outcome("P12", 1), e => e["governance_outcome"]!["note"] = new string('n', 501)), "governance_outcome.note must be at most 500" };
            yield return new object[] { With(Gr("P13", 1), e => e["seq"] = "abc"), "Invalid field value" };
        }

        [Theory]
        [MemberData(nameof(ParkedEvents))]
        public async Task An_invalid_event_is_parked_and_never_blocks_the_batch(JsonObject bad, string message)
        {
            var (svc, repo) = Service();
            var j = Parse(await svc.ReceiveAsync(Batch(Gr("OK-1", 100), bad, Gr("OK-2", 101)), SapKit.Caller));
            Assert.Equal(new[] { "OK-1", "OK-2" }, Ids(j, "accepted"));
            var parked = Assert.Single(j["parked"]!.AsArray());
            Assert.StartsWith(message, parked!["message"]!.GetValue<string>());
            Assert.Equal("PARKED", repo.Events[1].Status);
        }

        [Fact]
        public async Task An_event_without_msg_id_is_parked_under_a_generated_id()
        {
            var (svc, _) = Service();
            var e = Gr("x", 1); e.Remove("msg_id");
            var j = Parse(await svc.ReceiveAsync(Batch(e), SapKit.Caller));
            Assert.StartsWith("NOID-B-", Ids(j, "parked")[0]);
            Assert.Equal("msg_id is required", j["parked"]![0]!["message"]!.GetValue<string>());
        }

        [Fact]
        public async Task A_seq_below_the_last_accepted_is_stored_LATE()
        {
            var (svc, repo) = Service();
            await svc.ReceiveAsync(Batch(Gr("A", 10), Gr("B", 20)), SapKit.Caller);
            var j = Parse(await svc.ReceiveAsync(Batch(Gr("C", 15), Gr("D", 30)), SapKit.Caller));
            Assert.Equal(new[] { "C", "D" }, Ids(j, "accepted"));
            Assert.Equal(new[] { "IN_ORDER", "IN_ORDER", "LATE", "IN_ORDER" }, repo.Events.Select(e => e.SeqStatus));
        }

        [Theory]
        [InlineData("{\"client\":\"300\",\"events\":[{}]}", 400, "source_system is required")]
        [InlineData("{\"source_system\":\"ECP\",\"events\":[{}]}", 400, "client is required")]
        [InlineData("{\"source_system\":\"ECP\",\"client\":\"300\"}", 400, "events is required")]
        [InlineData("{\"source_system\":\"ECP\",\"client\":\"300\",\"events\":[]}", 400, "events must contain at least one event")]
        [InlineData("{\"source_system\":\"ECP\",\"client\":\"300\",\"sent_at\":\"now\",\"events\":[{}]}", 400, "sent_at must be")]
        [InlineData("[1,2]", 400, "Invalid JSON")]
        public async Task A_malformed_batch_is_400(string body, int status, string message)
        {
            var (svc, repo) = Service();
            var ex = await Assert.ThrowsAsync<SapApiException>(() => svc.ReceiveAsync(body, SapKit.Caller));
            Assert.Equal(status, ex.Status);
            Assert.StartsWith(message, ex.Message);
            Assert.Empty(repo.Events);
        }

        [Fact]
        public async Task More_than_500_events_is_413()
        {
            var (svc, repo) = Service();
            var ex = await Assert.ThrowsAsync<SapApiException>(() => svc.ReceiveAsync(Batch(Enumerable.Range(1, 501).Select(i => Gr("M" + i, i)).ToArray()), SapKit.Caller));
            Assert.Equal(413, ex.Status);
            Assert.Equal("BATCH_TOO_LARGE", ex.ErrorCode);
            var ok = await svc.ReceiveAsync(Batch(Enumerable.Range(1, 500).Select(i => Gr("M" + i, i)).ToArray()), SapKit.Caller);
            Assert.Equal(500, Parse(ok)["accepted"]!.AsArray().Count);
        }
    }

    /// <summary>Basic-auth of the technical users: PBKDF2 check, lockout, inactive users, audit lines.</summary>
    public class SapAuthServiceTests
    {
        private sealed class Audit : Application.Interfaces.V1.IAuditService
        {
            public readonly List<(string Who, string Action, string Detail)> Lines = new();
            public Task AppendAsync(string who, string action, string detail = "", CancellationToken ct = default) { Lines.Add((who, action, detail)); return Task.CompletedTask; }
            public Task<Application.DTOs.Platform.AuditQueryResponse> QueryAsync(string? from, string? to, string? q, CancellationToken ct = default) => throw new NotSupportedException();
        }

        private static (SapAuthService Svc, InMemorySapRepository Repo, Audit Audit) Service()
        {
            var repo = new InMemorySapRepository();
            repo.Users.Add(new SapTechUser { Id = 1, UserName = "sap_ecp", SourceSystem = "ECP", PasswordHash = "plain$right-password-of-24-chars!", Active = true });
            var audit = new Audit();
            var svc = new SapAuthService(repo, new PlainHasher(), audit, repo.Clock, SapKit.Options(c => { c.MaxFailedLogins = 3; c.LockoutMinutes = 15; }),
                                         NullLogger<SapAuthService>.Instance);
            return (svc, repo, audit);
        }

        [Fact]
        public async Task Right_password_authenticates_and_records_the_call()
        {
            var (svc, repo, _) = Service();
            var r = await svc.AuthenticateAsync("SAP_ECP", "right-password-of-24-chars!", "10.0.0.5");
            Assert.True(r.Ok);
            Assert.Equal("ECP", r.User!.SourceSystem);
            Assert.Equal("10.0.0.5", repo.Users[0].LastCallIp);
            await svc.AuthenticateAsync("sap_ecp", "right-password-of-24-chars!", "10.0.0.5");
            Assert.Equal(1, repo.LoginOkCalls);   // the last-call stamp is written at most once a minute
        }

        [Fact]
        public async Task Wrong_passwords_lock_the_user_and_are_audited()
        {
            var (svc, repo, audit) = Service();
            for (var i = 0; i < 3; i++) Assert.Equal("UNAUTHORIZED", (await svc.AuthenticateAsync("sap_ecp", "wrong", "10.0.0.5")).ErrorCode);
            var locked = await svc.AuthenticateAsync("sap_ecp", "right-password-of-24-chars!", "10.0.0.5");
            Assert.False(locked.Ok);
            Assert.Equal("USER_LOCKED", locked.ErrorCode);
            Assert.Equal(new[] { "sap-login-fail", "sap-login-fail", "sap-locked" }, audit.Lines.Select(l => l.Action));
            await repo.UnlockTechUserAsync("sap_ecp");
            Assert.True((await svc.AuthenticateAsync("sap_ecp", "right-password-of-24-chars!", "10.0.0.5")).Ok);
        }

        [Fact]
        public async Task Unknown_and_disabled_users_are_refused()
        {
            var (svc, repo, audit) = Service();
            Assert.False((await svc.AuthenticateAsync("sap_xxx", "whatever", "1.2.3.4")).Ok);
            repo.Users[0].Active = false;
            Assert.False((await svc.AuthenticateAsync("sap_ecp", "right-password-of-24-chars!", "1.2.3.4")).Ok);
            Assert.Equal(2, audit.Lines.Count);
            Assert.False((await svc.AuthenticateAsync("", "", "1.2.3.4")).Ok);
        }

        [Fact]
        public async Task A_password_reset_invalidates_the_remembered_check()
        {
            var (svc, repo, _) = Service();
            Assert.True((await svc.AuthenticateAsync("sap_ecp", "right-password-of-24-chars!", "1.2.3.4")).Ok);
            repo.Users[0].PasswordHash = "plain$a-new-password-of-24-chars!!";
            Assert.False((await svc.AuthenticateAsync("sap_ecp", "right-password-of-24-chars!", "1.2.3.4")).Ok);
            Assert.True((await svc.AuthenticateAsync("sap_ecp", "a-new-password-of-24-chars!!", "1.2.3.4")).Ok);
        }
    }

    /// <summary>The portal side (/admin/sap): technical users, reason codes, test decision, ping / reason-codes.</summary>
    public class SapAdminServiceTests
    {
        private sealed class NoAudit : Application.Interfaces.V1.IAuditService
        {
            public Task AppendAsync(string who, string action, string detail = "", CancellationToken ct = default) => Task.CompletedTask;
            public Task<Application.DTOs.Platform.AuditQueryResponse> QueryAsync(string? from, string? to, string? q, CancellationToken ct = default) => throw new NotSupportedException();
        }

        private static (SapAdminService Svc, InMemorySapRepository Repo) Service()
        {
            var repo = new InMemorySapRepository();
            var clock = new FakeAppClock { TodayValue = SapKit.Today };
            var cache = new FakeDatasetCache { Data = SapKit.Data };
            var store = SapKit.Store();
            var opts = SapKit.Options();
            var decisions = new SapDecisionService(repo, cache, store, clock, new SapDecisionEngine(new Tool1DecisionEngine()), new FakeEventStatus(), opts,
                                                   NullLogger<SapDecisionService>.Instance);
            return (new SapAdminService(repo, decisions, new FakeEventStatus(), new PlainHasher(), new NoAudit(), clock, cache, store, opts), repo);
        }

        [Fact]
        public async Task Technical_users_add_generate_reset_enable_disable()
        {
            var (svc, repo) = Service();
            var gen = await svc.AddTechUserAsync(new SapTechUserAddReq { UserName = "sap_ecp", SourceSystem = "ecp" }, "admin");
            Assert.Equal(32, gen.GeneratedPassword!.Length);
            Assert.Equal("ECP", gen.User!.SourceSystem);
            Assert.Equal("plain$" + gen.GeneratedPassword, repo.Users[0].PasswordHash);
            await Assert.ThrowsAsync<ConflictException>(() => svc.AddTechUserAsync(new SapTechUserAddReq { UserName = "SAP_ECP", SourceSystem = "ECP" }, "admin"));
            await Assert.ThrowsAsync<AppValidationException>(() => svc.AddTechUserAsync(new SapTechUserAddReq { UserName = "x", SourceSystem = "ECQ" }, "admin"));
            await Assert.ThrowsAsync<AppValidationException>(() => svc.AddTechUserAsync(new SapTechUserAddReq { UserName = "sap_ecq", SourceSystem = "ECQ", Password = "too-short" }, "admin"));
            var given = await svc.AddTechUserAsync(new SapTechUserAddReq { UserName = "sap_ecq", SourceSystem = "ECQ", Password = new string('p', 24) }, "admin");
            Assert.Null(given.GeneratedPassword);
            Assert.False((await svc.UpdateTechUserAsync(new SapTechUserUpdateReq { UserName = "sap_ecq", Action = "disable" }, "admin")).User!.Active);
            Assert.True((await svc.UpdateTechUserAsync(new SapTechUserUpdateReq { UserName = "sap_ecq", Action = "enable" }, "admin")).User!.Active);
            var reset = await svc.UpdateTechUserAsync(new SapTechUserUpdateReq { UserName = "sap_ecq", Action = "reset-password" }, "admin");
            Assert.Equal("plain$" + reset.GeneratedPassword, repo.Users[1].PasswordHash);
            await Assert.ThrowsAsync<NotFoundException>(() => svc.UpdateTechUserAsync(new SapTechUserUpdateReq { UserName = "nobody", Action = "unlock" }, "admin"));
            await Assert.ThrowsAsync<AppValidationException>(() => svc.UpdateTechUserAsync(new SapTechUserUpdateReq { UserName = "sap_ecq", Action = "delete" }, "admin"));
        }

        [Fact]
        public async Task Exception_reason_codes_are_checked_against_the_spec_limits()
        {
            var (svc, repo) = Service();
            var row = await svc.SaveReasonCodeAsync(new SapReasonCodeSaveReq { Code = "mrkd", Text = "Markdown clearance" }, "planner");
            Assert.Equal("MRKD", row.Code);
            await Assert.ThrowsAsync<AppValidationException>(() => svc.SaveReasonCodeAsync(new SapReasonCodeSaveReq { Code = "TOOLONG", Text = "x" }, "p"));
            await Assert.ThrowsAsync<AppValidationException>(() => svc.SaveReasonCodeAsync(new SapReasonCodeSaveReq { Code = "ABCD", Text = new string('t', 61) }, "p"));
            var util = new SapUtilityService(repo, new FakeAppClock { TodayValue = SapKit.Today }, SapKit.Options());
            var codes = await util.ReasonCodesAsync();
            Assert.Equal("2026-10-01", codes.Version);
            Assert.Equal(new[] { "NEWL", "OTHR", "MRKD" }, codes.ReasonCodes.Select(c => c.Code));   // inactive OLD left out
        }

        [Fact]
        public async Task Ping_names_the_recognised_SAP_system()
        {
            var util = new SapUtilityService(new InMemorySapRepository(), new FakeAppClock { TodayValue = SapKit.Today }, SapKit.Options(c => c.Environment = "production"));
            var p = await util.PingAsync(SapKit.Caller);
            Assert.Equal("ok", p.Status);
            Assert.Equal("PRODUCTION", p.Environment);
            Assert.Equal("ECQ", p.SourceSystemRecognised);
            Assert.Equal("2026-10-21T10:00:00+05:30", p.ServerTime);
        }

        [Fact]
        public async Task Test_decision_runs_the_real_path_with_a_TEST_request_id()
        {
            var (svc, repo) = Service();
            var sample = await svc.SampleRequestAsync();
            Assert.Contains("\"mode\": \"SHADOW\"", sample);
            var res = await svc.TestDecisionAsync(new SapTestDecisionReq { RequestJson = SapKit.Request("WI-77", SapKit.Item(10, "1101000101", "WHT", 100)).ToJsonString() }, "admin@x", "10.1.1.1");
            Assert.Equal(200, res.HttpStatus);
            Assert.Contains("\"request_id\":\"TEST-WI-77\"", res.ResponseJson);
            var row = Assert.Single(repo.Decisions).Row;
            Assert.True(row.IsTest);
            Assert.Equal("portal:admin@x", row.TechUser);
            var fromSample = await svc.TestDecisionAsync(new SapTestDecisionReq(), "admin@x", "10.1.1.1");
            Assert.Equal(200, fromSample.HttpStatus);
        }
    }
}
