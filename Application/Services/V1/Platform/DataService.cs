using System.IO.Compression;
using System.Text.Json.Nodes;
using Application.DTOs.Platform;
using Application.Interfaces.V1;
using DecisionEngine.Model;
using Domain.Entities.Store;
using Domain.Exceptions;
using Domain.Records;
using Microsoft.Extensions.Logging;

namespace Application.Services.V1.Platform
{
    /// <summary>
    /// Data files and upload batches — server.py api_GET/PUT/DELETE/POST_data, api_POST_uploads, new_batch.
    /// Every save keeps all older versions; the manifest says which version is current.
    /// </summary>
    public sealed class DataService : IDataService
    {
        private readonly IStore _store;
        private readonly IAppClock _clock;
        private readonly IAccountsService _acc;
        private readonly IAuditService _audit;
        private readonly IDatasetCache _cache;
        private readonly ILogger<DataService> _log;

        public DataService(IStore store, IAppClock clock, IAccountsService acc, IAuditService audit, IDatasetCache cache, ILogger<DataService> log)
        {
            _store = store;
            _clock = clock;
            _acc = acc;
            _audit = audit;
            _cache = cache;
            _log = log;
        }

        private static void RequireStaff(AppUserContext me)
        {
            if (!PlatformCore.IsStaff(me)) throw new ForbiddenException("Only planners and admins can do this");
        }

        private static void CheckKey(string key)
        {
            if (!PlatformCore.KeyRe.IsMatch(key ?? "")) throw new AppValidationException("Bad dataset key");
        }

        public Task<Manifest> GetManifestAsync(CancellationToken ct = default) => _store.GetManifestAsync(ct);

        // ── batches ─────────────────────────────────────────────────

        public async Task<string> NewUploadBatchAsync(string source, IEnumerable<string> sourceFiles, AppUserContext user, string note = "", CancellationToken ct = default)
        {
            RequireStaff(user);
            using (await _store.LockAsync(ct))
                return await PlatformCore.NewBatchLockedAsync(_store, _clock, user.AuditName, source, sourceFiles, note, ct);
        }

        // ── read ────────────────────────────────────────────────────

        public async Task<byte[]> GetPayloadGzipAsync(string key, string? version, CancellationToken ct = default)
        {
            CheckKey(key);
            var ver = version ?? "";
            if (ver.Length == 0)
            {
                var m = await _store.GetManifestAsync(ct);
                if (!m.Datasets.TryGetValue(key, out var e) || e == null) throw new NotFoundException("No data uploaded for " + key);
                ver = e.Version ?? "";
            }
            if (!PlatformCore.VerRe.IsMatch(ver)) throw new AppValidationException("Bad version");
            return await _store.GetBlobAsync(BlobKinds.Dataset, key, ver, ct)
                   ?? throw new NotFoundException($"That version of {key} is no longer stored");
        }

        // ── save ────────────────────────────────────────────────────

        public async Task<DatasetSaveResult> SaveVersionAsync(string key, DatasetPayload payload, string label, string? uploadId, AppUserContext user, CancellationToken ct = default)
        {
            var r = await PutRawAsync(key, payload.ToGzip(), payload.Rows.Count, label, uploadId, user, ct);
            return new DatasetSaveResult(r.DataVersion, r.Entry!, r.Users, r.UsersError);
        }

