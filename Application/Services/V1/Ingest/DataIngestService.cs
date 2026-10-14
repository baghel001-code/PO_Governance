using System.IO.Compression;
using System.Text.Json;
using System.Text.RegularExpressions;
using Application.DTOs.Ingest;
using Application.Interfaces.V1;
using Application.Interfaces.V1.Ingest;
using Domain.Constants;
using Domain.Entities.Common;
using Domain.Entities.Engine;
using Domain.Exceptions;
using Domain.Records;

namespace Application.Services.V1.Ingest
{
    /// <summary>
    /// The planner's Data Setup uploads, moved from the browser to the server. Parsing reproduces
    /// index.html loadFile / loadZipBundle / handleRawImport / loadGRCFile (see <see cref="LegacyImport"/>);
    /// saving reproduces shared.js pushFiles: one upload batch per action, then one stored version per
    /// dataset key with the slot's status text as label (X-Label) and, for grc, meta {grcPeriodType}.
    /// </summary>
    public sealed class DataIngestService : IDataIngestService
    {
        private const int LabelMax = 200;

        private readonly IWorkbookReader _reader;
        private readonly IDatasetStore? _store;
        private readonly IDatasetCache? _cache;

        public DataIngestService(IWorkbookReader reader, IDatasetStore? store = null, IDatasetCache? cache = null)
        {
            _reader = reader;
            _store = store;
            _cache = cache;
        }

        private static string Label(string s) => s.Length > LabelMax ? s[..LabelMax] : s;

        private static DatasetPayload Payload(List<SheetRow> rows, string? grcPeriodType = null)
        {
            var p = new DatasetPayload { Rows = rows };
            if (grcPeriodType != null)
                p.Meta = new Dictionary<string, JsonElement> { ["grcPeriodType"] = JsonSerializer.SerializeToElement(grcPeriodType) };
            return p;
        }

        // ── loadFile ───────────────────────────────────────────────────

        /// <inheritdoc />
        /// <remarks>Port of index.html loadFile / readOne (line 2016).</remarks>
        public IngestResult ParseFiles(string key, IReadOnlyList<IngestUpload> files, IReadOnlyList<SheetRow>? existingRows = null)
        {
            var result = new IngestResult { Source = IngestSources.Files, SourceFiles = files.Select(f => f.FileName).ToList() };
            var rep = result.Report;
            var merged = new List<SheetRow>();
            var errors = new List<string>();
            foreach (var f in files)
            {
                try
                {
                    var wb = _reader.Read(f.Content, f.FileName);
                    var rows = LegacyImport.ParseWorkbookForKey(wb, key);
                    merged.AddRange(rows);
                    rep.Files.Add(new IngestFileRows(f.FileName, rows.Count, true));
                }
                catch (Exception e) when (e is not OperationCanceledException)
                {
                    errors.Add(f.FileName);
                    rep.Files.Add(new IngestFileRows(f.FileName, 0, false, e.Message));
                }
            }
            if (merged.Count > 0)
            {
                var multi = LegacyImport.IsMultiKey(key);
                List<SheetRow> all = multi && existingRows is { Count: > 0 } ? existingRows.Concat(merged).ToList() : merged;
                var totalNow = all.Count;
                var lbl = multi
                    ? $"✓ {totalNow} rows total (+{merged.Count} from this upload)"
                    : (files.Count > 1 ? $"✓ {merged.Count} rows from {files.Count} files" : $"✓ {merged.Count} rows — {files[0].FileName}");
                rep.Status = lbl;
                rep.SlotStatus[key] = lbl;
                rep.Toasts.Add($"Loaded {merged.Count} rows");
                result.Keys.Add(new IngestKeyPayload { Key = key, Label = Label(lbl), Rows = all.Count, NewRows = merged.Count, Payload = Payload(all) });
            }
            if (errors.Count > 0)
            {
                if (merged.Count == 0)
                {
                    rep.Status = "✗ Error reading file(s)";
                    rep.StatusIsError = true;
                    rep.SlotStatus[key] = rep.Status;
                }
                var msg = "Error: " + string.Join(", ", errors);
                rep.Toasts.Add(msg);
                rep.Errors.Add(msg);
                if (merged.Count == 0) result.Error = msg;
            }
            return result;
        }

        // ── loadZipBundle ──────────────────────────────────────────────

