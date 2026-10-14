using System.Text.Json.Nodes;
using Application.DTOs.Recommendation;
using Application.Interfaces.V1;
using Application.Services.V1.Engines.Common;
using Application.Services.V1.Recommendation;
using ClosedXML.Excel;
using Domain.Entities.Engine;
using Infrastructure.Services.V1.Excel;

namespace POGovernance.Tests.Recommendation
{
    public class RecommendationUnitTests
    {
        [Fact]
        public void JsObjectMap_orders_array_index_keys_first()
        {
            var m = new JsObjectMap<int> { ["blk"] = 1, ["101"] = 2, ["07"] = 3, ["7"] = 4, ["1114123897"] = 5, ["4294967295"] = 6 };
            Assert.Equal(new[] { "7", "101", "1114123897", "blk", "07", "4294967295" }, m.Keys);
        }

        [Fact]
        public void JsSort_is_stable()
        {
            var l = new List<(string K, double V)> { ("a", 1), ("b", 2), ("c", 1), ("d", 0), ("e", 2) };
            JsSort.StableSort(l, (x, y) => y.V - x.V);
            Assert.Equal("b,e,a,c,d", string.Join(",", l.Select(x => x.K)));
        }

        [Fact]
        public void Month_math_matches_js()
        {
            Assert.Equal((2027, 1), MonthMath.AddMonths(2026, 12, 1));
            Assert.Equal((2025, 12), MonthMath.AddMonths(2026, 1, -1));
            Assert.Equal(29, MonthMath.DaysInMonth(2028, 2));
            Assert.Equal(31, MonthMath.DaysInMonth(2026, 13)); // new Date(2026,13,0) → 31 Jan 2027
            Assert.Equal("Oct 2026", MonthMath.MonLabel(2026, 10));
            Assert.Equal("07.2026", MonthMath.Mmyyyy(2026, 7));
            Assert.True(double.IsNaN(MonthMath.MonthOrdinal("Jul-26")));
        }

        [Fact]
        public void Ledger_approved_qty_rules()
        {
            JsonObject R(string json) => JsonNode.Parse(json)!.AsObject();
            Assert.Null(LedgerHelpers.ApprovedQtyFor(R("{\"engine\":\"A\",\"decision\":\"Auto Allow\",\"qty\":5}")));
            Assert.Equal(5, LedgerHelpers.ApprovedQtyFor(R("{\"engine\":\"B\",\"decision\":\"Auto Allow\",\"qty\":5}")));
            Assert.Equal(0, LedgerHelpers.ApprovedQtyFor(R("{\"engine\":\"B\",\"decision\":\"Watch List\",\"qty\":5}")));
            Assert.Equal(0, LedgerHelpers.ApprovedQtyFor(R("{\"engine\":\"B\",\"decision\":\"Auto Allow\",\"qty\":5,\"finalApprovedQty\":0}")));
            Assert.Equal(5, LedgerHelpers.ApprovedQtyFor(R("{\"engine\":\"B\",\"decision\":\"Auto Allow\",\"qty\":5,\"finalApprovedQty\":null}")));
            Assert.True(LedgerHelpers.IsToday(R("{}"), "2026-10-14"));
            Assert.False(LedgerHelpers.IsToday(R("{\"reqDate\":\"2026-10-13\"}"), "2026-10-14"));
        }

        [Fact]
        public void Carry_note_text()
        {
            var k = new CarryOver(1234567, 5, "09.2026", "10.2026");
            Assert.Equal(" [Includes 12,34,567 units of previous month's (09.2026) pending PO, carried over — Day-of-month cut-off = 5.]", PrevMonthCarry.Note(k));
            Assert.Equal("", PrevMonthCarry.Note(k with { Qty = 0 }));
        }