        public async Task<DataSaveResponse> PutRawAsync(string key, byte[] payloadGz, int rows, string label, string? uploadId, AppUserContext me, CancellationToken ct = default)
        {
            RequireStaff(me);
            CheckKey(key);
            var uid = uploadId ?? "";
            DatasetEntry entry;
            Manifest m;
            using (await _store.LockAsync(ct))
            {
                if (!PlatformCore.UploadIdRe.IsMatch(uid))
                    uid = await PlatformCore.NewBatchLockedAsync(_store, _clock, me.AuditName, "file", null, "", ct);
                var ver = PlatformCore.NewId(_clock);
                await _store.PutBlobAsync(BlobKinds.Dataset, key, ver, payloadGz, false, ct);
                m = await _store.GetManifestAsync(ct);
                m.DataVersion += 1;
                entry = new DatasetEntry
                {
                    Version = ver, Rows = rows, Bytes = payloadGz.Length, Label = PlatformCore.Trunc(label ?? "", 200),
                    UpdatedBy = me.AuditName, UpdatedAt = _clock.NowIso, UploadId = uid
                };
                m.Datasets[key] = entry;
                await _store.SaveManifestAsync(m, ct);
                var log = PlatformCore.EntryToObject(entry);
                log["key"] = key;
                log["action"] = "upload";
                await _store.AppendLogAsync(LogStreams.Uploads, log, null, ct);
            }
            _cache.Invalidate();
            await _audit.AppendAsync(me.AuditName, "upload", $"{key} rows={rows} bytes={payloadGz.Length} {uid}", ct);
            var resp = new DataSaveResponse { DataVersion = m.DataVersion, Entry = entry };
            if (key == DatasetKeys.CatHeadMap)   // SCR products: create logins for new buyers / category heads
            {
                try
                {
                    resp.Users = await _acc.SyncFromScrAsync(DatasetPayload.FromGzip(payloadGz).Rows, me.Email, ct);
                }
                catch (Exception e) when (e is not OperationCanceledException)
                {
                    _log.LogError(e, "SCR sync failed");
                    resp.UsersError = e.Message;
                }
            }
            return resp;
        }

        /// <summary>
        /// Cheap sanity check of a raw PUT body (server.py: the first 64 bytes must look like {"rows": [);
        /// returns the gzip payload to store.
        /// </summary>
        public static byte[] PreparePutBody(byte[] raw, bool bodyIsGzip)
        {
            byte[] head;
            byte[] gz;
            if (bodyIsGzip)
            {
                gz = raw;
                try
                {
                    using var z = new GZipStream(new MemoryStream(raw), CompressionMode.Decompress);
                    var buf = new byte[64];
                    var n = 0;
                    int k;
                    while (n < 64 && (k = z.Read(buf, n, 64 - n)) > 0) n += k;
                    head = buf[..n];
                }
                catch (InvalidDataException) { throw new AppValidationException("Body is not valid gzip"); }
            }
            else
            {
                head = raw.Length > 64 ? raw[..64] : raw;
                using var ms = new MemoryStream();
                using (var z = new GZipStream(ms, CompressionLevel.Optimal, true)) z.Write(raw);
                gz = ms.ToArray();
            }
            if (!System.Text.RegularExpressions.Regex.IsMatch(System.Text.Encoding.Latin1.GetString(head), @"^\s*\{\s*""rows""\s*:\s*\["))
                throw new AppValidationException("Body must be {\"rows\": [...]}");
            return gz;
        }

        // ── clear / restore ─────────────────────────────────────────

        public async Task<DataVersionResponse> ClearAsync(string key, string? uploadId, AppUserContext me, CancellationToken ct = default)
        {
            RequireStaff(me);
            CheckKey(key);
            Manifest m;
            using (await _store.LockAsync(ct))
            {
                // Only removes the file from "current" — every uploaded version stays in history.
                m = await _store.GetManifestAsync(ct);
                if (m.Datasets.Remove(key, out var old) && old != null)
                {
                    var uid = uploadId ?? "";
                    if (!PlatformCore.UploadIdRe.IsMatch(uid))
                        uid = await PlatformCore.NewBatchLockedAsync(_store, _clock, me.AuditName, "clear", null, "", ct);
                    await _store.AppendLogAsync(LogStreams.Uploads, new JsonObject
                    {
                        ["key"] = key, ["action"] = "clear", ["version"] = old.Version, ["updatedBy"] = me.AuditName,
                        ["updatedAt"] = _clock.NowIso, ["uploadId"] = uid
                    }, null, ct);
                }
                m.DataVersion += 1;
                await _store.SaveManifestAsync(m, ct);
            }
            _cache.Invalidate();
            await _audit.AppendAsync(me.AuditName, "delete", key, ct);
            return new DataVersionResponse { DataVersion = m.DataVersion };
        }

        private static DatasetEntry EntryFrom(JsonObject e)
        {
            // {k: hist[k] for k in (version, rows, bytes, label, updatedBy, updatedAt, uploadId) if k in hist}
            var o = new JsonObject();
            foreach (var k in new[] { "version", "rows", "bytes", "label", "updatedBy", "updatedAt", "uploadId" })
                if (e.ContainsKey(k)) o[k] = e[k]?.DeepClone();
            return PyJson.FromNode<DatasetEntry>(o) ?? new DatasetEntry();
        }

