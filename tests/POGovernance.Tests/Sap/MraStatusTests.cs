using System.Text.Json;
using Application.DTOs.Ingest;
using Application.DTOs.Platform;
using Application.Interfaces.V1;
using Application.Interfaces.V1.Sap;
using Application.Services.V1.Sap;
using ClosedXML.Excel;
using Domain.Entities.AppSettings;
using Domain.Entities.Sap;
using Domain.Exceptions;
using Domain.Records;
using Infrastructure.Services.V1.Excel;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using POGovernance.Tests.Tool1;

namespace POGovernance.Tests.Sap
{
    /// <summary>MRA status: the morning file (Data Setup) and POST /api/sap/v1/mra-status.</summary>
    public class MraStatusTests
    {
        private static readonly List<string> Statuses = new SapInterfaceSettings().MraStatuses;

        /// <summary>In-memory stand-in: upload merges by exact key, apply matches by MRA No + article + colour (the SQL rules are tested on SQL Server).</summary>
        private sealed class FakeRepo : IMraRepository
        {
            public readonly List<MraLine> Lines = new();
            public int Uploads, Applies;
            public bool Available => true;
            public string? UnavailableReason => null;

            public Task<MraUploadCounts> UploadAsync(IReadOnlyList<MraLine> lines, string uploadId, string? fileName, string by, CancellationToken ct = default)
            {
                Uploads++;
                var c = new MraUploadCounts { Rows = lines.Count };
                for (var i = 0; i < lines.Count; i++)
                {
                    var l = lines[i];
                    var same = Lines.Where(x => x.MraNo == l.MraNo).ToList();
                    if (l.GenericArticle == "" && l.Colour == "" && same.Count > 1) { c.Ambiguous++; c.AmbiguousLines.Add((i, l.MraNo)); continue; }
                    var hit = same.FirstOrDefault(x => x.GenericArticle == l.GenericArticle && x.Colour == l.Colour);
                    if (hit == null) { Lines.Add(l); c.Created++; }
                    else if (hit.Status == l.Status && hit.Qty == l.Qty) c.Unchanged++;
                    else { hit.Status = l.Status; hit.Qty = l.Qty; c.Updated++; }
                }
                return Task.FromResult(c);
            }

            public Task<List<MraApplyRow>> ApplyAsync(IReadOnlyList<MraLine> lines, string by, string? sourceSystem, string batchId, string? traceId, CancellationToken ct = default)
            {
                Applies++;
                var res = new List<MraApplyRow>();
                for (var i = 0; i < lines.Count; i++)
                {
                    var l = lines[i];
                    var cur = Lines.FirstOrDefault(x => x.MraNo == l.MraNo && x.GenericArticle == l.GenericArticle && x.Colour == l.Colour);
                    var r = new MraApplyRow { Idx = i, MraNo = l.MraNo, GenericArticle = l.GenericArticle, Colour = l.Colour, Status = l.Status, Qty = l.Qty };
                    if (cur == null) { Lines.Add(l); r.Result = MraApplyResults.Created; }
                    else if (cur.Status == l.Status && cur.Qty == l.Qty) { r.Result = MraApplyResults.Unchanged; r.OldStatus = cur.Status; }
                    else { r.OldStatus = cur.Status; cur.Status = l.Status; cur.Qty = l.Qty; r.Result = MraApplyResults.Updated; }
                    res.Add(r);
                }
                return Task.FromResult(res);
            }

            public Task<MraListResponse> ListAsync(MraListReq req, CancellationToken ct = default) => Task.FromResult(new MraListResponse { Items = Lines.ToList(), Total = Lines.Count });
            public Task<MraSummary> SummaryAsync(DateTimeOffset since, CancellationToken ct = default) => Task.FromResult(new MraSummary { Lines = Lines.Count });
            public Task<List<MraHistoryRow>> HistoryAsync(string mraNo, CancellationToken ct = default) => Task.FromResult(new List<MraHistoryRow>());
        }

        private sealed class FakeAudit : IAuditService
        {
            public readonly List<string> Lines = new();
            public Task AppendAsync(string who, string action, string detail = "", CancellationToken ct = default) { Lines.Add(action + " " + detail); return Task.CompletedTask; }
            public Task<AuditQueryResponse> QueryAsync(string? from, string? to, string? q, CancellationToken ct = default) => throw new NotSupportedException();
        }

