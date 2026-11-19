using Application.DTOs.Ingest;
using Application.Interfaces.V1;
using Application.Services.V1.Ingest;
using ClosedXML.Excel;
using DecisionEngine.Common;
using DecisionEngine.Model;
using Domain.Entities.Store;
using Domain.Exceptions;
using Domain.Records;
using Infrastructure.Services.V1.Excel;

namespace POGovernance.Tests.Ingest
{
    /// <summary>
    /// The Entry / Mid / Fashion mapping upload (Data Setup, dataset "emf"): replaces the previous mapping, keeps only rows
    /// tagged Entry, Mid or Fashion, and lists every rejected row with its Excel row number and reason.
    /// </summary>
    public class EmfUploadTests
    {
        private sealed class FakeStore : IDatasetStore
        {
            public List<(string Source, List<string> Files)> Batches { get; } = new();
            public List<(string Key, DatasetPayload Payload, string Label)> Saves { get; } = new();

            public Task<string> NewUploadBatchAsync(string source, IEnumerable<string> sourceFiles, AppUserContext user, string note = "", CancellationToken ct = default)
            {
                Batches.Add((source, sourceFiles.ToList()));
                return Task.FromResult("UPL-20261119-000" + Batches.Count);
            }

            public Task<DatasetSaveResult> SaveVersionAsync(string key, DatasetPayload payload, string label, string? uploadId, AppUserContext user, CancellationToken ct = default)
            {
                Saves.Add((key, payload, label));
                return Task.FromResult(new DatasetSaveResult(Saves.Count, new DatasetEntry
                {
                    Version = "v" + Saves.Count, Rows = payload.Rows.Count, Label = label, UploadId = uploadId, UpdatedBy = user.Name, UpdatedAt = "2026-11-19T11:00:00+05:30"
                }));
            }
        }

        private static readonly AppUserContext Planner = new("p@x.in", "Planner One", new[] { "planner" }, false);

        private static readonly string[] Header = { "Brand MC Concat", "At_Brand", "Division", "MC Code", "at_Desc", "Flag", "MRP", "EMF" };

        /// <summary>A workbook laid out like the business file: three blank rows, the header on row 4, data from row 5.</summary>
        private static byte[] Book(params object?[][] rows)
        {
            using var wb = new XLWorkbook();
            var ws = wb.AddWorksheet("Sheet1");
            for (var c = 0; c < Header.Length; c++) ws.Cell(4, c + 1).Value = Header[c];
            for (var r = 0; r < rows.Length; r++)
                for (var c = 0; c < rows[r].Length; c++)
                    if (rows[r][c] != null) ws.Cell(5 + r, c + 1).Value = XLCellValue.FromObject(rows[r][c]);
            using var ms = new MemoryStream();
            wb.SaveAs(ms);
            return ms.ToArray();
        }

        private static object?[] Row(string brand, object mc, string flag, object mrp, string tag, string div = "MENS ETHNIC", string desc = "MSE_SW_T-SHIRT F/S") =>
            new object?[] { brand + "-" + mc, brand, div, mc, desc, flag, mrp, tag };

        private static IngestUpload Upload(byte[] content, string name = "EMF.xlsx") => new(name, content);

        // ── tags ───────────────────────────────────────────────────────────

        [Theory]
        [InlineData("Entry", "Entry")]
        [InlineData("1.ENTRY ", "Entry")]
        [InlineData("2.MID", "Mid")]
        [InlineData("2. Mid", "Mid")]
        [InlineData("3.FASHION", "Fashion")]
        [InlineData(" fashion", "Fashion")]
        [InlineData("Premium", null)]
        [InlineData("4.LUXURY", null)]
        [InlineData("Entry/Mid", null)]
        [InlineData("1.", null)]
        [InlineData("", null)]
        [InlineData(null, null)]
        public void Only_Entry_Mid_and_Fashion_are_tags(string? raw, string? tag) => Assert.Equal(tag, EmfMapping.NormalizeTag(raw));

        // ── the upload ─────────────────────────────────────────────────────

