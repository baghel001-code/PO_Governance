using Application.Interfaces.V1.Sap;
using Dapper;
using DecisionEngine.Mra;
using Infrastructure.Repositories;
using Infrastructure.Utilitys;
using Microsoft.Data.SqlClient;

namespace POGovernance.Tests.SqlServer
{
    /// <summary>PRO_SAP_MRA against a real server: morning file (replace), live messages in arrival order, state since the upload day.</summary>
    public class MraSqlServerTests : IClassFixture<SqlServerFixture>
    {
        private readonly SqlServerFixture _fx;
        public MraSqlServerTests(SqlServerFixture fx) => _fx = fx;

        private MraSqlServerRepository Repo() => new(new DapperHelper(_fx.ConnectionString));

        private static MraFileRecord R(string ga, long qty, string level, string dd = "2026-12-31") =>
            new() { Ga = ga, Colour = "LGY", Qty = qty, Dd = dd, Level = level, Lines = 2, GaDesc = "KURTA",
                    Pos = { new MraFilePo { Po = "4500001", Status = "3", PoDate = "2026-11-02" } },
                    DecisionInfo = new MraDecisionInfo { Decision = "Auto Allow", DecisionMaxPerm = 1200 } };

        [SqlServerFact]
        public async Task File_replaces_the_list_and_messages_replay_on_top()
        {
            var r = Repo();
            await r.UploadAsync(new Dictionary<string, MraFileRecord> { ["1"] = R("1112045675", 10, "1") }, "MRAUPL-1", "2026-11-04", "old.xlsb", "planner", "{}");
            await r.UploadAsync(new Dictionary<string, MraFileRecord> { ["100"] = R("1112045675", 5000, "1"), ["101"] = R("1112045675", 3000, "8") },
                                "MRAUPL-2", "2026-11-05", "today.xlsb", "planner", """{"Mras":2}""");
            var s0 = await r.StampAsync();

            var at = new DateTimeOffset(2026, 11, 5, 10, 0, 0, TimeSpan.FromHours(5.5));
            var seqs = await r.AddFeedAsync(new[]
            {
                new MraFeedRow("mra", """{"mra_no":"100","level":"7"}""", """{"Mra":"100","Level":"7","RawLevel":"7"}""", null),
                new MraFeedRow("mra", """{"mra_no":"x"}""", null, "level not recognised: None"),
                new MraFeedRow("po", """{"po_no":"45","status":"3"}""", """{"Po":"45","Status":"3","RawStatus":"3"}""", null),
            }, "sap_ecq", "ECQ", "T-1", at);
            Assert.Equal(3, seqs.Count);
            Assert.True(seqs[0] < seqs[1] && seqs[1] < seqs[2]);
            Assert.NotEqual(s0, await r.StampAsync());
            // a message from yesterday is not replayed on today's file
            await r.AddFeedAsync(new[] { new MraFeedRow("mra", "{}", """{"Mra":"101","Level":"1","RawLevel":"1"}""", null) }, "sap_ecq", "ECQ", null, at.AddDays(-1));

            var st = await r.StateAsync("2026-11-05");
            Assert.Equal(("MRAUPL-2", "2026-11-05", "today.xlsb", 2), (st.Snapshot!.UploadId, st.Snapshot.UploadDay, st.Snapshot.FileName, st.Snapshot.MraCount));
            Assert.Equal(new[] { "100", "101" }, st.File.Keys.OrderBy(x => x));
            var f = st.File["100"];
            Assert.Equal(("1112045675", "LGY", 5000L, "2026-12-31", "1", 2, "KURTA"), (f.Ga, f.Colour, f.Qty, f.Dd, f.Level, f.Lines, f.GaDesc));
            Assert.Equal(("4500001", "3"), (f.Pos.Single().Po, f.Pos.Single().Status));
            Assert.Equal(("Auto Allow", 1200L), (f.DecisionInfo!.Decision, f.DecisionInfo.DecisionMaxPerm!.Value));
            Assert.Equal(seqs, st.Feed.Select(e => e.Seq));
            Assert.Equal("level not recognised: None", st.Feed[1].Error);
            Assert.Equal(("7", "sap_ecq"), (st.Feed[0].Mra!.Level, st.Feed[0].By));
            Assert.Equal("3", st.Feed[2].Po!.Status);

            var replay = MraReplay.Run(st.File, st.Feed);
            Assert.Equal("7", replay.Mras["100"].Level);
            Assert.Equal("8", replay.Mras["101"].Level);
            Assert.Equal(new[] { "applied", "parked", "stored" }, replay.Results.Select(x => x.Outcome));
        }

        [SqlServerFact]
        public async Task Schema_is_v6_and_the_v5_tables_are_gone()
        {
            await using var c = new SqlConnection(_fx.ConnectionString);
            Assert.Equal("6", await c.ExecuteScalarAsync<string>("SELECT value FROM dbo.schema_info WHERE [key] = N'version'"));
            Assert.Null(await c.ExecuteScalarAsync<int?>("SELECT OBJECT_ID(N'dbo.SAP_MRA_STATUS', N'U')"));
            Assert.NotNull(await c.ExecuteScalarAsync<int?>("SELECT OBJECT_ID(N'dbo.PRO_SAP_MRA', N'P')"));
        }
    }
}
