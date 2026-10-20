using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Application.Interfaces.V1;
using Application.Interfaces.V1.Tool2;
using DecisionEngine.Model;
using DecisionEngine.Recommendation;
using DecisionEngine.Results.Tool2;
using DecisionEngine.Tool2;
using Domain.Entities.Store;
using Domain.Exceptions;
using Domain.Records;

namespace Application.Services.V1.Tool2
{
    /// <summary>
    /// In-memory cache of recently built / opened runs (results + the data they are shown with), so the
    /// detail / rollup / export calls of a run do not reload it. Singleton.
    /// </summary>
    public sealed class Tool2RunCache
    {
        private const int Max = 8;
        private readonly ConcurrentDictionary<string, (Tool2Workspace Ws, DateTime At, Tool2ViewDto? View)> _items = new(StringComparer.Ordinal);

        public bool TryGet(string id, out Tool2Workspace ws, out Tool2ViewDto? view)
        {
            if (_items.TryGetValue(id, out var e)) { ws = e.Ws; view = e.View; return true; }
            ws = null!; view = null; return false;
        }

        public void Put(string id, Tool2Workspace ws, Tool2ViewDto? view)
        {
            _items[id] = (ws, DateTime.UtcNow, view);
            while (_items.Count > Max)
            {
                var oldest = _items.OrderBy(kv => kv.Value.At).First().Key;
                _items.TryRemove(oldest, out _);
            }
        }

        public void Remove(string id) => _items.TryRemove(id, out _);
    }

    /// <summary>
    /// See <see cref="ITool2Service"/>. Orchestration only: loads the snapshot (<see cref="IDatasetCache"/>), today's
    /// ledger (<see cref="IRequestLedger"/>) and saved runs (<see cref="IRunArchive"/>); the assessment, views and
    /// export model come from <see cref="ITool2AssessmentEngine"/> (DecisionEngine).
    /// </summary>
    public sealed class Tool2Service : ITool2Service
    {
        private readonly IDatasetCache _cache;
        private readonly IRequestLedger _ledger;
        private readonly IAppClock _clock;
        private readonly ITool2AssessmentEngine _engine;
        private readonly IRunArchive _archive;
        private readonly IDatasetManifestReader _manifest;
        private readonly ITool2ExcelExporter _excel;
        private readonly Tool2RunCache _runs;

        public Tool2Service(IDatasetCache cache, IRequestLedger ledger, IAppClock clock, ITool2AssessmentEngine engine, IRunArchive archive,
                            IDatasetManifestReader manifest, ITool2ExcelExporter excel, Tool2RunCache runs)
        {
            _cache = cache; _ledger = ledger; _clock = clock; _engine = engine; _archive = archive; _manifest = manifest; _excel = excel; _runs = runs;
        }

        private async Task<RecommendationContext> ContextAsync(EngineData data, CancellationToken ct)
        {
            var today = _clock.Today;
            var (requests, _) = await _ledger.GetDayAsync(today, ct);
            return new RecommendationContext(data, requests, today);
        }

        /// <summary>shared.js <c>runSummary(list)</c>: decision counts, qty and ₹ at risk (see <see cref="Tool2AssessmentEngine.RunSummary"/>).</summary>
        public static (JsonObject Counts, double AtRisk, double ValueAtRisk) RunSummary(IReadOnlyList<Tool2LineResult> list) =>
            Tool2AssessmentEngine.RunSummary(list);

