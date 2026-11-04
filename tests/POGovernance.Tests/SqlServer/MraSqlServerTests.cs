using Dapper;
using Domain.Entities.Sap;
using Domain.Exceptions;
using Infrastructure.Repositories;
using Infrastructure.Utilitys;
using Microsoft.Data.SqlClient;

namespace POGovernance.Tests.SqlServer
{
    /// <summary>PRO_SAP_MRA_STATUS against a real server: morning upload (truncate + fill), SAP changes, list, summary, history.</summary>
    public class MraSqlServerTests : IClassFixture<SqlServerFixture>
    {
        private readonly SqlServerFixture _fx;
        public MraSqlServerTests(SqlServerFixture fx) => _fx = fx;

        private MraSqlServerRepository Repo() => new(new DapperHelper(_fx.ConnectionString));

        private static MraLine L(string mra, decimal qty, string status, string gen = "", string colour = "", DateTimeOffset? changed = null) =>
            new() { MraNo = mra, Qty = qty, Status = status, GenericArticle = gen, Colour = colour, ChangedAt = changed };

        private async Task<int> Count(string table)
        {
            await using var c = new SqlConnection(_fx.ConnectionString);
            return await c.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM dbo." + table);
        }

        [SqlServerFact]
        public async Task Morning_upload_replaces_every_line_and_SAP_changes_follow()
        {
            var r = Repo();
            var uploadsBefore = await Count("SAP_MRA_UPLOADS");
            var historyBefore = await Count("SAP_MRA_STATUS_HISTORY");
            // yesterday's data, then this morning's file
            await r.UploadAsync(new[] { L("OLD-1", 5, "ALLOW") }, "MRAUPL-1", "yesterday.xlsx", "planner");
            var (rows, replaced) = await r.UploadAsync(new[]
            {
                new MraLine { MraNo = "0000317231", GenericArticle = "1101000101", Colour = "WHT", Qty = 1200, Status = "ALLOW", Mrp = 799,
                              BrandType = "NON-PL", McCode = "121030002", DeliveryDate = new DateTime(2026, 11, 30), ApprovalLevel = "Level 2" },
                L("0000317232", 300, "WATCHLIST", "1101000102", "BLK"),
                L("0000317232", 100, "WATCHLIST", "1101000102", "RED"),   // a second line of the same MRA
                L("0000317240", 50, "ALLOW")                              // the file had no article / colour
            }, "MRAUPL-2", "today.xlsx", "planner");
            Assert.Equal((4, 1), (rows, replaced));

            var t0 = new DateTimeOffset(2026, 11, 4, 10, 0, 0, TimeSpan.FromHours(5.5));
            var res = await r.ApplyAsync(new[]
            {
                L("0000317231", 1200, "SELF_REJECTED", changed: t0),                     // 0 only line → UPDATED
                L("0000317231", 1200, "SELF_REJECTED", changed: t0),                     // 1 same again → UNCHANGED
                L("0000317231", 1200, "ALLOW", changed: t0.AddMinutes(-5)),              // 2 older change → STALE
                L("0000317232", 300, "REJECTED"),                                         // 3 two lines, none named → AMBIGUOUS
                L("0000317232", 300, "REJECTED", "1101000102", "BLK", t0),               // 4 named line → UPDATED
                L("0000317240", 60, "ALLOW", "1101000199", "GRN", t0),                   // 5 completes the line without article → UPDATED
                L("0000317299", 10, "WATCHLIST", "1101000150", "YLW", t0)                // 6 new MRA → CREATED
            }, "sap_ecq", "ECQ", "MRA-1", "T-1");
            Assert.Equal(new[] { "UPDATED", "UNCHANGED", "STALE", "AMBIGUOUS", "UPDATED", "UPDATED", "CREATED" }, res.Select(x => x.Result));
            Assert.Equal(("ALLOW", "SELF_REJECTED"), (res[0].OldStatus, res[0].Status));
            Assert.Equal("SELF_REJECTED", res[2].Status);   // STALE: the stored status stays
            Assert.Null(res[3].Status);                      // AMBIGUOUS: nothing chosen

            var list = await r.ListAsync(new MraListReq { PageSize = 100 });
            Assert.Equal(5, list.Total);
            var first = list.Items.Single(x => x.MraNo == "0000317231");
            Assert.Equal(("SELF_REJECTED", "SAP", 799m, "NON-PL", "Level 2"), (first.Status, first.Source, first.Mrp!.Value, first.BrandType, first.ApprovalLevel));
            Assert.Equal(new DateTime(2026, 11, 30), first.DeliveryDate);   // kept: not sent by SAP
            var completed = list.Items.Single(x => x.MraNo == "0000317240");
            Assert.Equal(("1101000199", "GRN", 60m), (completed.GenericArticle, completed.Colour, completed.Qty));
            Assert.Equal(2, (await r.ListAsync(new MraListReq { Status = "WATCHLIST" })).Total);
            Assert.Single((await r.ListAsync(new MraListReq { Search = "317299" })).Items);
            var page2 = await r.ListAsync(new MraListReq { Page = 2, PageSize = 2 });
            Assert.Equal((5, 2), (page2.Total, page2.Items.Count));

            var s = await r.SummaryAsync(t0.AddDays(-1));
            Assert.Equal((5, 4, 1670m), (s.Lines, s.Mras, s.Qty));   // 1200 + 300 + 100 + 60 + 10
            Assert.Equal(("MRAUPL-2", "today.xlsx", 4, "planner"), (s.LastUploadId, s.LastUploadFile, s.LastUploadRows!.Value, s.LastUploadBy));
            Assert.Equal(historyBefore + 4, s.SapChangesToday);   // updated 3 + created 1
            Assert.Equal(new[] { "ALLOW", "REJECTED", "SELF_REJECTED", "WATCHLIST" }, s.ByStatus.Select(x => x.Status));

            var h = await r.HistoryAsync("0000317231");
            var only = Assert.Single(h);
            Assert.Equal(("UPDATED", "ALLOW", "SELF_REJECTED", "sap_ecq", "MRA-1"), (only.Action, only.OldStatus, only.NewStatus, only.TechUser, only.BatchId));

            // next morning: the file replaces the day's lines; the history stays
            var (rows2, replaced2) = await r.UploadAsync(new[] { L("NEW-1", 1, "ALLOW") }, "MRAUPL-3", "next.xlsx", "planner");
            Assert.Equal((1, 5), (rows2, replaced2));
            Assert.Equal(1, await Count("SAP_MRA_STATUS"));
            Assert.Equal(historyBefore + 4, await Count("SAP_MRA_STATUS_HISTORY"));
            Assert.Equal(uploadsBefore + 3, await Count("SAP_MRA_UPLOADS"));
        }

        [SqlServerFact]
        public async Task A_duplicate_line_in_the_file_keeps_the_previous_data()
        {
            var r = Repo();
            await r.UploadAsync(new[] { L("KEEP", 1, "ALLOW") }, "MRAUPL-K", "keep.xlsx", "planner");
            var e = await Assert.ThrowsAsync<AppValidationException>(() =>
                r.UploadAsync(new[] { L("D1", 1, "ALLOW", "G", "C"), L("D1", 2, "ALLOW", "G", "C") }, "MRAUPL-D", "dup.xlsx", "planner"));
            Assert.Contains("twice", e.Message);
            Assert.Equal("KEEP", (await r.ListAsync(new MraListReq())).Items.Single().MraNo);
        }

        [SqlServerFact]
        public async Task Schema_is_v5()
        {
            await using var c = new SqlConnection(_fx.ConnectionString);
            Assert.Equal("5", await c.ExecuteScalarAsync<string>("SELECT value FROM dbo.schema_info WHERE [key] = N'version'"));
        }
    }
}