        private static readonly SapCallContext Caller = new("sap_ecq", "ECQ", "10.0.0.5", "T-abc123");
        private static readonly AppUserContext Planner = new("plan@x.com", "Plan User", new[] { "planner" }, false);

        private static SapMraService Sap(FakeRepo repo, int maxBatch = 500) =>
            new(repo, new FakeAppClock(), Options.Create(new SapInterfaceSettings { MaxMraLinesPerBatch = maxBatch }), NullLogger<SapMraService>.Instance);

        private static (MraService Svc, FakeRepo Repo, FakeAudit Audit) Portal()
        {
            var repo = new FakeRepo();
            var audit = new FakeAudit();
            return (new MraService(repo, new ExcelWorkbookReader(), new FakeAppClock(), audit, Options.Create(new SapInterfaceSettings()),
                                   NullLogger<MraService>.Instance), repo, audit);
        }

        /// <summary>The layout of MRA_Format_For_upload.xlsx: a group heading row, the header row, data rows.</summary>
        private static byte[] Workbook(Action<IXLWorksheet> fill)
        {
            using var wb = new XLWorkbook();
            var ws = wb.AddWorksheet("Sheet1");
            ws.Cell(1, 12).Value = " Needed for Multiconcept req computation";
            var headers = new[] { "MRA No", "Generic Art Cd", "Generic Art Desc", "Colour", "Vendor Cd", "Vendor Name", "Brand Type", "MC CODE", "MC NAME",
                                  "MRP", "Planning flag", "Fit", "Design", "Style", "Fabric", "Product material", "PO Qty", "PO Val", "Delivery date",
                                  "Decision", "Level (Waiting for approval)" };
            for (var c = 0; c < headers.Length; c++) ws.Cell(2, c + 1).Value = headers[c];
            fill(ws);
            using var ms = new MemoryStream();
            wb.SaveAs(ms);
            return ms.ToArray();
        }

        private static void Row(IXLWorksheet ws, int r, string mra, string gen, string colour, double? qty, string status, string level = "Level 2/3/4")
        {
            ws.Cell(r, 1).Value = mra;          // text: keeps the leading zeros
            ws.Cell(r, 2).Value = gen;
            ws.Cell(r, 4).Value = colour;
            ws.Cell(r, 7).Value = "NON-PL";
            ws.Cell(r, 8).Value = "121030002";
            ws.Cell(r, 9).Value = "L_EW_MM_KURTA";
            ws.Cell(r, 10).Value = 799;
            if (qty != null) ws.Cell(r, 17).Value = qty.Value;
            ws.Cell(r, 20).Value = status;
            ws.Cell(r, 21).Value = level;
        }

        [Theory]
        [InlineData("Allow", "ALLOW")]
        [InlineData("Watchlist", "WATCHLIST")]
        [InlineData("Watch List", "WATCHLIST")]
        [InlineData("Cancelled", "CANCELLED")]
        [InlineData("discard", "DISCARDED")]
        [InlineData("Reject", "REJECTED")]
        [InlineData("Self Reject", "SELF_REJECTED")]
        [InlineData("self-rejected", "SELF_REJECTED")]
        [InlineData(" SELF_REJECTED ", "SELF_REJECTED")]
        [InlineData("Approved", null)]
        [InlineData("", null)]
        public void Status_spellings_are_matched_to_the_configured_values(string input, string? expected) =>
            Assert.Equal(expected, MraStatuses.Normalize(input, Statuses));

        [Fact]
        public void A_new_status_is_accepted_once_it_is_configured()
        {
            Assert.Null(MraStatuses.Normalize("On Hold", Statuses));
            Assert.Equal("ON_HOLD", MraStatuses.Normalize("On Hold", Statuses.Append("ON_HOLD").ToList()));
        }

