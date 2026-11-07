using Application.Interfaces.V1.Sap;
using Dapper;
using DecisionEngine.Mra;
using Infrastructure.Repositories;
using Infrastructure.Utilitys;
using Microsoft.Data.SqlClient;

namespace POGovernance.Tests.SqlServer
{
    /// <summary>PRO_SAP_MRA against a real server: morning file (replace), live messages, the pending table before the day's file (v7).</summary>
    public class MraSqlServerTests : IClassFixture<SqlServerFixture>
    {
        private readonly SqlServerFixture _fx;
        public MraSqlServerTests(SqlServerFixture fx) => _fx = fx;

        private MraSqlServerRepository Repo() => new(new DapperHelper(_fx.ConnectionString));

        private static MraFileRecord R(string ga, long qty, string level, string dd = "2026-12-31") =>
            new() { Ga = ga, Colour = "LGY", Qty = qty, Dd = dd, Level = level, Lines = 2, GaDesc = "KURTA",
                    Pos = { new MraFilePo { Po = "4500001", Status = "3", PoDate = "2026-11-02" } },
                    DecisionInfo = new MraDecisionInfo { Decision = "Auto Allow", DecisionMaxPerm = 1200 } };

        private static MraFeedRow Mra(string no, string level, string? ga = null) => new("mra", "{}",
            ga == null ? $$"""{"Mra":"{{no}}","Level":"{{level}}","RawLevel":"{{level}}"}"""
                       : $$"""{"Mra":"{{no}}","Level":"{{level}}","RawLevel":"{{level}}","Ga":"{{ga}}","Colour":"LGY","Qty":700,"Dd":"2026-12-20"}""", null);

        private static DateTimeOffset At(int day, int hour) => new(2026, 11, day, hour, 0, 0, TimeSpan.FromHours(5.5));