        /// <summary>Builds the saved-run payload + X-Run-Meta exactly as <c>Shared.saveRun</c> did.</summary>
        public static (Tool2RunPayload Payload, JsonObject Meta) BuildRunDocument(IReadOnlyList<Tool2LineResult> results, EngineData data,
            IReadOnlyDictionary<string, DatasetEntry> datasets)
        {
            var used = new Dictionary<string, DatasetEntry>(StringComparer.Ordinal);
            var versions = new JsonObject();
            foreach (var k in DatasetKeys.All)
                if (data.Has(k) && datasets.TryGetValue(k, out var e)) { used[k] = e; versions[k] = e.Version; }
            var payload = new Tool2RunPayload
            {
                Params = JsonSerializer.SerializeToNode(data.Params)!.AsObject(),
                DataVersions = used,
                Results = results.ToList()
            };
            var (counts, atRisk, value) = RunSummary(results);
            var meta = new JsonObject
            {
                ["lines"] = results.Count, ["counts"] = counts, ["atRisk"] = atRisk, ["valueAtRisk"] = value,
                ["dataVersions"] = versions, ["T2"] = data.Params.T2, ["D"] = data.Params.D
            };
            return (payload, meta);
        }

        public async Task<Tool2RunResultDto> RunAsync(AppUserContext user, CancellationToken ct = default)
        {
            var data = await _cache.GetCurrentAsync(ct);
            var ctx = await ContextAsync(data, ct);
            var results = EngineErrors.Run(() => _engine.Assess(ctx));
            var ws = _engine.Workspace(results, ctx);
            var view = ws.BuildView();
            view.Toast = "Assessment complete — " + results.Count + " lines processed";
            var datasets = await _manifest.GetCurrentDatasetsAsync(ct);
            var (payload, meta) = BuildRunDocument(results, data, datasets);
            string id;
            try
            {
                var saved = await _archive.SaveRunAsync(payload.ToGzip(), meta, user, ct);
                id = saved.Id;
                view.RunId = saved.Id; view.RunNo = saved.RunNo;
                view.At = saved.Meta["at"]?.ToString(); view.By = saved.Meta["by"]?.ToString();
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                id = "unsaved-" + Guid.NewGuid().ToString("N");
                view.RunId = id;
                view.SaveError = "⚠ Run finished but was not saved to History: " + ex.Message;
            }
            _runs.Put(id, ws, view);
            return new Tool2RunResultDto { RunId = view.RunId, RunNo = view.RunNo, View = view };
        }

        /// <summary>Loads a saved run: payload → data versions + parameters of that moment → workspace.</summary>
        private async Task<(Tool2Workspace Ws, Tool2ViewDto? View, List<string> Missing, JsonObject? Meta)> LoadAsync(string runId, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(runId)) throw new AppValidationException("Bad run id");
            if (_runs.TryGet(runId, out var cached, out var cv)) return (cached, cv, new List<string>(), null);
            var gz = await _archive.GetRunPayloadAsync(runId, ct) ?? throw new NotFoundException("Run not found");
            Tool2RunPayload payload;
            try { payload = Tool2RunPayload.FromGzip(gz); }
            catch (Exception ex) when (ex is JsonException or InvalidDataException) { throw new AppValidationException("Bad run payload"); }
            var p = payload.GetParams();
            var versions = payload.DataVersions.Where(kv => !string.IsNullOrEmpty(kv.Value.Version)).ToDictionary(kv => kv.Key, kv => kv.Value.Version);
            var data = await _cache.GetForVersionsAsync(versions, p, ct);
            var missing = versions.Keys.Where(k => data.Get(k) == null)
                                  .Select(k => DatasetKeys.Labels.TryGetValue(k, out var l) ? l : k).ToList();
            var ctx = await ContextAsync(data, ct);
            var ws = _engine.Workspace(payload.Results, ctx);
            var meta = await _archive.GetRunMetaAsync(runId, ct);
            return (ws, null, missing, meta);
        }

        public async Task<Tool2ViewDto> GetRunAsync(string runId, CancellationToken ct = default)
        {
            var (ws, view, missing, meta) = await LoadAsync(runId, ct);
            if (view != null) return view;
            view = ws.BuildView();
            view.RunId = runId;
            view.IsHistory = true;
            view.MissingFiles = missing;
            view.RunNo = meta?["runNo"]?.ToString();
            view.At = meta?["at"]?.ToString();
            view.By = meta?["by"]?.ToString();
            view.StatusText = "📜 Saved run — " + ws.Results.Count + " lines";
            _runs.Put(runId, ws, view);
            return view;
        }

