using System.IO.Compression;
using System.Text.Json.Nodes;
using Application.Interfaces.V1;
using Application.Interfaces.V1.Tool2;
using Application.Services.V1.Tool2;
using ClosedXML.Excel;
using DecisionEngine.Model;
using DecisionEngine.Results.Tool2;
using DecisionEngine.Tool2;
using Domain.Entities.Store;
using Domain.Exceptions;
using Domain.Records;
using Infrastructure.Services.V1.Excel;

namespace POGovernance.Tests.Tool2
{
    /// <summary>Service flow (run → save → re-open → export) with in-memory fakes.</summary>
    public class Tool2ServiceTests
    {
        private const string Sc = "S1-early-month";

        private sealed class FakeCache : IDatasetCache
        {
            public EngineData Data = null!;
            public Dictionary<string, string>? AskedVersions;
            public Task<EngineData> GetCurrentAsync(CancellationToken ct = default) => Task.FromResult(Data);
            public Task<EngineData> GetForVersionsAsync(IReadOnlyDictionary<string, string> versions, EngineParams p, CancellationToken ct = default)
            {
                AskedVersions = versions.ToDictionary(k => k.Key, k => k.Value);
                var files = Data.Files.ToDictionary(k => k.Key, k => versions.ContainsKey(k.Key) && k.Key != "colmaster" ? k.Value : null);
                return Task.FromResult(new EngineData(files, p, 1, 1));
            }
            public void Invalidate() { }
        }

        private sealed class FakeLedger : IRequestLedger
        {
            public List<JsonObject> Reqs = new();
            public Task<(List<JsonObject> Requests, int Seq)> GetDayAsync(string date, CancellationToken ct = default) => Task.FromResult((Reqs, 0));
            public Task<JsonObject> SaveAsync(JsonObject request, int seenSeq, bool force, AppUserContext user, CancellationToken ct = default) => throw new NotSupportedException();
        }

        private sealed class FakeClock : IAppClock
        {
            public DateTimeOffset Now { get; set; }
            public string Today => Now.ToString("yyyy-MM-dd");
            public string NowIso => Now.ToString("yyyy-MM-ddTHH:mm:sszzz");
        }

        private sealed class FakeArchive : IRunArchive
        {
            public readonly Dictionary<string, (byte[] Gz, JsonObject Meta)> Runs = new();
            public bool Fail;
            public Task<RunSaveResult> SaveRunAsync(byte[] gzipPayload, JsonObject meta, AppUserContext user, CancellationToken ct = default)
            {
                if (Fail) throw new InvalidOperationException("store down");
                var id = Guid.NewGuid().ToString("N");
                meta["id"] = id; meta["by"] = user.Name; meta["at"] = "2026-10-03T12:00:00+05:30"; meta["runNo"] = "RUN-" + (Runs.Count + 1).ToString("0000");
                Runs[id] = (gzipPayload, meta);
                return Task.FromResult(new RunSaveResult(id, meta["runNo"]!.ToString(), meta));
            }
            public Task<byte[]?> GetRunPayloadAsync(string id, CancellationToken ct = default) => Task.FromResult(Runs.TryGetValue(id, out var r) ? r.Gz : null);
            public Task<JsonObject?> GetRunMetaAsync(string id, CancellationToken ct = default) => Task.FromResult(Runs.TryGetValue(id, out var r) ? r.Meta : null);
        }

        private sealed class FakeManifest : IDatasetManifestReader
        {
            public Task<IReadOnlyDictionary<string, DatasetEntry>> GetCurrentDatasetsAsync(CancellationToken ct = default) =>
                Task.FromResult<IReadOnlyDictionary<string, DatasetEntry>>(DecisionEngine.Model.DatasetKeys.All.ToDictionary(k => k, k => new DatasetEntry { Version = "v-" + k, Rows = 1, UpdatedAt = "2026-10-01" }));
        }

        private static (Tool2Service Svc, FakeCache Cache, FakeArchive Archive) Make(FakeArchive? archive = null)
        {
            var ctx = Tool2Fixture.Context(Sc);
            var cache = new FakeCache { Data = ctx.Data };
            var ledger = new FakeLedger { Reqs = ctx.Requests.ToList() };
            var clock = new FakeClock { Now = Tool2Fixture.Now(Sc) };
            archive ??= new FakeArchive();
            var svc = new Tool2Service(cache, ledger, clock, new Tool2AssessmentEngine(Tool2Fixture.Engine), archive, new FakeManifest(), new Tool2ExcelExporter(), new Tool2RunCache());
            return (svc, cache, archive);
        }