        [Theory]
        [InlineData("""{"mras":[]}""", "mras must be a non-empty array")]
        [InlineData("""{"mras":[{"qty":1,"status":"ALLOW"}]}""", "mras[0].mra_no is required")]
        [InlineData("""{"mras":[{"mra_no":"1","status":"ALLOW"}]}""", "mras[0].qty is required")]
        [InlineData("""{"mras":[{"mra_no":"1","qty":-5,"status":"ALLOW"}]}""", "mras[0].qty must not be negative")]
        [InlineData("""{"mras":[{"mra_no":"1","qty":5}]}""", "mras[0].status is required")]
        [InlineData("""{"mras":[{"mra_no":"1","qty":5,"status":"APPROVED"}]}""", "mras[0].status 'APPROVED' is not one of")]
        [InlineData("""{"mras":[{"mra_no":"1","qty":5,"status":"ALLOW","changed_at":"yesterday"}]}""", "mras[0].changed_at must be an ISO 8601")]
        [InlineData("""{"mras":[{"mra_no":"1","qty":5,"status":"ALLOW","delivery_date":"30.11.2026"}]}""", "mras[0].delivery_date must be a date YYYY-MM-DD")]
        [InlineData("""{"mras":[{"mra_no":"123456789012345678901","qty":5,"status":"ALLOW"}]}""", "mras[0].mra_no must be at most 20 characters")]
        public async Task A_wrong_line_refuses_the_whole_batch_with_400(string body, string message)
        {
            var repo = new FakeRepo();
            var e = await Assert.ThrowsAsync<SapApiException>(() => Sap(repo).ReceiveAsync(body, Caller));
            Assert.Equal(400, e.Status);
            Assert.Equal(SapErrorCodes.ValidationFailed, e.ErrorCode);
            Assert.StartsWith(message, e.Message);
            Assert.Equal(0, repo.Applies);
        }

        [Fact]
        public async Task Invalid_json_and_too_many_lines()
        {
            var e = await Assert.ThrowsAsync<SapApiException>(() => Sap(new FakeRepo()).ReceiveAsync("{not json", Caller));
            Assert.Equal((400, SapErrorCodes.InvalidJson), (e.Status, e.ErrorCode));
            var big = JsonSerializer.Serialize(new { mras = Enumerable.Range(0, 3).Select(i => new { mra_no = "M" + i, qty = 1, status = "ALLOW" }) });
            e = await Assert.ThrowsAsync<SapApiException>(() => Sap(new FakeRepo(), maxBatch: 2).ReceiveAsync(big, Caller));
            Assert.Equal((413, SapErrorCodes.BatchTooLarge), (e.Status, e.ErrorCode));
        }

        [Fact]
        public async Task SAP_changes_are_applied_in_order_with_a_result_per_line()
        {
            var repo = new FakeRepo();
            repo.Lines.Add(new MraLine { MraNo = "0000317231", Qty = 1200, Status = "ALLOW" });
            var body = """
                { "mras": [
                    { "mra_no": "0000317231", "qty": 1200, "status": "Self Reject", "changed_at": "2026-11-04T10:15:00+05:30" },
                    { "mra_no": "0000317299", "qty": 300, "status": "WATCHLIST", "generic_article": "1101000101", "colour": "WHT",
                      "delivery_date": "2026-11-30", "mrp": 799 },
                    { "mra_no": "0000317299", "qty": 300, "status": "WATCHLIST", "generic_article": "1101000101", "colour": "WHT" } ] }
                """;
            var r = await Sap(repo).ReceiveAsync(body, Caller);
            Assert.Equal(200, r.Status);
            var a = SapJson.Deserialize<SapMraStatusResponse>(r.Json)!;
            Assert.Equal((3, 1, 1, 1), (a.Received, a.Created, a.Updated, a.Unchanged));
            Assert.StartsWith("MRA-2026", a.BatchId);
            Assert.Equal(("UPDATED", "ALLOW", "SELF_REJECTED"), (a.Results[0].Result, a.Results[0].PreviousStatus, a.Results[0].Status));
            Assert.Equal("CREATED", a.Results[1].Result);
            Assert.Equal("UNCHANGED", a.Results[2].Result);
            var created = repo.Lines.Single(l => l.MraNo == "0000317299");
            Assert.Equal((new DateTime(2026, 11, 30), 799m, "SAP"), (created.DeliveryDate!.Value, created.Mrp!.Value, created.Source));
            using var doc = JsonDocument.Parse(r.Json);   // snake_case answer
            Assert.True(doc.RootElement.TryGetProperty("received_at", out _));
            Assert.True(doc.RootElement.GetProperty("results")[0].TryGetProperty("previous_status", out _));
        }