        [Fact]
        public void Excel_writer_keeps_sheet_names_headers_and_types()
        {
            var ctx = RecommendationFixture.Context("S3-year-end");
            var engine = new RecommendationEngine();
            var view = RecommendationService.Build(engine, ctx, new RecommendationRequestDto { Gm = "2203000201" });
            Assert.True(view.A!.Recs!.Count > 0);
            var export = engine.BuildExport(ctx, view.A, "2026-12-02");
            var bytes = new RecommendationExcelExporter().Write(export);
            using var wb = new XLWorkbook(new MemoryStream(bytes));
            Assert.Equal(new[] { "1-Recommendations", "2-All Colours (full bucket)", "3-All Gen Materials", "4-Pending PO Detail (raw)", "5-Q1-Q2 Selection Trace" },
                         wb.Worksheets.Select(w => w.Name));
            var ws = wb.Worksheet(1);
            Assert.Equal("Attribute", ws.Cell(1, 1).GetString());
            Assert.Equal("12.2026 Colour PO Qty", ws.Cell(1, 16).GetString());
            Assert.Equal(XLDataType.Number, ws.Cell(2, 3).DataType);
            Assert.Equal(export.Sheets[0].Rows.Count, ws.LastRowUsed()!.RowNumber());
        }

        private sealed class FakeCache : IDatasetCache
        {
            public EngineData Data = null!;
            public Task<EngineData> GetCurrentAsync(CancellationToken ct = default) => Task.FromResult(Data);
            public Task<EngineData> GetForVersionsAsync(IReadOnlyDictionary<string, string> versions, EngineParams p, CancellationToken ct = default) => Task.FromResult(Data);
            public void Invalidate() { }
        }

        private sealed class FakeLedger : IRequestLedger
        {
            public List<JsonObject> Reqs = new();
            public string? AskedDate;
            public Task<(List<JsonObject> Requests, int Seq)> GetDayAsync(string date, CancellationToken ct = default) { AskedDate = date; return Task.FromResult((Reqs, 0)); }
            public Task<JsonObject> SaveAsync(JsonObject request, int seenSeq, bool force, Domain.Records.AppUserContext user, CancellationToken ct = default) => throw new NotSupportedException();
        }

        private sealed class FakeClock : IAppClock
        {
            public DateTimeOffset Now => new(2026, 10, 14, 10, 0, 0, TimeSpan.FromHours(5.5));
            public string Today => "2026-10-14";
            public string NowIso => Now.ToString("o");
        }

        [Fact]
        public async Task Service_uses_server_date_and_ledger()
        {
            var fixtureCtx = RecommendationFixture.Context("S2-params");
            var ledger = new FakeLedger { Reqs = fixtureCtx.Requests.ToList() };
            var svc = new RecommendationService(new FakeCache { Data = fixtureCtx.Data }, ledger, new FakeClock(), new RecommendationEngine());
            var view = await svc.GetAsync(new RecommendationRequestDto { Gm = "1101000101" });
            Assert.Equal("2026-10-14", ledger.AskedDate);
            Assert.Equal("B", view.RenderEngine);
            var direct = RecommendationService.Build(new RecommendationEngine(), fixtureCtx, new RecommendationRequestDto { Gm = "1101000101" });
            JsonParity.Check(JsonParity.ToNode(direct.B), view.B, "service");

            var months = await svc.GetMonthsAsync(new RecommendationRequestDto { Brand = "NON-PL", McCd = "2203", Mrp = "599", Flag = "BOGO" });
            Assert.Equal("A", months.Engine);
            Assert.Equal("Fit", months.RelevantDim);
            Assert.Equal(new[] { "10.2026", "11.2026", "12.2026", "01.2027" }, months.Months);

            var export = await svc.BuildExportAsync(new RecommendationRequestDto { Gm = "2203000201" });
            Assert.Equal("VMM_EngineA_FullDetail_2026-10-14.xlsx", export.FileName);
        }

        [Fact]
        public void Dim_override_options()
        {
            var o = new RecommendationEngine().DimOverrideOptions("Fit", null);
            Assert.Equal(new[] { "Style", "Fit (Relevant)", "Product Material", "Design" }, o.Select(x => x.Label));
            Assert.True(o[1].Selected);
            Assert.True(new RecommendationEngine().DimOverrideOptions("Colour-only", null)[0].Selected == false);
        }
    }
}