        private JsonObject RestoreLog(DatasetEntry entry, string key, string by, string uid, string src)
        {
            var log = PlatformCore.EntryToObject(entry);
            log["key"] = key;
            log["action"] = "restore";
            log["updatedBy"] = by;
            log["updatedAt"] = _clock.NowIso;
            log["uploadId"] = uid;
            log["restoredFrom"] = src;
            return log;
        }

        public async Task<DataSaveResponse> RestoreVersionAsync(string key, string version, AppUserContext me, CancellationToken ct = default)
        {
            RequireStaff(me);
            if (!PlatformCore.KeyRe.IsMatch(key ?? "") || !PlatformCore.VerRe.IsMatch(version ?? ""))
                throw new AppValidationException("Bad key or version");
            DatasetEntry entry;
            Manifest m;
            using (await _store.LockAsync(ct))
            {
                var hist = (await _store.ReadLogAsync(LogStreams.Uploads, null, ct))
                    .Where(e => e.GetStr("key") == key && e.GetStr("version") == version && e.GetStr("action") == "upload").ToList();
                if (hist.Count == 0 || !await _store.BlobExistsAsync(BlobKinds.Dataset, key!, version!, ct))
                    throw new NotFoundException("That version is no longer stored");
                var last = hist[^1];
                var src = last.GetStr("uploadId") ?? "";
                var uid = await PlatformCore.NewBatchLockedAsync(_store, _clock, me.AuditName, "restore", null,
                    $"Made current again from {(src.Length > 0 ? src : "an earlier upload")}", ct);
                m = await _store.GetManifestAsync(ct);
                entry = EntryFrom(last);
                entry.RestoredBy = me.AuditName;
                entry.RestoredAt = _clock.NowIso;
                m.Datasets[key!] = entry;
                m.DataVersion += 1;
                await _store.SaveManifestAsync(m, ct);
                await _store.AppendLogAsync(LogStreams.Uploads, RestoreLog(entry, key!, me.AuditName, uid, src), null, ct);
            }
            _cache.Invalidate();
            await _audit.AppendAsync(me.AuditName, "restore", $"{key} {version}", ct);
            return new DataSaveResponse { DataVersion = m.DataVersion, Entry = entry };
        }

        public async Task<UploadRestoreResponse> RestoreUploadAsync(string uploadId, AppUserContext me, CancellationToken ct = default)
        {
            RequireStaff(me);
            var uid = uploadId ?? "";
            string newUid;
            Manifest m;
            var latest = new Dictionary<string, JsonObject>();
            using (await _store.LockAsync(ct))
            {
                var ups = (await _store.ReadLogAsync(LogStreams.Uploads, null, ct))
                    .Where(e => e.GetStr("uploadId") == uid && e.GetStr("action") == "upload").ToList();
                if (ups.Count == 0) throw new NotFoundException("No files found for " + uid);
                foreach (var e in ups) latest[e.GetStr("key") ?? ""] = e;
                var missing = new List<string>();
                foreach (var (k, e) in latest)
                    if (!await _store.BlobExistsAsync(BlobKinds.Dataset, k, e.GetStr("version") ?? "", ct)) missing.Add(k);
                if (missing.Count > 0) throw new NotFoundException("No longer stored: " + string.Join(", ", missing));
                newUid = await PlatformCore.NewBatchLockedAsync(_store, _clock, me.AuditName, "restore", null, $"Made current again from {uid}", ct);
                m = await _store.GetManifestAsync(ct);
                foreach (var (k, e) in latest)
                {
                    var entry = EntryFrom(e);
                    entry.RestoredBy = me.AuditName;
                    entry.RestoredAt = _clock.NowIso;
                    m.Datasets[k] = entry;
                    await _store.AppendLogAsync(LogStreams.Uploads, RestoreLog(entry, k, me.AuditName, newUid, uid), null, ct);
                }
                m.DataVersion += 1;
                await _store.SaveManifestAsync(m, ct);
            }
            _cache.Invalidate();
            await _audit.AppendAsync(me.AuditName, "restore-upload", $"{uid} -> {newUid} ({string.Join(", ", latest.Keys)})", ct);
            return new UploadRestoreResponse { UploadId = newUid, DataVersion = m.DataVersion, Keys = latest.Keys.ToList() };
        }
    }
}