        [Fact]
        public void The_morning_file_is_read_by_its_headers_below_a_group_heading()
        {
            var bytes = Workbook(ws =>
            {
                Row(ws, 3, "0000317231", "1101000101", "WHT", 1200, "Allow");
                Row(ws, 4, "0000317232", "1101000102", "BLK", 300, "Watchlist");
                Row(ws, 5, "0000317233", "1101000103", "RED", 0, "Cancelled", "Level 1");
                ws.Cell(5, 19).Value = new DateTime(2026, 11, 30);   // Excel date
                ws.Cell(5, 18).Value = 239700.5;
                Row(ws, 6, "0000317234", "1101000104", "NVY", 50, "Rejected");
            });
            var r = MraWorkbookParser.Parse(new ExcelWorkbookReader().Read(bytes, "MRA.xlsx"), Statuses);
            Assert.True(r.Ok, string.Join("; ", r.Errors));
            Assert.Equal("Sheet1", r.Sheet);
            Assert.Equal(4, r.Lines.Count);
            var l = r.Lines[0];
            Assert.Equal(("0000317231", "1101000101", "WHT", 1200m, "ALLOW", "NON-PL", "121030002", 799m),
                         (l.MraNo, l.GenericArticle, l.Colour, l.Qty, l.Status, l.BrandType, l.McCode, l.Mrp!.Value));
            Assert.Equal("Level 2/3/4", l.ApprovalLevel);
            Assert.Equal((new DateTime(2026, 11, 30), 239700.5m), (r.Lines[2].DeliveryDate!.Value, r.Lines[2].PoValue!.Value));
            Assert.Equal(new[] { "ALLOW", "WATCHLIST", "CANCELLED", "REJECTED" }, r.Lines.Select(x => x.Status));
            Assert.Equal("Qty", r.Columns["PO Qty"]);
            Assert.Equal("Status", r.Columns["Decision"]);
        }

        [Fact]
        public void MRN_No_Qty_and_Status_headers_are_accepted_too_and_empty_rows_are_skipped()
        {
            using var wb = new XLWorkbook();
            var ws = wb.AddWorksheet("MRA");
            ws.Cell(1, 1).Value = "MRN NO.";
            ws.Cell(1, 2).Value = "Qty";
            ws.Cell(1, 3).Value = "Status";
            ws.Cell(2, 1).Value = 317231;                 // a number: written without decimals
            ws.Cell(2, 2).Value = "1,200";
            ws.Cell(2, 3).Value = "Discard";
            ws.Cell(4, 1).Value = "317232";
            ws.Cell(4, 2).Value = 5;
            ws.Cell(4, 3).Value = "Self Reject";
            using var ms = new MemoryStream();
            wb.SaveAs(ms);
            var r = MraWorkbookParser.Parse(new ExcelWorkbookReader().Read(ms.ToArray(), "x.xlsx"), Statuses);
            Assert.True(r.Ok, string.Join("; ", r.Errors));
            Assert.Equal(1, r.SkippedEmpty);
            Assert.Equal(("317231", 1200m, "DISCARDED"), (r.Lines[0].MraNo, r.Lines[0].Qty, r.Lines[0].Status));
            Assert.Equal("SELF_REJECTED", r.Lines[1].Status);
        }

        [Fact]
        public void A_file_with_any_wrong_row_is_refused_with_every_problem()
        {
            var bytes = Workbook(ws =>
            {
                Row(ws, 3, "0000317231", "1101000101", "WHT", 1200, "Allow");
                Row(ws, 4, "", "1101000102", "BLK", 300, "Watchlist");           // no MRA No
                Row(ws, 5, "0000317233", "1101000103", "RED", null, "Allow");    // no qty
                Row(ws, 6, "0000317234", "1101000104", "NVY", 50, "Approved");   // unknown status
                Row(ws, 7, "0000317231", "1101000101", "WHT", 10, "Allow");      // same line as row 3
                Row(ws, 8, "0000317235", "1101000105", "GRN", 10, "");           // no status
                ws.Cell(8, 19).Value = "next week";                               // not a date
            });
            var r = MraWorkbookParser.Parse(new ExcelWorkbookReader().Read(bytes, "MRA.xlsx"), Statuses);
            Assert.False(r.Ok);
            Assert.Contains("Row 4: MRA No is missing", r.Errors);
            Assert.Contains("Row 5: Qty is missing or not a number", r.Errors);
            Assert.Contains(r.Errors, e => e.StartsWith("Row 6: Status \"Approved\" is not one of"));
            Assert.Contains("Row 7: MRA 0000317231 / 1101000101 / WHT is already in row 3", r.Errors);
            Assert.Contains("Row 8: Status is missing", r.Errors);
            Assert.Contains(r.Errors, e => e.StartsWith("Row 8: Delivery date is not a date"));
        }