        private static readonly AppUserContext Planner = new("p@x", "Planner One", new[] { "planner" }, false);

        [Fact]
        public async Task Run_saves_a_v114_compatible_run_and_reopens_it_identically()
        {
            var (svc, cache, archive) = Make();
            var res = await svc.RunAsync(Planner);
            Assert.NotNull(res.RunId);
            Assert.Equal("RUN-0001", res.RunNo);
            Assert.Null(res.View.SaveError);
            var (gz, meta) = archive.Runs[res.RunId!];
            Assert.Equal(87, meta["lines"]!.GetValue<int>());
            Assert.Equal(Tool2Fixture.Result(Sc)["summary"]!["atRisk"]!.GetValue<double>(), meta["atRisk"]!.GetValue<double>());
            Assert.Equal("v-f2", meta["dataVersions"]!["f2"]!.GetValue<string>());
            Assert.Null(meta["dataVersions"]!["grc"]); // only files with data are recorded

            var payload = JsonNode.Parse(new GZipStream(new MemoryStream(gz), CompressionMode.Decompress))!.AsObject();
            Assert.Equal("v-f2", payload["dataVersions"]!["f2"]!["version"]!.GetValue<string>());
            Cmp.Check(Tool2Fixture.Result(Sc)["saved"]!["packed"], payload["packed"], "payload");

            // re-open from the store (fresh cache): data versions + run params, same dashboard
            var (svc2, cache2, _) = Make(archive);
            var view = await svc2.GetRunAsync(res.RunId!);
            Assert.True(view.IsHistory);
            Assert.Equal("RUN-0001", view.RunNo);
            Assert.Equal("Planner One", view.By);
            Assert.Equal("📜 Saved run — 87 lines", view.StatusText);
            Assert.Equal(new[] { "Colour Master" }, view.MissingFiles);
            Assert.Equal("v-bsr", cache2.AskedVersions!["bsr"]);
            Assert.Equal(System.Text.Json.JsonSerializer.Serialize(res.View.Lines.Select(l => l.Tooltip)), System.Text.Json.JsonSerializer.Serialize(view.Lines.Select(l => l.Tooltip)));
            Assert.Equal(res.View.Kpis.Select(k => k.Value), view.Kpis.Select(k => k.Value));
            Assert.Equal(res.View.Lines.Select(l => l.RcRecommendation), view.Lines.Select(l => l.RcRecommendation));
        }

        [Fact]
        public async Task Detail_rollup_options_and_exports_work_on_a_run()
        {
            var (svc, _, _) = Make();
            var res = await svc.RunAsync(Planner);
            var det = await svc.GetDetailAsync(res.RunId!, new Tool2DetailFilterDto { Tab = "A", Dec = "Watch List" });
            Assert.All(det.Rows!, i => Assert.Equal("Watch List", res.View.Lines.First(l => l.Idx == i).Decision));
            var roll = await svc.GetRollupAsync(res.RunId!, new Tool2RollupFilterDto { Dec = "Cancel" });
            Assert.All(roll.Lines.Values, l => Assert.Equal("Cancel", l.Decision));
            var opt = await svc.GetOptionsAsync(res.RunId);
            Assert.Equal(Tool2Fixture.Result(Sc)["catHeads"]!.AsArray().Select(n => n!.GetValue<string>()), opt.CatHeads);
            var cur = await svc.GetOptionsAsync(null);
            Assert.Equal(87, cur.AssessableLines);

            var csv = await svc.ExportAsync(new Tool2ExportRequestDto { RunId = res.RunId!, Format = "csv" });
            Assert.Equal("VMM_RaisedPO_Assessment_2026-10-03.csv", csv.FileName);
            Assert.Equal(Tool2Fixture.Result(Sc)["csv"]!["text"]!.GetValue<string>(), System.Text.Encoding.UTF8.GetString(csv.Content));

            var x = await svc.ExportAsync(new Tool2ExportRequestDto { RunId = res.RunId! });
            using (var wb = new XLWorkbook(new MemoryStream(x.Content)))
            {
                Assert.Equal("Summary Dashboard", wb.Worksheets.First().Name);
                Assert.Equal("All Decisions", wb.Worksheets.Last().Name);
                Assert.All(wb.Worksheets, w => Assert.False(w.ShowGridLines));
                var s = wb.Worksheet("Summary Dashboard");
                Assert.Equal("VMM RAISED PO ASSESSMENT — SUMMARY DASHBOARD", s.Cell("A1").GetString());
                Assert.Equal(87, s.Cell("A6").GetDouble());
                Assert.Equal("#,##0", s.Cell("F6").Style.NumberFormat.Format);
                var detail = wb.Worksheets.First(w => w.Name.EndsWith("RC-SC"));
                Assert.True(detail.Cell("A1").HasHyperlink);
                Assert.Equal(1285, detail.Cell("K4").GetDouble());
            }

            var zip = await svc.ExportAsync(new Tool2ExportRequestDto { RunId = res.RunId!, CatHeads = new() { "Sukhdev Singh", "Anita Rao", "Nobody" } });
            Assert.Equal("application/zip", zip.ContentType);
            using var za = new ZipArchive(new MemoryStream(zip.Content));
            Assert.Equal(new[] { "VMM_RaisedPO_Assessment_2026-10-03_Sukhdev_Singh.xlsx", "VMM_RaisedPO_Assessment_2026-10-03_Anita_Rao.xlsx" }, za.Entries.Select(e => e.Name));

            var one = await svc.ExportAsync(new Tool2ExportRequestDto { RunId = res.RunId!, CatHeads = new() { "Sukhdev Singh", "Anita Rao" }, SeparateFiles = false });
            Assert.Contains("scoped to: Sukhdev Singh, Anita Rao", one.Message);

            var ex = await Assert.ThrowsAsync<AppValidationException>(() => svc.ExportAsync(new Tool2ExportRequestDto { RunId = res.RunId!, CatHeads = new() { "Nobody" } }));
            Assert.Equal("No rows match the selected Category Head(s) — export cancelled.", ex.Message);
        }

