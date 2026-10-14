using Application.DTOs.Ingest;
using Application.Interfaces.V1;
using Application.Services.V1.Ingest;
using Domain.Entities.Engine;
using Domain.Entities.Store;
using Domain.Exceptions;
using Domain.Records;
using Infrastructure.Services.V1.Excel;

namespace POGovernance.Tests.Ingest
{
    public class IngestCommitTests
    {
        private sealed class FakeStore : IDatasetStore
        {
            public List<(string Source, List<string> Files)> Batches { get; } = new();
            public List<(string Key, DatasetPayload Payload, string Label, string? UploadId)> Saves { get; } = new();

            public Task<string> NewUploadBatchAsync(string source, IEnumerable<string> sourceFiles, AppUserContext user, string note = "", CancellationToken ct = default)
            {
                Batches.Add((source, sourceFiles.ToList()));
                return Task.FromResult("UPL-20261014-000" + Batches.Count);
            }

            public Task<DatasetSaveResult> SaveVersionAsync(string key, DatasetPayload payload, string label, string? uploadId, AppUserContext user, CancellationToken ct = default)
            {
                Saves.Add((key, payload, label, uploadId));
                return Task.FromResult(new DatasetSaveResult(Saves.Count, new DatasetEntry { Version = "v" + Saves.Count, Rows = payload.Rows.Count, Label = label, UploadId = uploadId }));
            }
        }

        private static readonly AppUserContext Planner = new("p@x.in", "Planner", new[] { "planner" }, false);
        private static IngestUpload Upload(string name) => new(name, File.ReadAllBytes(IngestTestPaths.Fixture(name)));

        [Fact]
        public async Task Raw_import_saves_every_key_in_one_batch()
        {
            var store = new FakeStore();
            var svc = new DataIngestService(new ExcelWorkbookReader(), store);
            var resp = await svc.ImportRawAsync(new[] { Upload("PO_raw.xlsx"), Upload("Stock_raw.xlsx") }, Planner);
            Assert.Single(store.Batches);
            Assert.Equal("raw", store.Batches[0].Source);
            Assert.Equal(new[] { "PO_raw.xlsx", "Stock_raw.xlsx" }, store.Batches[0].Files);
            Assert.Equal(new[] { "f2", "f1" }, store.Saves.Select(s => s.Key));
            Assert.All(store.Saves, s => Assert.Equal("UPL-20261014-0001", s.UploadId));
            Assert.Equal("✓ 3 rows (via Raw Import)", store.Saves[0].Label);
            Assert.Equal("UPL-20261014-0001", resp.UploadId);
            Assert.Equal(2, resp.Saved.Count);
        }

        [Fact]
        public async Task Grc_saves_meta_period_type()
        {
            var store = new FakeStore();
            var svc = new DataIngestService(new ExcelWorkbookReader(), store);
            var resp = await svc.ImportGrcAsync(Upload("GRC_weekly.xlsx"), Planner);
            Assert.Equal("grc", store.Batches[0].Source);
            var p = store.Saves.Single().Payload;
            Assert.Equal("week", p.MetaString("grcPeriodType"));
            Assert.Equal(new[] { "brand", "gm", "artdesc", "colour", "period", "periodType", "qty", "value" }, p.Rows[0].Keys);
            Assert.IsType<double>(p.Rows[0]["qty"]);
            Assert.Equal("week", resp.Report.GrcPeriodType);
        }

        [Fact]
        public async Task Failed_parse_creates_no_batch_and_returns_400()
        {
            var store = new FakeStore();
            var svc = new DataIngestService(new ExcelWorkbookReader(), store);
            var ex = await Assert.ThrowsAsync<AppException>(() => svc.ImportGrcAsync(Upload("F1_STOCK_portal.xlsx"), Planner));
            Assert.Equal(400, ex.Status);
            Assert.Equal("Could not find the \"Cal. year / month\" row — is this the right file?", ex.Message);
            Assert.Empty(store.Batches);
        }

        [Fact]
        public async Task Unknown_slot_key_is_rejected()
        {
            var svc = new DataIngestService(new ExcelWorkbookReader(), new FakeStore());
            await Assert.ThrowsAsync<AppValidationException>(() => svc.ImportFilesAsync("grc", new[] { Upload("GRC_weekly.xlsx") }, Planner));
        }

        [Fact]
        public void Date_helpers_match_index_html()
        {
            Assert.True(IngestDateHelpers.IsMMYYYY(" 06.2026 "));
            Assert.Equal("2026-06", IngestDateHelpers.ToYM("06.2026"));
            Assert.True(IngestDateHelpers.IsDayCol("Day 12"));
            Assert.True(IngestDateHelpers.IsDayCol("d7"));
            Assert.Equal(12, IngestDateHelpers.DayColNum("DAY12"));
            Assert.Equal("06.2026", IngestDateHelpers.DateToMMYYYY("6/1/2026"));
            Assert.Equal("06.2026", IngestDateHelpers.DateToMMYYYY("2026-06-01"));
            Assert.Equal("06.2026", IngestDateHelpers.DateToMMYYYY(46174.0));
            Assert.Null(IngestDateHelpers.DateToMMYYYY(30000.0));
        }
    }
}