        [Fact]
        public void Missing_required_columns_are_named()
        {
            using var wb = new XLWorkbook();
            var ws = wb.AddWorksheet("S");
            ws.Cell(1, 1).Value = "MRA No";
            ws.Cell(1, 2).Value = "Colour";
            ws.Cell(2, 1).Value = "1";
            using var ms = new MemoryStream();
            wb.SaveAs(ms);
            var r = MraWorkbookParser.Parse(new ExcelWorkbookReader().Read(ms.ToArray(), "x.xlsx"), Statuses);
            Assert.Equal(new[] { "Column \"Qty\" is missing (required: MRA No, Qty, Status)", "Column \"Status\" is missing (required: MRA No, Qty, Status)" }, r.Errors);
            var none = MraWorkbookParser.Parse(new ExcelWorkbookReader().Read("a,b\n1,2\n"u8.ToArray(), "x.csv"), Statuses);
            Assert.StartsWith("No sheet has an \"MRA No\" column header", none.Errors.Single());
        }

        [Fact]
        public async Task Upload_merges_only_when_the_file_is_valid()
        {
            var (svc, repo, audit) = Portal();
            repo.Lines.Add(new MraLine { MraNo = "OLD", Qty = 1, Status = "ALLOW" });
            repo.Lines.Add(new MraLine { MraNo = "M1", GenericArticle = "G1", Colour = "WHT", Qty = 10, Status = "WATCHLIST" });
            repo.Lines.Add(new MraLine { MraNo = "M3", GenericArticle = "G3", Colour = "A", Qty = 1, Status = "ALLOW" });
            repo.Lines.Add(new MraLine { MraNo = "M3", GenericArticle = "G3", Colour = "B", Qty = 1, Status = "ALLOW" });

            var bad = await svc.UploadAsync(new IngestUpload("bad.xlsx", Workbook(ws => Row(ws, 3, "M1", "G", "C", null, "Allow"))), Planner);
            Assert.False(bad.Ok);
            Assert.Equal(0, repo.Uploads);                       // nothing changed
            Assert.Equal(4, repo.Lines.Count);

            var ok = await svc.UploadAsync(new IngestUpload("MRA 04-11.xlsx", Workbook(ws =>
            {
                Row(ws, 3, "M1", "G1", "WHT", 10, "Allow");
                Row(ws, 4, "M2", "G2", "BLK", 20, "Rejected");
                Row(ws, 5, "M3", "", "", 5, "Allow");                        // two lines in the tool, none named
            })), Planner);
            Assert.True(ok.Ok);
            Assert.Equal((3, 1, 1, 0, 1), (ok.Rows, ok.Created, ok.Updated, ok.Unchanged, ok.Ambiguous));
            Assert.Equal(new[] { "OLD", "M1", "M3", "M3", "M2" }, repo.Lines.Select(l => l.MraNo));   // OLD is kept
            Assert.Equal("ALLOW", repo.Lines[1].Status);
            Assert.StartsWith("Row 5: MRA M3 has several lines", ok.Warnings.Single());
            Assert.Equal(new[] { "ALLOW", "REJECTED" }, ok.ByStatus.Select(s => s.Status));
            Assert.StartsWith("mra-upload MRAUPL-", audit.Lines.Single());
            Assert.Contains("created=1 updated=1 unchanged=0 skipped=1", audit.Lines.Single());

            var notExcel = await svc.UploadAsync(new IngestUpload("x.xlsx", new byte[] { 0x50, 0x4B, 3, 4, 1, 2 }), Planner);
            Assert.False(notExcel.Ok);
            Assert.StartsWith("The file could not be read", notExcel.Errors.Single());
        }
    }
}
