using System.Globalization;
using System.IO.Compression;
using System.Text;
using Application.DTOs.Platform;
using Application.Interfaces.V1;
using Application.Services.V1.Platform;
using Domain.Entities.AppSettings;
using Domain.Entities.Store;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Infrastructure.Services.V1
{
    /// <summary>
    /// storage.py backup_to_zip + accounts.py list_backups: the whole store written as
    /// vmm-data-yyyyMMdd-HHmmss.zip in the v1.1.4 <b>file layout</b> (FileStore), so a backup can be
    /// unzipped into a v1.1.4 data folder or imported into PostgreSQL again with migrate_to_db.py.
    /// <code>
    /// manifest.json params.json users.json sessions.json scr_mapping.json   (documents that exist)
    /// datasets/uploads.jsonl  datasets/batches.jsonl  runs/index.jsonl  feedback.jsonl
    /// requests/YYYY-MM-DD.jsonl            audit.log (at\twho\taction\tdetail)
    /// datasets/&lt;key&gt;/&lt;version&gt;.json.gz   runs/&lt;id&gt;.json.gz   runs/&lt;id&gt;.json.gz.deleted
    /// </code>
    /// </summary>
    public sealed class BackupService : IBackupService
    {
        private static readonly SemaphoreSlim BackupLock = new(1, 1);   // one backup at a time; does not block requests
        private static readonly UTF8Encoding Utf8 = new(false);
        private readonly IStore _store;
        private readonly IAppClock _clock;
        private readonly IAuditService _audit;
        private readonly PoGovSettings _cfg;
        private readonly string _contentRoot;

        public BackupService(IStore store, IAppClock clock, IAuditService audit, IOptions<PoGovSettings> cfg, IHostEnvironment? env = null)
        {
            _store = store;
            _clock = clock;
            _audit = audit;
            _cfg = cfg.Value;
            _contentRoot = env?.ContentRootPath ?? AppContext.BaseDirectory;
        }

        public string BackupDir => Path.GetFullPath(Path.IsPathRooted(_cfg.BackupDir) ? _cfg.BackupDir
            : Path.Combine(_contentRoot, string.IsNullOrEmpty(_cfg.BackupDir) ? "backups" : _cfg.BackupDir));

        public BackupListResponse List() => new()
        {
            Items = ListBackups(BackupDir, _clock),
            Dir = BackupDir,
            Hour = _cfg.BackupHour,
            KeepDays = _cfg.BackupKeepDays
        };

        public static List<BackupInfo> ListBackups(string dir, IAppClock clock)
        {
            if (!Directory.Exists(dir)) return new List<BackupInfo>();
            return Directory.GetFiles(dir)
                .Select(Path.GetFileName)
                .Where(f => f!.StartsWith("vmm-data-", StringComparison.Ordinal) && f.EndsWith(".zip", StringComparison.Ordinal))
                .OrderByDescending(f => f, StringComparer.Ordinal)
                .Select(f =>
                {
                    var fi = new FileInfo(Path.Combine(dir, f!));
                    var at = TimeZoneInfo.ConvertTime(new DateTimeOffset(fi.LastWriteTimeUtc), TimeZoneInfo.Utc).ToOffset(clock.Now.Offset);
                    return new BackupInfo { Name = f!, Bytes = fi.Length, At = at.ToString("yyyy-MM-dd'T'HH:mm:sszzz", CultureInfo.InvariantCulture) };
                }).ToList();
        }

        public bool HasBackupFor(string yyyymmdd) => ListBackups(BackupDir, _clock).Any(b => b.Name.StartsWith("vmm-data-" + yyyymmdd, StringComparison.Ordinal));

        public async Task<string> BackupNowAsync(string who, CancellationToken ct = default)
        {
            string name;
            await BackupLock.WaitAsync(ct);
            try { name = await BackupToZipAsync(BackupDir, _cfg.BackupKeepDaysOrDefault, ct); }
            finally { BackupLock.Release(); }
            await _audit.AppendAsync(who, "backup", name, ct);
            return name;
        }

        public async Task<string> BackupToZipAsync(string backupDir, int keepDays, CancellationToken ct)
        {
            Directory.CreateDirectory(backupDir);
            var name = "vmm-data-" + _clock.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) + ".zip";
            var path = Path.Combine(backupDir, name);
            var tmp = path + ".tmp";
            try
            {
                await using (var fs = new FileStream(tmp, FileMode.Create, FileAccess.Write))
                using (var zip = new ZipArchive(fs, ZipArchiveMode.Create))
                    await WriteStoreAsync(_store, zip, ct);
                File.Move(tmp, path, true);
            }
            finally
            {
                if (File.Exists(tmp)) File.Delete(tmp);
            }
            var cutoff = DateTime.UtcNow.AddDays(-keepDays);
            foreach (var f in Directory.GetFiles(backupDir))
            {
                var n = Path.GetFileName(f);
                if (n.StartsWith("vmm-data-", StringComparison.Ordinal) && n.EndsWith(".zip", StringComparison.Ordinal) && File.GetLastWriteTimeUtc(f) < cutoff)
                    File.Delete(f);
            }
            return name;
        }

        private static async Task Text(ZipArchive zip, string entry, string content)
        {
            var e = zip.CreateEntry(entry, CompressionLevel.Optimal);
            await using var s = e.Open();
            var b = Utf8.GetBytes(content);
            await s.WriteAsync(b);
        }

        private static async Task Bytes(ZipArchive zip, string entry, byte[] data)
        {
            var e = zip.CreateEntry(entry, CompressionLevel.Optimal);
            await using var s = e.Open();
            await s.WriteAsync(data);
        }

        private static string Jsonl(IEnumerable<System.Text.Json.Nodes.JsonObject> recs)
        {
            var sb = new StringBuilder();
            foreach (var r in recs) sb.Append(PyJson.Dumps(r)).Append('\n');
            return sb.ToString();
        }

        /// <summary>storage.py copy_store(src, FileStore) written straight into a zip.</summary>
        public static async Task WriteStoreAsync(IStore store, ZipArchive zip, CancellationToken ct)
        {
            foreach (var name in DocNames.All)
            {
                var v = await store.GetDocAsync(name, ct);
                if (v != null) await Text(zip, name + ".json", PyJson.Dumps(v));
            }
            await Text(zip, "datasets/uploads.jsonl", Jsonl(await store.ReadLogAsync(LogStreams.Uploads, null, ct)));
            await Text(zip, "datasets/batches.jsonl", Jsonl(await store.ReadLogAsync(LogStreams.Batches, null, ct)));
            await Text(zip, "runs/index.jsonl", Jsonl(await store.ReadLogAsync(LogStreams.Runs, null, ct)));
            await Text(zip, "feedback.jsonl", Jsonl(await store.ReadLogAsync(LogStreams.Feedback, null, ct)));
            foreach (var d in await store.LogDaysAsync(LogStreams.Requests, ct))
                await Text(zip, $"requests/{d}.jsonl", Jsonl(await store.ReadLogAsync(LogStreams.Requests, d, ct)));
            var audit = new StringBuilder();
            foreach (var l in await store.AuditReadAsync(ct))
                audit.Append(l.At).Append('\t').Append(l.Who ?? "None").Append('\t').Append(l.Action ?? "None").Append('\t').Append(l.Detail ?? "None").Append('\n');
            await Text(zip, "audit.log", audit.ToString());
            foreach (var b in await store.ListBlobsAsync(BlobKinds.Dataset, ct))
            {
                var data = await store.GetBlobAnyAsync(BlobKinds.Dataset, b.Key, b.Version, false, ct);
                if (data != null) await Bytes(zip, $"datasets/{b.Key}/{b.Version}.json.gz", data);
            }
            foreach (var b in await store.ListBlobsAsync(BlobKinds.Run, ct))
            {
                var data = await store.GetBlobAnyAsync(BlobKinds.Run, "", b.Version, b.Deleted, ct);
                if (data != null) await Bytes(zip, $"runs/{b.Version}.json.gz" + (b.Deleted ? ".deleted" : ""), data);
            }
        }
    }

    /// <summary>server.py backup_loop: every 10 min, after backup_hour, one backup per business day.</summary>
    public sealed class BackupBackgroundService : BackgroundService
    {
        private readonly IBackupService _backups;
        private readonly IAppClock _clock;
        private readonly PoGovSettings _cfg;
        private readonly Microsoft.Extensions.Logging.ILogger<BackupBackgroundService> _log;

        public BackupBackgroundService(IBackupService backups, IAppClock clock, IOptions<PoGovSettings> cfg,
                                       Microsoft.Extensions.Logging.ILogger<BackupBackgroundService> log)
        {
            _backups = backups;
            _clock = clock;
            _cfg = cfg.Value;
            _log = log;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    var hour = _cfg.BackupHour ?? 2;
                    var now = _clock.Now;
                    if (hour >= 0 && now.Hour >= hour)
                    {
                        var today = now.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
                        if (!_backups.HasBackupFor(today)) await _backups.BackupNowAsync("system", stoppingToken);
                    }
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
                catch (Exception e)
                {
                    Microsoft.Extensions.Logging.LoggerExtensions.LogError(_log, e, "Backup failed");
                }
                try { await Task.Delay(TimeSpan.FromMinutes(10), stoppingToken); }
                catch (OperationCanceledException) { break; }
            }
        }
    }
}