        [Fact]
        public async Task Failed_save_still_returns_the_run()
        {
            var (svc, _, archive) = Make();
            archive.Fail = true;
            var res = await svc.RunAsync(Planner);
            Assert.StartsWith("⚠ Run finished but was not saved to History: store down", res.View.SaveError);
            Assert.Equal("Assessment complete — 87 lines processed", res.View.Toast);
            Assert.NotEmpty((await svc.GetDetailAsync(res.RunId!, new Tool2DetailFilterDto())).Months!);
        }

        [Fact]
        public async Task Missing_f2_and_unknown_run_give_the_v114_errors()
        {
            var (svc, cache, _) = Make();
            var files = cache.Data.Files.ToDictionary(k => k.Key, k => k.Key == "f2" ? null : k.Value);
            cache.Data = new EngineData(files, cache.Data.Params, 1, 1);
            var ex = await Assert.ThrowsAsync<AppValidationException>(() => svc.RunAsync(Planner));
            Assert.Equal("Please upload the F2 (Released PO) file in Data Setup first", ex.Message);
            await Assert.ThrowsAsync<NotFoundException>(() => svc.GetRunAsync("nope"));
        }

        [Fact]
        public void Small_helpers()
        {
            Assert.Equal("Sukhdev Singh", Tool2Workspace.CatHeadDisplayName("sukhdev.singh@vishalwholesale.co.in"));
            Assert.Equal("Anita Rao", Tool2Workspace.CatHeadDisplayName("anita_rao2@x.in"));
            Assert.Equal("Unassigned", Tool2Workspace.CatHeadDisplayName(null));
            Assert.Equal("July'2026", Tool2Workspace.MonthLabelHuman("07.2026"));
            Assert.Equal("13.2026", Tool2Workspace.MonthLabelHuman("13.2026"));
            Assert.Equal("03.2027", Tool2Js.DateToMMYYYY("2027-03-01"));
            Assert.Equal("06.2026", Tool2Js.DateToMMYYYY("6/1/2026"));
            Assert.Equal("01.2023", Tool2Js.DateToMMYYYY(44927.0));
            Assert.Null(Tool2Js.DateToMMYYYY(1101.0));
            Assert.Equal(new[] { 3 }, Tool2Js.SliceLast(new List<int> { 1, 2, 3 }, 1));
            Assert.Equal(new[] { 1, 2, 3 }, Tool2Js.SliceLast(new List<int> { 1, 2, 3 }, 0));
            Assert.Equal("3/10/2026, 12:00:00 pm", Tool2Workspace.LocaleDateTimeIN(Tool2Fixture.Now(Sc)));
        }
    }
}