        [SqlServerFact]
        public async Task Changes_before_the_days_file_wait_and_are_moved_onto_it()
        {
            var r = Repo();
            // day 1 (5 Nov): file, then a change -> applied at once (IsDailyDataUpload)
            Assert.Equal(0, await r.UploadAsync(new Dictionary<string, MraFileRecord> { ["100"] = R("1112045675", 5000, "1"), ["101"] = R("1112045675", 3000, "8") },
                                                "MRAUPL-1", "2026-11-05", "day1.xlsb", "planner", "{}"));
            var d1 = await r.AddFeedAsync(new[] { Mra("101", "1") }, "sap_ecq", "ECQ", "T-1", At(5, 10));
            Assert.False(d1.Single().Pending);
            var s1 = await r.StateAsync("2026-11-05");
            Assert.Equal((true, "MRAUPL-1"), (s1.Control.IsDailyDataUpload, s1.Control.DataVersion));
            Assert.Single(s1.Feed);

            // day 2 (6 Nov) before the file: IsDailyDataUpload is false -> pending; one is an MRA that is not in any file
            var stamp0 = await r.StampAsync("2026-11-06");
            var d2 = await r.AddFeedAsync(new[] { Mra("100", "7"), Mra("200", "1", "1112045675"),
                                                  new MraFeedRow("mra", """{"mra_no":"x"}""", null, "level not recognised: None") },
                                          "sap_ecq", "ECQ", "T-2", At(6, 8));
            Assert.All(d2, x => Assert.True(x.Pending));
            Assert.NotEqual(stamp0, await r.StampAsync("2026-11-06"));
            var s2 = await r.StateAsync("2026-11-06");
            Assert.Equal((false, "MRAUPL-1"), (s2.Control.IsDailyDataUpload, s2.Control.DataVersion));
            Assert.Equal(3, s2.Pending.Count);
            Assert.Single(s2.Feed);                                     // still yesterday's file + its change
            Assert.Equal("1", MraReplay.Run(s2.File, s2.Feed).Mras["101"].Level);

            // day 2 file: replaces the list; the 3 pending messages are moved onto it (arrival order) and the pending table is empty
            var moved = await r.UploadAsync(new Dictionary<string, MraFileRecord> { ["100"] = R("1112045675", 5000, "1") },
                                            "MRAUPL-2", "2026-11-06", "day2.xlsb", "planner", "{}");
            Assert.Equal(3, moved);
            var s3 = await r.StateAsync("2026-11-06");
            Assert.Equal((true, "MRAUPL-2", 3), (s3.Control.IsDailyDataUpload, s3.Control.DataVersion, s3.Snapshot!.MovedFromPending!.Value));
            Assert.Empty(s3.Pending);
            Assert.Equal(new[] { "100", "200", null }, s3.Feed.Select(e => e.Mra?.Mra));
            Assert.Equal("level not recognised: None", s3.Feed[2].Error);
            var st = MraReplay.Run(s3.File, s3.Feed);
            Assert.Equal("7", st.Mras["100"].Level);                    // the change SAP sent before the file
            Assert.True(st.Mras["200"].CreatedLive);                   // the MRA that is not in the file
            Assert.False(st.Mras.ContainsKey("101"));                  // yesterday's list is replaced

            // after the file: applied at once with the new version
            var after = await r.AddFeedAsync(new[] { Mra("200", "2") }, "sap_ecq", "ECQ", null, At(6, 11));
            Assert.False(after.Single().Pending);
            Assert.Equal(4, (await r.StateAsync("2026-11-06")).Feed.Count);

            // a second file the same day keeps the day's changes
            Assert.Equal(0, await r.UploadAsync(new Dictionary<string, MraFileRecord> { ["100"] = R("1112045675", 4000, "1") },
                                                "MRAUPL-3", "2026-11-06", "day2b.xlsb", "planner", "{}"));
            var s4 = await r.StateAsync("2026-11-06");
            Assert.Equal(("MRAUPL-3", 4), (s4.Control.DataVersion, s4.Feed.Count));
            Assert.Equal("2", MraReplay.Run(s4.File, s4.Feed).Mras["200"].Level);

            // next day: back to pending until that day's file
            Assert.True((await r.AddFeedAsync(new[] { Mra("100", "2") }, "sap_ecq", "ECQ", null, At(7, 7))).Single().Pending);
            Assert.False((await r.StateAsync("2026-11-07")).Control.IsDailyDataUpload);
        }

