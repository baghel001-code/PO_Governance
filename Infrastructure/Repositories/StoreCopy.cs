using System.Text.Json;
using System.Text.Json.Nodes;
using Application.Interfaces.V1;
using Domain.Entities.Store;

namespace Infrastructure.Repositories
{
    /// <summary>
    /// Port of storage.py <c>copy_store</c> and <c>verify_copy</c>: copies every document, log, request day,
    /// audit line and blob from one store to another (PostgreSQL → SQL Server for the migration, or back
    /// for a rollback) and re-reads both sides to list the differences.
    /// <para>
    /// One addition: when the target is a <see cref="SqlServerStore"/>, blobs keep their original
    /// created_at (storage.py wrote now(); history retention uses that time).
    /// </para>
    /// </summary>
    public static class StoreCopy
    {
        private static readonly string[] FlatLogs = { LogStreams.Uploads, LogStreams.Batches, LogStreams.Runs, LogStreams.Feedback };

        /// <summary>copy_store: returns the counts ("doc:users" = rows of the map, "log:requests", "request_days", "audit", "blob:ds", …).</summary>
        public static async Task<Dictionary<string, long>> CopyAsync(IStore src, IStore dst, Action<string>? say = null, CancellationToken ct = default)
        {
            say ??= _ => { };
            var counts = new Dictionary<string, long>();
            foreach (var name in DocNames.All)
            {
                var v = await src.GetDocAsync(name, ct);
                if (v == null) continue;
                await dst.PutDocAsync(name, v, ct);
                counts["doc:" + name] = v is JsonObject o ? o.Count : 1;
            }
            say($"  copied documents: {string.Join(", ", counts.Select(kv => kv.Key[4..] + " (" + kv.Value + ")"))}");

            foreach (var stream in FlatLogs)
            {
                var recs = await src.ReadLogAsync(stream, null, ct);
                await dst.RewriteLogAsync(stream, recs, null, ct);
                counts["log:" + stream] = recs.Count;
            }
            say($"  copied logs: {string.Join(", ", FlatLogs.Select(s => s + " " + counts["log:" + s]))}");

            long n = 0;
            var days = await src.LogDaysAsync(LogStreams.Requests, ct);
            foreach (var d in days)
            {
                var recs = await src.ReadLogAsync(LogStreams.Requests, d, ct);
                await dst.RewriteLogAsync(LogStreams.Requests, recs, d, ct);
                n += recs.Count;
            }
            counts["log:requests"] = n;
            counts["request_days"] = days.Count;
            say($"  copied {n} request events of {days.Count} days");

            var lines = await src.AuditReadAsync(ct);
            switch (dst)
            {
                case SqlServerStore s: await s.AuditAppendManyAsync(lines, ct); break;
                case PgStore p: await p.AuditAppendManyAsync(lines, ct); break;
                default:
                    foreach (var l in lines) await dst.AuditAppendAsync(l.At, l.Who ?? "", l.Action ?? "", l.Detail ?? "", ct);
                    break;
            }
            counts["audit"] = lines.Count;
            say($"  copied {lines.Count} audit lines");

            foreach (var kind in new[] { BlobKinds.Dataset, BlobKinds.Run })
            {
                var blobs = await src.ListBlobsAsync(kind, ct);
                long bytes = 0;
                var i = 0;
                foreach (var b in blobs)
                {
                    var data = await src.GetBlobAnyAsync(kind, b.Key, b.Version, b.Deleted, ct);
                    i++;
                    if (data == null) continue;
                    bytes += data.Length;
                    if (dst is SqlServerStore s)
                        await s.PutBlobAsync(kind, b.Key, b.Version, data, b.Deleted, FromEpoch(b.Mtime), ct);
                    else
                        await dst.PutBlobAsync(kind, b.Key, b.Version, data, b.Deleted, ct);
                    if (i % 50 == 0) say($"    {i}/{blobs.Count} {(kind == BlobKinds.Dataset ? "file versions" : "saved runs")}");
                }
                counts["blob:" + kind] = blobs.Count;
                counts["bytes:" + kind] = bytes;
                say($"  copied {blobs.Count} {(kind == BlobKinds.Dataset ? "file versions" : "saved runs")} ({bytes:N0} bytes)");
            }
            return counts;
        }

