using ClosedXML.Excel;
using DecisionEngine.Common;
using DecisionEngine.Grc;
using DecisionEngine.Results.Analytics.Grc;
using Infrastructure.Services.V1.Excel;

namespace POGovernance.Tests.Analytics.Grc
{
    public sealed class GrcUnitTests : IClassFixture<GrcParityFixture>
    {
        private readonly GrcParityFixture _f;
        public GrcUnitTests(GrcParityFixture f) => _f = f;

        [Fact]
        public void Excel_export_has_same_sheet_headers_and_cell_types()
        {
            var svc = GrcTestServices.Grc();
            var dto = svc.GetRowDetail(_f.Data("week"), null)!;
            var bytes = new GrcExcelExporter().WriteRowDetail(dto);
            using var wb = new XLWorkbook(new MemoryStream(bytes));
            var ws = wb.Worksheet("GRC Row Detail");
            for (var c = 0; c < GrcRowDetailExportDto.Columns.Length; c++)
                Assert.Equal(GrcRowDetailExportDto.Columns[c], ws.Cell(1, c + 1).GetString());
            Assert.Equal(dto.Rows.Count + 1, ws.LastRowUsed()!.RowNumber());
            Assert.True(ws.AutoFilter.IsEnabled);
            Assert.Equal(16, ws.Column(1).Width);
            var first = dto.Rows[0];
            Assert.Equal(XLDataType.Number, ws.Cell(2, 6).DataType); // GRC Qty
            Assert.Equal((double)first[5]!, ws.Cell(2, 6).GetDouble());
            Assert.Equal(Js.ToJsString(first[0]), ws.Cell(2, 1).GetString());
            Assert.StartsWith("Exported ", dto.Message);
        }

        [Fact]
        public void Without_as_of_period_the_current_date_is_the_reference()
        {
            var data = _f.Data("week");
            var nowMs = GrcTestServices.Clock.Now.ToUnixTimeMilliseconds();
            Assert.Equal(nowMs, GrcPeriodMath.AsOfDate(null, "week", nowMs));
            // today is after every week of the file → nothing is a future period
            var e = new GrcBacktestEngine(data, new[] { "AW26" }, new[] { "SS26" }, null, nowMs);
            var bt = e.Compute(null, null, "native", null)!;
            Assert.DoesNotContain(bt.FlatRows, r => r.Bucket == GrcBuckets.FuturePeriod);
            // an as-of in the middle of the file makes the later weeks future periods
            var mid = new GrcBacktestEngine(data, new[] { "AW26" }, new[] { "SS26" }, "05.2026", nowMs).Compute(null, null, "native", null)!;
            Assert.Contains(mid.FlatRows, r => r.Bucket == GrcBuckets.FuturePeriod);
        }

        [Fact]
        public void Cached_compute_equals_fresh_compute()
        {
            var svc = GrcTestServices.Grc();
            var data = _f.Data("week");
            var req = new GrcImpactRequest { Division = "MENS", ViewMode = "month" };
            var a = svc.Compute(data, req);
            var b = svc.Compute(data, req);
            Assert.Equal(System.Text.Json.JsonSerializer.Serialize(a), System.Text.Json.JsonSerializer.Serialize(b));
            Assert.Equal("month", a.PeriodType);
            Assert.True(a.ShowMonthMappingNote);
        }

        [Theory]
        [InlineData("sukhdev.singh@vishalwholesale.co.in", "Sukhdev Singh")]
        [InlineData("anita_rao2@x.com", "Anita Rao")]
        [InlineData("123@x.com", "123@x.com")]
        [InlineData(null, "Unassigned")]
        public void Cat_head_display_name(string? email, string expected) =>
            Assert.Equal(expected, GrcLookups.CatHeadDisplayName(email));

        [Fact]
        public void Js_slice_semantics_for_cutover_not_found()
        {
            var l = new List<string> { "a", "b", "c" };
            Assert.Equal(new[] { "a", "b" }, GrcJs.Slice(l, 0, -1)); // indexOf = -1 → before = all but last
            Assert.Equal(new[] { "c" }, GrcJs.Slice(l, -1));          // with = last only
        }
    }
}