        [SqlServerFact]
        public async Task Decisions_and_their_working_are_read_for_the_mra_link()
        {
            await using (var c = new SqlConnection(_fx.ConnectionString))
            {
                await c.ExecuteAsync("""
                    INSERT INTO dbo.SAP_DECISION_LOG (Decision_Id, Request_Id, Http_Status, Is_Test, Po_Number, Decided_At, Response_Json)
                    VALUES (N'D-20261106-000001', N'R-MRA-1', 200, 0, N'4500099001', '2026-11-06T10:00:00+05:30', N'{"view_url":"http://portal/verdict/tok1"}'),
                           (N'D-20261106-000002', N'R-MRA-2', 200, 1, N'4500099002', '2026-11-06T10:00:00+05:30', N'{}');
                    INSERT INTO dbo.SAP_DECISION_ITEMS (Decision_Log_Id, Po_Item, Gm, Colour, Delivery_Month, Order_Qty, Verdict, Engine_Decision, Reason_Text, Metrics_Json, Working_Json)
                    SELECT Id, 10, N'1112045675', N'BLK', N'12.2026', 2000, N'ALLOW', N'Auto Allow', N'Colour STR within limit', N'{"headroom_qty":22803}',
                           N'{"decision":"Auto Allow","maxPermissible":22803}'
                      FROM dbo.SAP_DECISION_LOG WHERE Request_Id IN (N'R-MRA-1', N'R-MRA-2');
                    -- an old decision (before @Since) still comes when SAP stored its document (MRA number)
                    INSERT INTO dbo.SAP_DECISION_LOG (Decision_Id, Request_Id, Http_Status, Is_Test, Po_Number, Decided_At, Response_Json)
                    VALUES (N'D-20260801-000001', N'R-MRA-3', 200, 0, NULL, '2026-08-01T10:00:00+05:30', N'{}'),
                           (N'D-20260801-000002', N'R-MRA-4', 200, 0, NULL, '2026-08-01T10:00:00+05:30', N'{}');
                    INSERT INTO dbo.SAP_DECISION_ITEMS (Decision_Log_Id, Po_Item, Gm, Colour, Delivery_Month, Order_Qty, Verdict, Engine_Decision)
                    SELECT Id, 10, N'1112045675', N'WHT', N'12.2026', 500, N'ALLOW', N'Auto Allow' FROM dbo.SAP_DECISION_LOG WHERE Request_Id IN (N'R-MRA-3', N'R-MRA-4');
                    INSERT INTO dbo.SAP_DECISION_DOCUMENTS (Decision_Log_Id, Request_Id, Decision_Id, Document_No, Is_Current)
                    SELECT Id, Request_Id, Decision_Id, N'900777', 1 FROM dbo.SAP_DECISION_LOG WHERE Request_Id = N'R-MRA-3';
                    INSERT INTO dbo.SAP_DECISION_DOCUMENTS (Decision_Log_Id, Request_Id, Decision_Id, Document_No, Is_Current)
                    SELECT Id, Request_Id, Decision_Id, N'900111', 1 FROM dbo.SAP_DECISION_LOG WHERE Request_Id = N'R-MRA-1';
                    INSERT INTO dbo.SAP_DECISION_DOCUMENTS (Decision_Log_Id, Request_Id, Decision_Id, Document_No, Is_Current)
                    SELECT Id, Request_Id, Decision_Id, N'900222', 1 FROM dbo.SAP_DECISION_LOG WHERE Request_Id = N'R-MRA-2';
                    """);
            }
            var r = Repo();
            var d = (await r.DecisionsAsync(new DateTime(2026, 10, 1))).Where(x => x.RequestId?.StartsWith("R-MRA") == true).ToList();
            var old = Assert.Single(d, x => x.RequestId == "R-MRA-3");    // any age, SAP stored its document
            Assert.Equal("900777", old.DocumentNo);
            Assert.DoesNotContain(d, x => x.RequestId is "R-MRA-2" or "R-MRA-4");   // test decision / no document: never linked
            var one = Assert.Single(d, x => x.RequestId == "R-MRA-1");
            Assert.Equal("900111", one.DocumentNo);
            Assert.Equal(("D-20261106-000001", "Auto Allow", 22803d, "http://portal/verdict/tok1", true),
                         (one.DecisionId, one.Decision, one.MaxPermissible!.Value, one.ViewUrl, one.HasWorking));
            Assert.Equal(("1112045675", "BLK", "12.2026", "4500099001"), (one.Gm, one.Colour, one.DeliveryMonth, one.PoNumber));
            Assert.Contains("\"maxPermissible\":22803", await r.WorkingAsync(one.ItemId));
        }

        [SqlServerFact]
        public async Task Schema_is_v8_and_the_v5_tables_are_gone()
        {
            await using var c = new SqlConnection(_fx.ConnectionString);
            Assert.Equal("8", await c.ExecuteScalarAsync<string>("SELECT value FROM dbo.schema_info WHERE [key] = N'version'"));
            Assert.Null(await c.ExecuteScalarAsync<int?>("SELECT OBJECT_ID(N'dbo.SAP_MRA_STATUS', N'U')"));
            Assert.NotNull(await c.ExecuteScalarAsync<int?>("SELECT OBJECT_ID(N'dbo.PRO_SAP_MRA', N'P')"));
        }
    }
}