        private static DateTimeOffset? FromEpoch(double t) =>
            t > 0 ? DateTimeOffset.UnixEpoch.AddTicks((long)Math.Round(t * 1_000_000) * 10) : null;   // whole microseconds (PostgreSQL precision)

        /// <summary>verify_copy: re-reads both sides; empty list = identical.</summary>
        public static async Task<List<string>> VerifyAsync(IStore src, IStore dst, CancellationToken ct = default)
        {
            var diffs = new List<string>();
            foreach (var name in DocNames.All)
                if (!PyEquals(await src.GetDocAsync(name, ct), await dst.GetDocAsync(name, ct)))
                    diffs.Add($"document {name} differs");
            foreach (var stream in FlatLogs)
                if (!RecsEqual(await src.ReadLogAsync(stream, null, ct), await dst.ReadLogAsync(stream, null, ct)))
                    diffs.Add($"log {stream} differs");
            var sd = await src.LogDaysAsync(LogStreams.Requests, ct);
            if (!sd.SequenceEqual(await dst.LogDaysAsync(LogStreams.Requests, ct), StringComparer.Ordinal))
                diffs.Add("request days differ");
            foreach (var d in sd)
                if (!RecsEqual(await src.ReadLogAsync(LogStreams.Requests, d, ct), await dst.ReadLogAsync(LogStreams.Requests, d, ct)))
                    diffs.Add($"requests of {d} differ");
            if (!(await src.AuditReadAsync(ct)).SequenceEqual(await dst.AuditReadAsync(ct)))
                diffs.Add("audit log differs");
            foreach (var kind in new[] { BlobKinds.Dataset, BlobKinds.Run })
            {
                var a = (await src.ListBlobsAsync(kind, ct)).Select(b => (b.Key, b.Version, b.Deleted)).ToHashSet();
                var b = (await dst.ListBlobsAsync(kind, ct)).Select(x => (x.Key, x.Version, x.Deleted)).ToHashSet();
                if (!a.SetEquals(b))
                {
                    var missing = a.Except(b).OrderBy(x => x.Key, StringComparer.Ordinal).ThenBy(x => x.Version, StringComparer.Ordinal).Take(5);
                    diffs.Add($"{kind} list differs: missing [{string.Join(", ", missing)}]");
                    continue;
                }
                foreach (var (k, v, x) in a)
                {
                    var da = await src.GetBlobAnyAsync(kind, k, v, x, ct);
                    var db = await dst.GetBlobAnyAsync(kind, k, v, x, ct);
                    if (!(da ?? Array.Empty<byte>()).AsSpan().SequenceEqual(db ?? Array.Empty<byte>()) || (da == null) != (db == null))
                        diffs.Add($"{kind} {k}/{v} content differs");
                }
            }
            return diffs;
        }

        private static bool RecsEqual(List<JsonObject> a, List<JsonObject> b) =>
            a.Count == b.Count && a.Zip(b).All(p => PyEquals(p.First, p.Second));

        /// <summary>Python == on json.loads values: dicts ignore key order, lists are ordered, 1 == 1.0.</summary>
        public static bool PyEquals(JsonNode? a, JsonNode? b)
        {
            switch (a)
            {
                case null: return b == null;
                case JsonObject oa:
                    if (b is not JsonObject ob || oa.Count != ob.Count) return false;
                    foreach (var kv in oa)
                        if (!ob.TryGetPropertyValue(kv.Key, out var bv) || !PyEquals(kv.Value, bv)) return false;
                    return true;
                case JsonArray aa:
                    if (b is not JsonArray ab || aa.Count != ab.Count) return false;
                    for (var i = 0; i < aa.Count; i++)
                        if (!PyEquals(aa[i], ab[i])) return false;
                    return true;
                case JsonValue va:
                    if (b is not JsonValue vb) return false;
                    var ka = va.GetValueKind();
                    var kb = vb.GetValueKind();
                    if (ka == JsonValueKind.Number && kb == JsonValueKind.Number) return PyJson.AsDouble(va).Equals(PyJson.AsDouble(vb));
                    if (ka != kb) return false;
                    return ka switch
                    {
                        JsonValueKind.String => string.Equals(va.GetValue<string>(), vb.GetValue<string>(), StringComparison.Ordinal),
                        _ => true   // true / false / null of the same kind
                    };
                default: return false;
            }
        }
    }
}