        private static byte[] Mixed() => Book(
            Row("NON-PL", 113010003, "Normal", 129, "1.ENTRY "),       // row 5  ok
            Row("NON-PL", 113010003, "Normal", 159, "2.MID"),          // row 6  ok
            Row("NON-PL", 113010003, "Normal", 399, "4.PREMIUM"),      // row 7  other tag
            Row("NON-PL", 113010003, "Normal", 499, ""),               // row 8  blank tag
            Row("BELONG", 121020003, "MULTI PACK", 299, "3.FASHION"),  // row 9  ok
            Row("NON-PL", 136020001, "Normal", 79, "1.ENTRY "),        // row 10 ok
            Row("NON-PL", 136020001, "Normal", 79, "1.ENTRY "),        // row 11 exact repeat → loaded once
            Row("MAVIE", 114010001, "Normal", 1199, "2.MID"),          // row 12 conflict with row 13
            Row("MAVIE", 114010001, "NORMAL", 1199, "3.FASHION"),      // row 13 same key (flag case ignored), other tag
            new object?[] { "UST-1", "UST", "KIDS", 115, "x", "Normal", null, "2.MID" });  // row 14 MRP missing

        [Fact]
        public void Rows_with_another_tag_are_rejected_and_listed_the_rest_is_stored_normalised()
        {
            var r = new DataIngestService(new ExcelWorkbookReader()).ParseEmf(new[] { Upload(Mixed()) });
            var rep = r.Report;
            Assert.Null(r.Error);
            Assert.Equal("✓ 4 rows — EMF.xlsx · 5 rejected", rep.Status);
            Assert.Equal("Loaded 4 rows — 5 rejected, see the list", rep.Toasts.Single());
            Assert.Equal(1, rep.DuplicatesIgnored);

            Assert.Equal(new[] { 7, 8, 12, 13, 14 }, rep.Rejected.Select(x => x.Row));
            Assert.Equal("Tag \"4.PREMIUM\" is not Entry, Mid or Fashion", rep.Rejected[0].Reason);
            Assert.Equal(("NON-PL", "113010003", "Normal", "399", "4.PREMIUM"), (rep.Rejected[0].Brand, rep.Rejected[0].Mc, rep.Rejected[0].Flag, rep.Rejected[0].Mrp, rep.Rejected[0].Tag));
            Assert.Equal("No tag — must be Entry, Mid or Fashion", rep.Rejected[1].Reason);
            Assert.Equal("Same Brand, MC, Flag and MRP is tagged Mid and Fashion in the file", rep.Rejected[2].Reason);
            Assert.Equal(rep.Rejected[2].Reason, rep.Rejected[3].Reason);
            Assert.Equal("MRP missing", rep.Rejected[4].Reason);
            Assert.All(rep.Rejected, x => Assert.Equal(("EMF.xlsx", "Sheet1"), (x.File, x.Sheet)));

            var k = Assert.Single(r.Keys);
            Assert.Equal(DatasetKeys.Emf, k.Key);
            Assert.Equal(4, k.Rows);
            var rows = k.Payload.Rows;
            Assert.Equal(new[] { "Brand Type", "Division", "MC CD", "MC DESC", "Flag", "MRP", "EMF" }, rows[0].Keys);
            Assert.Equal(new[] { "Entry", "Mid", "Fashion", "Entry" }, rows.Select(x => x.Col("EMF")));
            Assert.Equal(("NON-PL", "113010003", "Normal", "129", "MSE_SW_T-SHIRT F/S"),
                         (rows[0].Col("Brand Type"), rows[0].Col("MC CD"), rows[0].Col("Flag"), rows[0].Col("MRP"), rows[0].Col("MC DESC")));
        }

