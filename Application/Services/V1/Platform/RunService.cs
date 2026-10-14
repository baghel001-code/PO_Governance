using System.IO.Compression;
using System.Text.Json.Nodes;
using Application.DTOs.Platform;
using Application.Interfaces.V1;
using Domain.Entities.Store;
using Domain.Exceptions;
using Domain.Records;

namespace Application.Services.V1.Platform
{
    /// <summary>Saved Tool 2 runs — server.py api_GET/POST/DELETE_runs.</summary>
    public sealed class RunService : IRunService
    {
        private readonly IStore _store;
        private readonly IAppClock _clock;
        private readonly IAuditService _audit;

        public RunService(IStore store, IAppClock clock, IAuditService audit)
        {
            _store = store;
            _clock = clock;
            _audit = audit;
        }

        private static void CheckId(string id)
        {
            if (!PlatformCore.VerRe.IsMatch(id ?? "")) throw new AppValidationException("Bad run id");
        }

        public async Task<ItemsResponse> ListAsync(string? from, string? to, CancellationToken ct = default)
        {
            PlatformCore.CheckRange(from, to);
            List<JsonObject> idx;
            HashSet<string> live;
            using (await _store.LockAsync(ct))
            {
                idx = await _store.ReadLogAsync(LogStreams.Runs, null, ct);
                live = (await _store.ListBlobsAsync(BlobKinds.Run, ct)).Where(b => !b.Deleted).Select(b => b.Version).ToHashSet();
            }
            var deleted = idx.Where(e => e.GetTruthy("deleted")).Select(e => e.GetStr("id") ?? "").ToHashSet();
            var items = idx.Where(e => !e.GetTruthy("deleted") && !deleted.Contains(e.GetStr("id") ?? "")
                                       && PlatformCore.InRange(e.GetStr("at"), from, to) && live.Contains(e.GetStr("id") ?? "")).ToList();
            foreach (var e in items)
                if (!e.ContainsKey("runNo"))
                {
                    var id = e.GetStr("id") ?? "";
                    e["runNo"] = "RUN-" + (id.Length > 8 ? id[..8] : id) + "-" + (id.Length > 9 ? id[9..Math.Min(13, id.Length)] : "");
                }
            items.Reverse();
            return new ItemsResponse { Items = items };
        }

        public async Task<byte[]> GetGzipAsync(string id, CancellationToken ct = default)
        {
            CheckId(id);
            return await _store.GetBlobAsync(BlobKinds.Run, "", id, ct) ?? throw new NotFoundException("Run not found");
        }

        public async Task<RunSaveResponse> SaveAsync(byte[] payloadGz, JsonObject meta, AppUserContext me, CancellationToken ct = default)
        {
            if (!PlatformCore.IsStaff(me)) throw new ForbiddenException("Only planners and admins can do this");
            byte[] head;
            try
            {
                using var z = new GZipStream(new MemoryStream(payloadGz), CompressionMode.Decompress);
                var buf = new byte[64];
                var n = 0;
                int k;
                while (n < 64 && (k = z.Read(buf, n, 64 - n)) > 0) n += k;
                head = buf[..n];
            }
            catch (InvalidDataException) { throw new AppValidationException("Body is not valid gzip"); }
            var i = 0;
            while (i < head.Length && (head[i] is (byte)' ' or (byte)'\t' or (byte)'\r' or (byte)'\n' or 0x0b or 0x0c)) i++;
            if (i >= head.Length || head[i] != (byte)'{') throw new AppValidationException("Bad run payload");
            var rid = PlatformCore.NewId(_clock);
            meta["id"] = rid;
            meta["at"] = _clock.NowIso;
            meta["by"] = me.AuditName;
            meta["bytes"] = payloadGz.Length;
            using (await _store.LockAsync(ct))
            {
                meta["runNo"] = await PlatformCore.NextNumberAsync(_store, _clock, LogStreams.Runs, "RUN", ct);
                await _store.PutBlobAsync(BlobKinds.Run, "", rid, payloadGz, false, ct);
                await _store.AppendLogAsync(LogStreams.Runs, meta.DeepClone().AsObject(), null, ct);
            }
            await _audit.AppendAsync(me.AuditName, "run-saved", $"{rid} lines={PyJson.Str(meta.Get("lines"))}", ct);
            return new RunSaveResponse { Id = rid, Meta = meta };
        }

        public async Task DeleteAsync(string id, AppUserContext me, CancellationToken ct = default)
        {
            if (!PlatformCore.IsStaff(me)) throw new ForbiddenException("Only planners and admins can do this");
            CheckId(id);
            using (await _store.LockAsync(ct))
            {
                await _store.AppendLogAsync(LogStreams.Runs, new JsonObject { ["id"] = id, ["deleted"] = true, ["by"] = me.AuditName, ["at"] = _clock.NowIso }, null, ct);
                await _store.MarkBlobDeletedAsync(BlobKinds.Run, "", id, ct);   // recoverable by an admin
            }
            await _audit.AppendAsync(me.AuditName, "run-deleted", id, ct);
        }
    }
}