        private static readonly Regex DataExt = new(@"\.(xlsx|xls|xlsb|csv)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        /// <inheritdoc />
        /// <remarks>Port of index.html loadZipBundle (line 2168).</remarks>
        public IngestResult ParseZip(IngestUpload zip, Func<string, bool>? hasCurrent = null)
        {
            var result = new IngestResult { Source = IngestSources.Zip, SourceFiles = new List<string> { zip.FileName } };
            var rep = result.Report;

            // JSZip.loadAsync → entry name → bytes (later duplicates replace earlier ones in place).
            var entryOrder = new List<string>();
            var entries = new Dictionary<string, Func<byte[]>>(StringComparer.Ordinal);
            ZipArchive archive;
            try
            {
                archive = new ZipArchive(new MemoryStream(zip.Content), ZipArchiveMode.Read);
                foreach (var e in archive.Entries)
                {
                    var name = e.FullName;
                    var isDir = name.EndsWith('/') || name.EndsWith('\\');
                    if (isDir) continue;
                    if (!entries.ContainsKey(name)) entryOrder.Add(name);
                    var entry = e;
                    entries[name] = () =>
                    {
                        using var s = entry.Open();
                        using var ms = new MemoryStream();
                        s.CopyTo(ms);
                        return ms.ToArray();
                    };
                }
            }
            catch (Exception e) when (e is InvalidDataException or IOException or ArgumentException)
            {
                rep.Status = "✗ Could not read zip file";
                rep.StatusIsError = true;
                var t = "Error reading zip: " + e.Message;
                rep.Toasts.Add(t);
                rep.Errors.Add(t);
                result.Error = t;
                return result;
            }

            var byKey = new List<KeyValuePair<string, List<SheetRow>>>();
            void AddRows(string k, IEnumerable<SheetRow> rows)
            {
                var i = byKey.FindIndex(x => x.Key == k);
                if (i < 0) byKey.Add(new(k, rows.ToList()));
                else byKey[i].Value.AddRange(rows);
            }

            using (archive)
            {
                foreach (var name in entryOrder)
                {
                    var bas = name.Split('/')[^1];
                    if (!DataExt.IsMatch(bas)) continue;
                    WorkbookData wb;
                    try
                    {
                        wb = _reader.Read(entries[name](), bas);
                    }
                    catch (Exception e) when (e is not OperationCanceledException)
                    {
                        rep.Unmatched.Add(bas + " (read error: " + e.Message + ")");
                        continue;
                    }

                    var raw = LegacyImport.ProcessRawWorkbook(wb, bas);
                    if (raw.Detected.Count > 0)
                    {
                        foreach (var kv in raw.ByKey) AddRows(kv.Key, kv.Value);
                        rep.Detected.AddRange(raw.Detected);
                        continue;
                    }

                    var rule = LegacyImport.ZipRules.FirstOrDefault(r => r.Pattern.IsMatch(bas));
                    if (rule != null)
                    {
                        try
                        {
                            var rows = LegacyImport.ParseWorkbookForKey(wb, rule.Key);
                            AddRows(rule.Key, rows);
                            rep.Matched.Add(new IngestMatchedFile(bas, rule.Key, rule.Label, rows.Count));
                        }
                        catch (Exception e) when (e is not OperationCanceledException)
                        {
                            rep.Unmatched.Add(bas + " (read error: " + e.Message + ")");
                        }
                    }
                    else rep.Unmatched.Add(bas);
                }
            }

            foreach (var (key, rows) in byKey)
            {
                var st = $"✓ {rows.Count} rows (via zip)";
                rep.SlotStatus[key] = st;
                if (rows.Count == 0)
                {
                    rep.Warnings.Add($"{key}: the matched file had no data rows — v1.1.4 would have cleared this dataset; it was left unchanged.");
                    continue;
                }
                result.Keys.Add(new IngestKeyPayload { Key = key, Label = Label(st), Rows = rows.Count, NewRows = rows.Count, Payload = Payload(rows) });
            }

            var totalRows = byKey.Sum(k => k.Value.Count);
            var keysHit = byKey.Count;
            rep.Status = $"✓ {entryOrder.Count} file(s) in zip — {keysHit} categories matched, {totalRows} rows";
            rep.Toasts.Add($"Zip loaded: {keysHit} categories, {totalRows} rows");

            // REQUIRED-FILE CHECK against S.files after the commit (zip keys + what is already stored).
            var missing = LegacyImport.RequiredKeys.Where(k =>
            {
                var i = byKey.FindIndex(x => x.Key == k);
                if (i >= 0) return byKey[i].Value.Count == 0;
                return !(hasCurrent?.Invoke(k) ?? false);
            }).ToList();
            rep.MissingRequired = missing;
            if (missing.Count > 0)
            {
                var names = missing.Select(k => LegacyImport.RequiredLabels.TryGetValue(k, out var l) ? l : k).ToList();
                rep.Status = $"⚠ Zip loaded but {missing.Count} required file(s) missing";
                rep.StatusIsError = true;
                rep.Alerts.Add("⚠ Required file(s) missing from this zip:\n\n• " + string.Join("\n• ", names) +
                    "\n\nThe zip was still loaded for everything it did contain, but you must add the missing file(s) — either re-zip with them included, or upload them individually in the slots below — before running Tool 1 or Tool 2.");
                rep.Lines.Add("⚠ Missing required files: " + string.Join(", ", names));
                rep.Lines.Add("");
            }

            rep.Lines.Add("Matched by filename (already portal-format):");
            if (rep.Matched.Count == 0) rep.Lines.Add("(none)");
            foreach (var m in rep.Matched) rep.Lines.Add($"✓ {m.Name} → {m.Label} ({m.Rows} rows)");
            rep.Lines.Add("");
            rep.Lines.Add("Auto-converted (raw system exports):");
            if (rep.Detected.Count == 0) rep.Lines.Add("(none)");
            foreach (var d in rep.Detected) rep.Lines.Add($"✓ {d.File} → {d.Sheet} → {RawLabel(d.Key)} ({d.Rows} rows)");
            if (rep.Unmatched.Count > 0)
            {
                rep.Lines.Add("");
                rep.Lines.Add("Not recognized at all — upload these individually below:");
                foreach (var u in rep.Unmatched) rep.Lines.Add($"✗ {u}");
            }
            return result;
        }

        private static string RawLabel(string key) => LegacyImport.RawKeyLabels.TryGetValue(key, out var l) ? l : key;

        // ── handleRawImport ────────────────────────────────────────────

        /// <inheritdoc />
        /// <remarks>Port of index.html handleRawImport (line 3643).</remarks>
        public IngestResult ParseRaw(IReadOnlyList<IngestUpload> files)
        {
            var result = new IngestResult { Source = IngestSources.Raw, SourceFiles = files.Select(f => f.FileName).ToList() };
            var rep = result.Report;
            var byKey = new List<KeyValuePair<string, List<SheetRow>>>();
            foreach (var f in files)
            {
                WorkbookData wb;
                try { wb = _reader.Read(f.Content, f.FileName); }
                catch (Exception e) when (e is not OperationCanceledException)
                {
                    rep.Skipped.Add(new IngestSkippedSheet(f.FileName, "—", "could not read file: " + e.Message));
                    continue;
                }
                var res = LegacyImport.ProcessRawWorkbook(wb, f.FileName);
                foreach (var (k, rows) in res.ByKey)
                {
                    var i = byKey.FindIndex(x => x.Key == k);
                    if (i < 0) byKey.Add(new(k, new List<SheetRow>(rows))); else byKey[i].Value.AddRange(rows);
                }
                rep.Detected.AddRange(res.Detected);
                rep.Skipped.AddRange(res.Skipped);
            }
            foreach (var (key, rows) in byKey)
            {
                var st = $"✓ {rows.Count} rows (via Raw Import)";
                rep.SlotStatus[key] = st;
                result.Keys.Add(new IngestKeyPayload { Key = key, Label = Label(st), Rows = rows.Count, NewRows = rows.Count, Payload = Payload(rows) });
            }
            var totalRows = byKey.Sum(k => k.Value.Count);
            if (totalRows > 0)
            {
                rep.Status = $"✓ {totalRows} rows converted across {byKey.Count} input(s)";
                rep.Toasts.Add($"Raw import: {totalRows} rows converted");
            }
            else
            {
                rep.Status = "✗ Nothing recognized — see report below";
                rep.StatusIsError = true;
            }
            rep.Lines.Add("Detected & converted:");
            if (rep.Detected.Count == 0) rep.Lines.Add("(none)");
            foreach (var d in rep.Detected) rep.Lines.Add($"✓ {d.File} → {d.Sheet} → {RawLabel(d.Key)} ({d.Rows} rows)");
            if (rep.Skipped.Count > 0)
            {
                rep.Lines.Add("");
                rep.Lines.Add("Skipped:");
                foreach (var s in rep.Skipped) rep.Lines.Add($"— {s.File} → {s.Sheet}: {s.Reason}");
            }
            return result;
        }

        // ── loadGRCFile ────────────────────────────────────────────────

        /// <inheritdoc />
        /// <remarks>Port of index.html loadGRCFile (line 2557) + _renderGRCMonthPicker (latest period pre-selected).</remarks>
        public IngestResult ParseGrc(IngestUpload file)
        {
            var result = new IngestResult { Source = IngestSources.Grc, SourceFiles = new List<string> { file.FileName } };
            var rep = result.Report;
            try
            {
                var wb = _reader.Read(file.Content, file.FileName);
                var grc = LegacyImport.ParseGrc(wb);
                var periodWord = grc.PeriodType == "week" ? "weeks" : "months";
                var st = $"✓ {Js.GroupIN(grc.Rows.Count)} rows across {grc.Periods.Count} {periodWord} ({grc.Periods[0]} – {grc.Periods[^1]})";
                rep.Status = st;
                rep.SlotStatus[DatasetKeys.Grc] = st;
                rep.Toasts.Add($"GRC loaded ✓ — {grc.Periods.Count} {periodWord} found");
                rep.GrcPeriodType = grc.PeriodType;
                rep.Periods = grc.Periods;
                rep.DefaultPeriod = grc.Periods[^1];
                result.Keys.Add(new IngestKeyPayload
                {
                    Key = DatasetKeys.Grc, Label = Label(st), Rows = grc.Rows.Count, NewRows = grc.Rows.Count,
                    Payload = Payload(grc.Rows, grc.PeriodType)
                });
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                rep.Status = "✗ " + e.Message;
                rep.StatusIsError = true;
                rep.SlotStatus[DatasetKeys.Grc] = rep.Status;
                rep.Errors.Add(e.Message);
                result.Error = e.Message;
            }
            return result;
        }

        // ── save (shared.js pushFiles) ─────────────────────────────────

        /// <inheritdoc />
        public async Task<IngestResponse> CommitAsync(IngestResult result, AppUserContext user, CancellationToken ct = default)
        {
            var resp = new IngestResponse { Source = result.Source, SourceFiles = result.SourceFiles, Report = result.Report };
            if (result.Keys.Count == 0)
            {
                if (result.Error != null) throw new AppException(400, result.Error, resp);
                return resp;
            }
            if (_store == null) throw new ServiceUnavailableException("Data storage is not configured");
            var uploadId = await _store.NewUploadBatchAsync(result.Source, result.SourceFiles.Select(f => f.Length > 200 ? f[..200] : f), user, "", ct);
            resp.UploadId = uploadId;
            foreach (var k in result.Keys)
            {
                var saved = await _store.SaveVersionAsync(k.Key, k.Payload, k.Label, uploadId, user, ct);
                resp.DataVersion = saved.DataVersion;
                resp.Saved.Add(new IngestSavedKey
                {
                    Key = k.Key, Label = k.Label, Rows = k.Rows, DataVersion = saved.DataVersion, Entry = saved.Entry,
                    UsersSync = saved.UsersSync, UsersError = saved.UsersError
                });
            }
            resp.Report.Toasts.Add("Saved to server — Upload ID " + uploadId);
            _cache?.Invalidate();
            return resp;
        }

        private static void RequireSlotKey(string key)
        {
            if (!LegacyImport.SlotKeys.Contains(key)) throw new AppValidationException("Bad dataset key");
        }

        public async Task<IngestResponse> ImportFilesAsync(string key, IReadOnlyList<IngestUpload> files, AppUserContext user, bool append = true, CancellationToken ct = default)
        {
            RequireSlotKey(key);
            if (files.Count == 0) throw new AppValidationException("No file uploaded");
            IReadOnlyList<SheetRow>? existing = null;
            if (append && LegacyImport.IsMultiKey(key) && _cache != null)
                existing = (await _cache.GetCurrentAsync(ct)).Get(key);
            return await CommitAsync(ParseFiles(key, files, existing), user, ct);
        }

        public async Task<IngestResponse> ImportZipAsync(IngestUpload zip, AppUserContext user, CancellationToken ct = default)
        {
            Func<string, bool>? has = null;
            if (_cache != null)
            {
                var data = await _cache.GetCurrentAsync(ct);
                has = k => data.Has(k);
            }
            return await CommitAsync(ParseZip(zip, has), user, ct);
        }

        public Task<IngestResponse> ImportRawAsync(IReadOnlyList<IngestUpload> files, AppUserContext user, CancellationToken ct = default)
        {
            if (files.Count == 0) throw new AppValidationException("No file uploaded");
            return CommitAsync(ParseRaw(files), user, ct);
        }

        public Task<IngestResponse> ImportGrcAsync(IngestUpload file, AppUserContext user, CancellationToken ct = default) =>
            CommitAsync(ParseGrc(file), user, ct);
    }
}