        [Fact]
        public async Task Each_upload_replaces_the_previous_mapping_and_records_who_and_how_many()
        {
            Assert.Contains(DatasetKeys.Emf, LegacyImport.SlotKeys);
            Assert.False(LegacyImport.IsMultiKey(DatasetKeys.Emf));     // never added to the previous rows
            Assert.Equal("Entry / Mid / Fashion Mapping", DatasetKeys.Labels[DatasetKeys.Emf]);

            var store = new FakeStore();
            var svc = new DataIngestService(new ExcelWorkbookReader(), store);
            await svc.ImportFilesAsync(DatasetKeys.Emf, new[] { Upload(Mixed()) }, Planner, append: true);
            var second = await svc.ImportFilesAsync(DatasetKeys.Emf, new[] { Upload(Book(Row("NON-PL", 113010003, "Normal", 129, "Fashion")), "EMF-v2.xlsx") }, Planner, append: true);

            Assert.Equal(2, store.Saves.Count);
            Assert.Equal(4, store.Saves[0].Payload.Rows.Count);
            Assert.Single(store.Saves[1].Payload.Rows);                   // the whole mapping is the new file
            Assert.Equal("✓ 1 rows — EMF-v2.xlsx", store.Saves[1].Label);
            var saved = Assert.Single(second.Saved);
            Assert.Equal((1, "Planner One", "2026-11-19T11:00:00+05:30"), (saved.Entry!.Rows, saved.Entry.UpdatedBy, saved.Entry.UpdatedAt));
            Assert.Empty(second.Report.Rejected);
        }

        [Fact]
        public async Task Nothing_is_saved_when_every_row_is_rejected_and_the_list_still_comes_back()
        {
            var store = new FakeStore();
            var svc = new DataIngestService(new ExcelWorkbookReader(), store);
            var ex = await Assert.ThrowsAsync<AppException>(() => svc.ImportFilesAsync(DatasetKeys.Emf,
                new[] { Upload(Book(Row("NON-PL", 1, "Normal", 99, "Premium"), Row("NON-PL", 1, "Normal", 129, "Basic"))) }, Planner));
            Assert.Equal(400, ex.Status);
            Assert.Equal("Not loaded: all 2 rows were rejected (see the list) — the previous mapping is kept", ex.Message);
            Assert.Empty(store.Batches);
            var resp = Assert.IsType<IngestResponse>(ex.Payload);
            Assert.Equal(new[] { 5, 6 }, resp.Report.Rejected.Select(x => x.Row));
            Assert.True(resp.Report.StatusIsError);
        }

        [Fact]
        public void A_file_without_the_columns_is_refused()
        {
            using var wb = new XLWorkbook();
            var ws = wb.AddWorksheet("Data");
            ws.Cell(1, 1).Value = "Generic Material"; ws.Cell(1, 2).Value = "Colour"; ws.Cell(2, 1).Value = "1100001"; ws.Cell(2, 2).Value = "RED";
            using var ms = new MemoryStream();
            wb.SaveAs(ms);
            var r = new DataIngestService(new ExcelWorkbookReader()).ParseEmf(new[] { Upload(ms.ToArray(), "wrong.xlsx") });
            Assert.Equal(EmfImport.ColumnsMissing, r.Error);
            Assert.Empty(r.Keys);
        }

        // ── lookup (for the next step of the multi concept rule) ───────────

        [Fact]
        public void Stored_rows_are_found_by_Brand_MC_Flag_and_MRP()
        {
            var r = new DataIngestService(new ExcelWorkbookReader()).ParseEmf(new[] { Upload(Mixed()) });
            var data = new EngineData(new Dictionary<string, IReadOnlyList<SheetRow>?> { [DatasetKeys.Emf] = r.Keys[0].Payload.Rows }, new EngineParams(), 1, 1);
            Assert.Equal("Entry", EmfMapping.Lookup(data, "non-pl", "113010003.0", "NORMAL", "129"));
            Assert.Equal("Fashion", EmfMapping.Lookup(data, "BELONG", "121020003", "Multi Pack", "299.0"));
            Assert.Null(EmfMapping.Lookup(data, "NON-PL", "113010003", "Normal", "399"));    // rejected row: not mapped
            Assert.Null(EmfMapping.Lookup(data, "MAVIE", "114010001", "Normal", "1199"));    // conflicting rows: not mapped
            Assert.Null(EmfMapping.Lookup(new EngineData(new Dictionary<string, IReadOnlyList<SheetRow>?>(), new EngineParams(), 1, 1), "NON-PL", "1", "Normal", "1"));
        }
    }
}
