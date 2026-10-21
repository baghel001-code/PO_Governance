using System.Diagnostics;
using System.Text.Json.Nodes;
using Dapper;
using Domain.Entities.Store;
using Infrastructure.Repositories;
using Infrastructure.Utilitys;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;

namespace POGovernance.Tests.SqlServer
{
    /// <summary>MyNewEncDec.dll (.NET Framework 4.5, from QPS) on net8 + the DefaultConnection password decoding. No database needed.</summary>
    public class EncodedPasswordTests
    {
        [Fact]
        public void MyNewEncDec_loads_on_net8_and_round_trips()
        {
            foreach (var plain in new[] { "DevPass#2026", "1234", "P@ss;word=x" })
            {
                var enc = DapperHelper.EncodePassword(plain);
                Assert.NotEqual(plain, enc);
                var cs = DapperHelper.DecodePassword(new SqlConnectionStringBuilder { DataSource = "x", Password = enc }.ConnectionString);
                Assert.Equal(plain, new SqlConnectionStringBuilder(cs).Password);
            }
            Assert.Equal("5VdI7VrKjcxcxpTI", DapperHelper.EncodePassword("DevPass#2026"));   // stable, as QPS stores it
        }

        [Fact]
        public void DefaultConnection_password_is_decoded_only_when_EncryptedDbPassword_is_true()
        {
            IConfiguration Cfg(bool enc) => new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = "Server=db1;Database=POGovernance;User Id=vmm_app;Password=5VdI7VrKjcxcxpTI;TrustServerCertificate=True",
                ["PoGovSettings:EncryptedDbPassword"] = enc ? "true" : "false"
            }).Build();
            var on = new SqlConnectionStringBuilder(DapperHelper.BuildConnectionString(Cfg(true)));
            var off = new SqlConnectionStringBuilder(DapperHelper.BuildConnectionString(Cfg(false)));
            Assert.Equal("DevPass#2026", on.Password);
            Assert.Equal("5VdI7VrKjcxcxpTI", off.Password);
            Assert.Equal("vmm-po-governance", new SqlConnectionStringBuilder(new DapperHelper(on.ConnectionString).ConnectionString).ApplicationName);
            Assert.Equal("SQL Server vmm_app@db1/POGovernance", new SqlServerStore(on.ConnectionString).Describe());
        }

        [Fact]
        public void Script_batches_split_on_GO_lines()
        {
            var batches = SqlServerSchema.SplitBatches("SELECT 1;\nGO\n/* only a comment */\nGO\nSELECT 'GO';\n  go  \n");
            Assert.Equal(new[] { "SELECT 1;", "SELECT 'GO';" }, batches);
            Assert.True(SqlServerSchema.SplitBatches(SqlServerSchema.ReadScript("02_procedures.sql")).Count >= 15);
        }
    }

    /// <summary>The platform-level store scenarios against a real SQL Server (skipped without POGOV_SQLSERVER_TEST).</summary>
    public class SqlServerStoreTests : IClassFixture<SqlServerFixture>
    {
        private readonly SqlServerFixture _fx;

        public SqlServerStoreTests(SqlServerFixture fx) => _fx = fx;

        private async Task<SqlServerStore> FreshAsync()
        {
            var s = _fx.NewStore();
            await s.InitializeAsync();
            await s.TruncateAllAsync();
            return s;
        }

        private async Task<T> Scalar<T>(string sql, object? p = null)
        {
            await using var c = new SqlConnection(_fx.ConnectionString);
            return await c.ExecuteScalarAsync<T>(sql, p) ?? throw new InvalidOperationException("null");
        }

        [SqlServerFact]
        public async Task Initialize_checks_the_schema_and_kind()
        {
            using var s = _fx.NewStore();
            await s.InitializeAsync();
            Assert.Equal("sqlserver", s.Kind);
            Assert.Equal("3", await Scalar<string>("SELECT value FROM schema_info WHERE [key] = 'version'"));
            Assert.True(await s.StorageBytesAsync() > 1_000_000);

            // a database without the scripts is refused with instructions (unless createSchema)
            var b = new SqlConnectionStringBuilder(_fx.ConnectionString) { InitialCatalog = "POGovTest_" + Guid.NewGuid().ToString("N")[..10] };
            await SqlServerSchema.CreateDatabaseAsync(b.ConnectionString);
            try
            {
                var e = await Assert.ThrowsAsync<InvalidOperationException>(() => new SqlServerStore(b.ConnectionString).InitializeAsync());
                Assert.Contains("01_tables.sql", e.Message);
                using var auto = new SqlServerStore(b.ConnectionString, createSchema: true);
                await auto.InitializeAsync();
                Assert.True(await auto.IsEmptyAsync());
            }
            finally { await SqlServerSchema.DropDatabaseAsync(b.ConnectionString); }
        }

        [SqlServerFact]
        public async Task Documents_keep_python_json_text_and_stamps()
        {
            using var s = await FreshAsync();
            Assert.True(await s.IsEmptyAsync());
            Assert.Null(await s.GetDocAsync(DocNames.Manifest));
            Assert.Null(await s.GetDocAsync(DocNames.Users));
            Assert.Equal(0, await s.DocStampAsync(DocNames.Manifest));

            var doc = JsonNode.Parse("{\"datasets\":{\"bsr\":{\"current\":\"v1\",\"versions\":[{\"v\":\"v1\",\"rows\":12,\"x\":2.5}]}},\"name\":\"ü – ₹\"}")!;
            await s.PutDocAsync(DocNames.Manifest, doc);
            Assert.Equal(1, await s.DocStampAsync(DocNames.Manifest));
            Assert.Equal(PyJson.Dumps(doc), await Scalar<string>("SELECT value FROM app_settings WHERE name = 'manifest'"));
            Assert.Equal("{\"datasets\": {\"bsr\": {\"current\": \"v1\", \"versions\": [{\"v\": \"v1\", \"rows\": 12, \"x\": 2.5}]}}, \"name\": \"ü – ₹\"}",
                PyJson.Dumps(await s.GetDocAsync(DocNames.Manifest)));
            await s.PutDocAsync(DocNames.Manifest, new JsonObject { ["a"] = 1 });
            Assert.Equal(2, await s.DocStampAsync(DocNames.Manifest));
            Assert.False(await s.IsEmptyAsync());

            // an empty map document exists once written ({} rather than null)
            await s.PutDocAsync(DocNames.Sessions, new JsonObject());
            Assert.Equal("{}", PyJson.Dumps(await s.GetDocAsync(DocNames.Sessions)));
        }

        private static JsonObject User(string email, string name, bool active, params string[] roles) => new()
        {
            ["email"] = email, ["name"] = name, ["roles"] = new JsonArray(roles.Select(r => (JsonNode)r).ToArray()), ["active"] = active,
            ["pw"] = "salt$hash", ["mustChange"] = false, ["failed"] = 0, ["lockedUntil"] = 0
        };

        [SqlServerFact]
        public async Task Users_map_writes_only_changed_rows()
        {
            using var s = await FreshAsync();
            var users = new JsonObject
            {
                ["a.one@x.com"] = User("a.one@x.com", "A One", true, "admin", "planner"),
                ["B.two@x.com"] = User("B.two@x.com", "B Two", true, "buyer"),
                ["b.two@x.com"] = User("b.two@x.com", "b two", false, "cathead"),   // keys are case-sensitive (as in PostgreSQL)
            };
            await s.PutDocAsync(DocNames.Users, users);
            Assert.Equal(3, await Scalar<int>("SELECT COUNT(*) FROM app_users"));
            Assert.Equal("admin,planner", await Scalar<string>("SELECT roles FROM app_users WHERE email = 'a.one@x.com'"));
            Assert.False(await Scalar<bool>("SELECT active FROM app_users WHERE email = 'b.two@x.com'"));
            await s.Db.ExecuteAsync("UPDATE app_users SET updated_at = '2001-01-01T00:00:00+00:00'");

            var u2 = (JsonObject)(await s.GetDocAsync(DocNames.Users))!;
            Assert.Equal(new[] { "B.two@x.com", "a.one@x.com", "b.two@x.com" }, u2.Select(kv => kv.Key).ToArray());   // ordered by key (code points)
            u2["a.one@x.com"]!["failed"] = 1;
            u2.Remove("B.two@x.com");
            await s.PutDocAsync(DocNames.Users, u2);

            Assert.Equal(2, await Scalar<int>("SELECT COUNT(*) FROM app_users"));
            Assert.Equal(1, await Scalar<int>("SELECT COUNT(*) FROM app_users WHERE updated_at > '2002-01-01'"));   // only the changed row
            Assert.Equal(2001, await Scalar<int>("SELECT YEAR(updated_at) FROM app_users WHERE email = 'b.two@x.com'"));
            Assert.Equal(PyJson.Dumps(u2["a.one@x.com"]), await Scalar<string>("SELECT data FROM app_users WHERE email = 'a.one@x.com'"));
            Assert.True(StoreCopy.PyEquals(u2, await _fx.NewStore().GetDocAsync(DocNames.Users)));
            Assert.Equal(2, await s.DocStampAsync(DocNames.Users));

            // sessions: token -> {email, created, seen}
            await s.PutDocAsync(DocNames.Sessions, new JsonObject { ["tok1"] = new JsonObject { ["email"] = "a.one@x.com", ["created"] = 1.5, ["seen"] = 2 } });
            Assert.Equal("a.one@x.com", await Scalar<string>("SELECT email FROM app_sessions WHERE token = 'tok1'"));
        }

        [SqlServerFact]
        public async Task Map_written_by_another_instance_is_reread_before_the_diff()
        {
            using var a = await FreshAsync();
            using var b = _fx.NewStore();
            await a.PutDocAsync(DocNames.Users, new JsonObject { ["u1@x.com"] = User("u1@x.com", "U1", true, "buyer") });
            var mine = (JsonObject)(await a.GetDocAsync(DocNames.Users))!;           // a caches the rows

            var theirs = (JsonObject)(await b.GetDocAsync(DocNames.Users))!;         // another API instance adds u2
            theirs["u2@x.com"] = User("u2@x.com", "U2", true, "buyer");
            await b.PutDocAsync(DocNames.Users, theirs);

            mine["u1@x.com"]!["failed"] = 3;                                          // a writes its whole document
            await a.PutDocAsync(DocNames.Users, mine);
            var stored = await _fx.NewStore().GetDocAsync(DocNames.Users);
            Assert.True(StoreCopy.PyEquals(mine, stored));                            // exactly a's document (u2 removed, as a full write)
            Assert.Equal(3, await a.DocStampAsync(DocNames.Users));
        }

        private static JsonObject Ev(int seq, string type, string reqId) =>
            type == "request"
                ? new JsonObject { ["type"] = type, ["seq"] = seq, ["request"] = new JsonObject { ["reqId"] = reqId, ["qty"] = seq * 10 } }
                : new JsonObject { ["type"] = type, ["seq"] = seq, ["reqId"] = reqId, ["status"] = "approved" };

        [SqlServerFact]
        public async Task Logs_append_rewrite_days()
        {
            using var s = await FreshAsync();
            for (var i = 1; i <= 3; i++)
            {
                await s.AppendLogAsync(LogStreams.Uploads, new JsonObject { ["n"] = i, ["file"] = "f" + i + ".xlsx" });
                await s.AppendLogAsync(LogStreams.Batches, new JsonObject { ["uploadId"] = $"UPL-20261014-000{i}" });
                await s.AppendLogAsync(LogStreams.Feedback, new JsonObject { ["id"] = $"FB-20261014-000{i}", ["message"] = "hi \"there\"\n" });
            }
            await s.AppendLogAsync(LogStreams.Runs, new JsonObject { ["id"] = "RUN-20261014-0001" });
            Assert.Equal(new[] { 1, 2, 3 }, (await s.ReadLogAsync(LogStreams.Uploads)).Select(r => r["n"]!.GetValue<int>()));
            Assert.Equal("UPL-20261014-0002", await Scalar<string>("SELECT upload_id FROM upload_batches ORDER BY id OFFSET 1 ROWS FETCH NEXT 1 ROWS ONLY"));
            Assert.Equal("{\"id\": \"FB-20261014-0001\", \"message\": \"hi \\\"there\\\"\\n\"}", PyJson.Dumps((await s.ReadLogAsync(LogStreams.Feedback))[0]));
            Assert.Equal("RUN-20261014-0001", await Scalar<string>("SELECT run_id FROM run_index"));

            await s.AppendLogAsync(LogStreams.Requests, Ev(1, "request", "REQ-20261014-0001"), "2026-10-14");
            await s.AppendLogAsync(LogStreams.Requests, Ev(2, "approval", "REQ-20261014-0001"), "2026-10-14");
            await s.AppendLogAsync(LogStreams.Requests, Ev(1, "request", "REQ-20261013-0001"), "2026-10-13");
            Assert.Equal("REQ-20261014-0001", await Scalar<string>("SELECT req_id FROM request_events WHERE seq = 2"));
            Assert.Equal("approval", await Scalar<string>("SELECT event_type FROM request_events WHERE seq = 2"));
            Assert.Equal(new[] { "2026-10-13", "2026-10-14" }, await s.LogDaysAsync());
            Assert.Empty(await s.ReadLogAsync(LogStreams.Requests));                       // no day = nothing (as PgStore)

            // rewrite one day (500 events, order kept); the other day is untouched
            var many = Enumerable.Range(1, 500).Select(i => Ev(i, i % 3 == 0 ? "approval" : "request", $"REQ-20261014-{i:0000}")).ToList();
            await s.RewriteLogAsync(LogStreams.Requests, many, "2026-10-14");
            var back = await s.ReadLogAsync(LogStreams.Requests, "2026-10-14");
            Assert.Equal(Enumerable.Range(1, 500), back.Select(r => r["seq"]!.GetValue<int>()));
            Assert.Equal(PyJson.Dumps(many[41]), PyJson.Dumps(back[41]));

            var byDay = await s.ReadLogsAsync(LogStreams.Requests, new[] { "2026-10-13", "2026-10-14", "2026-10-15", "2026-10-13" });
            Assert.Equal(3, byDay.Count);
            Assert.Single(byDay["2026-10-13"]);
            Assert.Equal(500, byDay["2026-10-14"].Count);
            Assert.Empty(byDay["2026-10-15"]);

            await s.RewriteLogAsync(LogStreams.Uploads, new[] { new JsonObject { ["n"] = 9 } });
            Assert.Single(await s.ReadLogAsync(LogStreams.Uploads));
            await s.RewriteLogAsync(LogStreams.Feedback, Array.Empty<JsonObject>());
            Assert.Empty(await s.ReadLogAsync(LogStreams.Feedback));

            await s.DeleteLogDayAsync(LogStreams.Requests, "2026-10-13");
            Assert.Equal(new[] { "2026-10-14" }, await s.LogDaysAsync());
            await Assert.ThrowsAsync<ArgumentException>(() => s.ReadLogAsync("nope"));
        }

        [SqlServerFact]
        public async Task Audit_append_read_and_bulk()
        {
            using var s = await FreshAsync();
            await s.AuditAppendAsync("2026-10-14T10:30:00+05:30", "admin.user@x.com", "login", "{\"ip\": \"127.0.0.1\"}");
            await s.AuditAppendManyAsync(Enumerable.Range(1, 4500).Select(i => new AuditLine("t" + i, "w", "a", i % 2 == 0 ? null : "d" + i)).ToList());
            var lines = await s.AuditReadAsync();
            Assert.Equal(4501, lines.Count);
            Assert.Equal(new AuditLine("2026-10-14T10:30:00+05:30", "admin.user@x.com", "login", "{\"ip\": \"127.0.0.1\"}"), lines[0]);
            Assert.Equal(new AuditLine("t4500", "w", "a", null), lines[^1]);
        }

        [SqlServerFact]
        public async Task Blobs_datasets_runs_and_a_20_MB_blob()
        {
            using var s = await FreshAsync();
            var t0 = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000.0;
            await s.PutBlobAsync(BlobKinds.Dataset, "bsr", "v2", new byte[] { 1, 2, 3 });
            await s.PutBlobAsync(BlobKinds.Dataset, "bsr", "v1", new byte[] { 9 });
            await s.PutBlobAsync(BlobKinds.Dataset, "attrrel", "v1", new byte[] { 7, 7 });
            await s.PutBlobAsync(BlobKinds.Dataset, "bsr", "v2", new byte[] { 4, 5 });          // replace
            Assert.Equal(new byte[] { 4, 5 }, await s.GetBlobAsync(BlobKinds.Dataset, "bsr", "v2"));
            Assert.Equal(2, await s.BlobSizeAsync(BlobKinds.Dataset, "bsr", "v2"));
            Assert.Null(await s.BlobSizeAsync(BlobKinds.Dataset, "bsr", "v9"));
            Assert.False(await s.BlobExistsAsync(BlobKinds.Dataset, "BSR", "v2"));
            var ds = await s.ListBlobsAsync(BlobKinds.Dataset);
            Assert.Equal(new[] { "attrrel/v1", "bsr/v1", "bsr/v2" }, ds.Select(b => b.Key + "/" + b.Version));
            Assert.All(ds, b => Assert.InRange(b.Mtime, t0 - 120, t0 + 120));

            await s.PutBlobAsync(BlobKinds.Run, "ignored", "RUN-1", new byte[] { 1 });
            await s.MarkBlobDeletedAsync(BlobKinds.Run, "", "RUN-1");
            Assert.Null(await s.GetBlobAsync(BlobKinds.Run, "", "RUN-1"));
            Assert.False(await s.BlobExistsAsync(BlobKinds.Run, "", "RUN-1"));
            Assert.Equal(new byte[] { 1 }, await s.GetBlobAnyAsync(BlobKinds.Run, "", "RUN-1", true));
            Assert.Null(await s.GetBlobAnyAsync(BlobKinds.Run, "", "RUN-1", false));
            Assert.Equal(new BlobInfo("", "RUN-1", true, 0) with { Mtime = 0 }, (await s.ListBlobsAsync(BlobKinds.Run)).Single() with { Mtime = 0 });
            await s.DeleteBlobAsync(BlobKinds.Run, "", "RUN-1");
            Assert.Empty(await s.ListBlobsAsync(BlobKinds.Run));
            await s.DeleteBlobAsync(BlobKinds.Dataset, "bsr", "v1");
            Assert.Equal(2, (await s.ListBlobsAsync(BlobKinds.Dataset)).Count);

            // migration keeps created_at
            var when = new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.FromHours(5.5));
            await s.PutBlobAsync(BlobKinds.Run, "", "RUN-OLD", new byte[] { 2 }, false, when);
            Assert.Equal(when.ToUnixTimeSeconds(), (await s.ListBlobsAsync(BlobKinds.Run)).Single().Mtime, 3);

            var big = new byte[20 * 1024 * 1024];
            new Random(42).NextBytes(big);
            var sw = Stopwatch.StartNew();
            await s.PutBlobAsync(BlobKinds.Dataset, "artmaster", "big", big);
            var got = await s.GetBlobAsync(BlobKinds.Dataset, "artmaster", "big");
            Assert.True(big.AsSpan().SequenceEqual(got));
            Assert.Equal(big.Length, await s.BlobSizeAsync(BlobKinds.Dataset, "artmaster", "big"));
            Assert.True(sw.Elapsed < TimeSpan.FromSeconds(60));
        }

        [SqlServerFact]
        public async Task Lock_is_exclusive_across_store_instances()
        {
            using var a = _fx.NewStore();
            using var b = _fx.NewStore();
            b.LockWaitSliceMs = 300;

            var ha = await a.LockAsync();
            Assert.True(await a.HoldsLockAsync());
            var pending = b.LockAsync();
            await Task.Delay(1500);
            Assert.False(pending.IsCompleted);                 // b waits for a (another "API instance")
            ha.Dispose();
            using (await pending.WaitAsync(TimeSpan.FromSeconds(10)))
            {
                Assert.True(await b.HoldsLockAsync());
                Assert.False(await a.HoldsLockAsync());
            }

            // the lock dies with its session: a store that is disposed while holding it frees it
            var c = _fx.NewStore();
            _ = await c.LockAsync();
            c.Dispose();
            using (await b.LockAsync().WaitAsync(TimeSpan.FromSeconds(10))) { }

            // a cancelled wait gives up cleanly
            using (await a.LockAsync())
            {
                using var cts = new CancellationTokenSource(700);
                await Assert.ThrowsAnyAsync<OperationCanceledException>(() => b.LockAsync(cts.Token));
            }
            using (await b.LockAsync().WaitAsync(TimeSpan.FromSeconds(10))) { }
        }

        [SqlServerFact]
        public async Task Read_modify_write_under_the_lock_from_two_instances()
        {
            using var s = await FreshAsync();
            await s.PutDocAsync(DocNames.Params, new JsonObject { ["n"] = 0 });
            var stores = new[] { _fx.NewStore(), _fx.NewStore() };
            await Task.WhenAll(stores.Select(st => Task.Run(async () =>
            {
                for (var i = 0; i < 15; i++)
                    using (await st.LockAsync())
                    {
                        var d = (JsonObject)(await st.GetDocAsync(DocNames.Params))!;
                        d["n"] = d["n"]!.GetValue<int>() + 1;
                        await st.PutDocAsync(DocNames.Params, d);
                    }
            })));
            Assert.Equal(30, (await s.GetDocAsync(DocNames.Params))!["n"]!.GetValue<int>());
            foreach (var st in stores) st.Dispose();
        }

        [SqlServerFact]
        public async Task Procedures_report_errors_through_Error_Code()
        {
            using var s = await FreshAsync();
            var p = DapperHelper.AddErrorOutputs(new DynamicParameters());
            p.Add("@Cmd", "NOPE");
            await s.Db.ExecuteStoredProcedureAsync("PRO_POGOV_LOG_READ", p);
            Assert.Equal(50001, DapperHelper.ErrorCode(p));
            Assert.Contains("unknown @Cmd", DapperHelper.ErrorMessage(p));

            var q = DapperHelper.AddErrorOutputs(new DynamicParameters());
            q.Add("@Name", "manifest");
            q.Add("@Value", "{not json");
            await s.Db.ExecuteStoredProcedureAsync("PRO_POGOV_DOC_PUT", q);
            Assert.Equal(547, DapperHelper.ErrorCode(q));                                  // CHECK (ISJSON(value) = 1)
            Assert.Equal(0, await s.DocStampAsync(DocNames.Manifest));                      // rolled back with the stamp
        }
    }
}