        public async Task<Tool2DetailViewDto> GetDetailAsync(string runId, Tool2DetailFilterDto filter, CancellationToken ct = default) =>
            (await LoadAsync(runId, ct)).Ws.Detail(filter);

        public async Task<Tool2RollupDto> GetRollupAsync(string runId, Tool2RollupFilterDto filter, CancellationToken ct = default) =>
            (await LoadAsync(runId, ct)).Ws.Rollup(filter);

        public async Task<Tool2OptionsDto> GetOptionsAsync(string? runId, CancellationToken ct = default)
        {
            if (!string.IsNullOrWhiteSpace(runId))
            {
                var ws = (await LoadAsync(runId, ct)).Ws;
                return new Tool2OptionsDto
                {
                    Params = ws.ParamsDto(), HasF2 = ws.Data.Has(DatasetKeys.F2), AssessableLines = ws.Results.Count,
                    CatHeads = ws.AllCatHeadNames(), DetailFilters = ws.DetailFilters(), RollupFilters = ws.RollupFilters()
                };
            }
            var data = await _cache.GetCurrentAsync(ct);
            var ctx = new RecommendationContext(data, null, _clock.Today);
            var empty = _engine.Workspace(Array.Empty<Tool2LineResult>(), ctx);
            return new Tool2OptionsDto { Params = empty.ParamsDto(), HasF2 = data.Has(DatasetKeys.F2), AssessableLines = _engine.AssessableLines(data) };
        }

        public async Task<Tool2FileDto> ExportAsync(Tool2ExportRequestDto request, CancellationToken ct = default)
        {
            var ws = (await LoadAsync(request.RunId, ct)).Ws;
            return Export(ws, request, _clock.Now, _excel);
        }

        /// <summary>The export of a workspace (public for tests). Engine rule errors become <see cref="AppValidationException"/>.</summary>
        public static Tool2FileDto Export(Tool2Workspace ws, Tool2ExportRequestDto request, DateTimeOffset now, ITool2ExcelExporter excel) =>
            EngineErrors.Run(() => ExportFiles(ws, request, now, excel));

        private static Tool2FileDto ExportFiles(Tool2Workspace ws, Tool2ExportRequestDto request, DateTimeOffset now, ITool2ExcelExporter excel)
        {
            var date = Tool2Workspace.FileDate(now);
            if (string.Equals(request.Format, "csv", StringComparison.OrdinalIgnoreCase))
                return new Tool2FileDto
                {
                    FileName = "VMM_RaisedPO_Assessment_" + date + ".csv", ContentType = "text/csv",
                    Content = Encoding.UTF8.GetBytes(ws.Csv()), Message = "CSV exported ✓"
                };
            var names = request.CatHeads?.Where(n => !string.IsNullOrEmpty(n)).Distinct().ToList() ?? new List<string>();
            if (names.Count > 1 && request.SeparateFiles)
            {
                var files = new List<(string, byte[])>();
                foreach (var n in names)
                {
                    if (!ws.Results.Any(r => ws.CatHeadFor(r) == n)) continue;
                    var wbn = ws.BuildWorkbook(new[] { n }, now);
                    files.Add(("VMM_RaisedPO_Assessment_" + date + "_" + SafeFile(n) + ".xlsx", excel.Write(wbn)));
                }
                if (files.Count == 0) throw new AppValidationException("No rows match the selected Category Head(s) — export cancelled.");
                return new Tool2FileDto
                {
                    FileName = "VMM_RaisedPO_Assessment_" + date + "_CategoryHeads.zip", ContentType = "application/zip",
                    Content = excel.Zip(files), Message = "Excel exported ✓ — " + files.Count + " workbooks (one per Category Head)"
                };
            }
            var wb = ws.BuildWorkbook(names, now);
            return new Tool2FileDto
            {
                FileName = wb.FileName, ContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                Content = excel.Write(wb), Message = wb.Message
            };
        }

        private static string SafeFile(string s) => string.Concat(s.Select(c => char.IsLetterOrDigit(c) ? c : '_'));
    }
}
