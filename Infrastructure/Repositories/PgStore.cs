using System.Net.Sockets;
using System.Text.Json.Nodes;
using Application.Interfaces.V1;
using Dapper;
using Domain.Entities.Store;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Infrastructure.Repositories
{
    /// <summary>
    /// PostgreSQL store — port of storage.py <c>PgStore</c> (schema v2, same tables, columns and SQL), so
    /// the v1.1.4 database on .37 is used unchanged. Documents "users" / "sessions" are one row per
    /// key (app_users / app_sessions) and only changed rows are written; every other document is a row
    /// of app_settings. Each document write bumps doc_stamps.
    /// </summary>
    public sealed class PgStore : IStore, IDisposable
    {
        /// <summary>storage.py SCHEMA (v2).</summary>
        public static readonly string[] Schema =
        {
            @"CREATE TABLE IF NOT EXISTS app_settings (
         name text PRIMARY KEY, value json NOT NULL, updated_at timestamptz NOT NULL DEFAULT now())",
            @"CREATE TABLE IF NOT EXISTS app_users (
         email text PRIMARY KEY, name text, roles text, active boolean, data json NOT NULL,
         updated_at timestamptz NOT NULL DEFAULT now())",
            @"CREATE TABLE IF NOT EXISTS app_sessions (
         token text PRIMARY KEY, email text, data json NOT NULL, updated_at timestamptz NOT NULL DEFAULT now())",
            @"CREATE TABLE IF NOT EXISTS doc_stamps (name text PRIMARY KEY, stamp bigint NOT NULL)",
            @"CREATE TABLE IF NOT EXISTS upload_log (
         id bigserial PRIMARY KEY, rec json NOT NULL, created_at timestamptz NOT NULL DEFAULT now())",
            @"CREATE TABLE IF NOT EXISTS upload_batches (
         id bigserial PRIMARY KEY, upload_id text, rec json NOT NULL, created_at timestamptz NOT NULL DEFAULT now())",
            @"CREATE TABLE IF NOT EXISTS run_index (
         id bigserial PRIMARY KEY, run_id text, rec json NOT NULL, created_at timestamptz NOT NULL DEFAULT now())",
            @"CREATE TABLE IF NOT EXISTS request_events (
         id bigserial PRIMARY KEY, req_date text NOT NULL, seq integer, event_type text, req_id text,
         rec json NOT NULL, created_at timestamptz NOT NULL DEFAULT now())",
            @"CREATE INDEX IF NOT EXISTS request_events_day ON request_events (req_date, id)",
            @"CREATE TABLE IF NOT EXISTS audit_log (
         id bigserial PRIMARY KEY, at text NOT NULL, who text, action text, detail text)",
            @"CREATE TABLE IF NOT EXISTS dataset_versions (
         dkey text NOT NULL, version text NOT NULL, data bytea NOT NULL, bytes integer,
         created_at timestamptz NOT NULL DEFAULT now(), PRIMARY KEY (dkey, version))",
            @"CREATE TABLE IF NOT EXISTS run_files (
         run_id text PRIMARY KEY, data bytea NOT NULL, bytes integer, deleted boolean NOT NULL DEFAULT false,
         created_at timestamptz NOT NULL DEFAULT now())",
            @"CREATE TABLE IF NOT EXISTS feedback_log (
         id bigserial PRIMARY KEY, fb_id text, rec json NOT NULL, created_at timestamptz NOT NULL DEFAULT now())",
            @"CREATE TABLE IF NOT EXISTS schema_info (key text PRIMARY KEY, value text)",
            @"INSERT INTO schema_info VALUES ('version', '2') ON CONFLICT (key) DO NOTHING",
            @"UPDATE schema_info SET value = '2' WHERE key = 'version' AND value < '2'",
        };

        private static readonly Dictionary<string, string> LogTable = new()
        {
            [LogStreams.Uploads] = "upload_log", [LogStreams.Batches] = "upload_batches", [LogStreams.Runs] = "run_index",
            [LogStreams.Requests] = "request_events", [LogStreams.Feedback] = "feedback_log"
        };

        private static readonly Dictionary<string, (string Table, string KeyCol)> MapTable = new()
        {
            [DocNames.Users] = ("app_users", "email"), [DocNames.Sessions] = ("app_sessions", "token")
        };

        /// <summary>server.py LOCK — one per process (all PgStore instances share it).</summary>
        private static readonly SemaphoreSlim ServerLock = new(1, 1);

        private readonly NpgsqlDataSource _ds;
        private readonly NpgsqlConnectionStringBuilder _csb;
        private readonly ILogger<PgStore>? _log;
        private readonly SemaphoreSlim _mapGate = new(1, 1);
        private readonly Dictionary<string, Dictionary<string, string>> _mapCache = new();

        public PgStore(string connectionString, ILogger<PgStore>? log = null)
        {
            _csb = new NpgsqlConnectionStringBuilder(connectionString);
            if (string.IsNullOrEmpty(_csb.ApplicationName)) _csb.ApplicationName = "vmm-po-governance";
            if (_csb.CommandTimeout < 60) _csb.CommandTimeout = 60;
            _ds = NpgsqlDataSource.Create(_csb.ConnectionString);
            _log = log;
        }

        public string Kind => "postgres";

        public string Describe() => $"PostgreSQL {_csb.Username}@{_csb.Host}:{_csb.Port}/{_csb.Database}";

        public void Dispose() => _ds.Dispose();

        // ── connections ─────────────────────────────────────────────

        private static bool IsConnectionError(Exception e) =>
            e is NpgsqlException and not PostgresException || e is IOException || e is SocketException || e is TimeoutException
            || (e is PostgresException pe && pe.SqlState is "57P01" or "57P02" or "57P03");

        /// <summary>Runs work on a pooled connection; on a lost connection, retries once on a fresh one.</summary>
        private async Task<T> WithConn<T>(Func<NpgsqlConnection, Task<T>> work, CancellationToken ct)
        {
            for (var attempt = 1; ; attempt++)
            {
                try
                {
                    await using var c = await _ds.OpenConnectionAsync(ct);
                    return await work(c);
                }
                catch (Exception e) when (attempt == 1 && IsConnectionError(e) && !ct.IsCancellationRequested)
                {
                    using (var dead = _ds.CreateConnection()) NpgsqlConnection.ClearPool(dead);   // the others are most likely dead too (database restarted)
                    _log?.LogWarning("Database connection lost ({Error}) - reconnecting", e.Message);
                }
            }
        }

        private Task<int> Exec(string sql, object? p, CancellationToken ct) =>
            WithConn(c => c.ExecuteAsync(new CommandDefinition(sql, p, cancellationToken: ct)), ct);

        private Task<List<T>> Query<T>(string sql, object? p, CancellationToken ct) =>
            WithConn(async c => (await c.QueryAsync<T>(new CommandDefinition(sql, p, cancellationToken: ct))).AsList(), ct);

        /// <summary>[(sql, params)] in one transaction.</summary>
        private Task Tx(IReadOnlyList<(string Sql, object? P)> statements, CancellationToken ct) =>
            WithConn(async c =>
            {
                await using var tx = await c.BeginTransactionAsync(ct);
                foreach (var (sql, p) in statements)
                    await c.ExecuteAsync(new CommandDefinition(sql, p, tx, cancellationToken: ct));
                await tx.CommitAsync(ct);
                return 0;
            }, ct);

        public async Task InitializeAsync(int waitSeconds = 0, CancellationToken ct = default)
        {
            var deadline = DateTime.UtcNow.AddSeconds(waitSeconds);
            while (true)
            {
                try
                {
                    await using var c = await _ds.OpenConnectionAsync(ct);
                    foreach (var stmt in Schema) await c.ExecuteAsync(new CommandDefinition(stmt, cancellationToken: ct));
                    return;
                }
                catch (Exception e) when (IsConnectionError(e) && DateTime.UtcNow < deadline && !ct.IsCancellationRequested)
                {
                    _log?.LogWarning("Database not reachable yet ({Error}) - retrying in 5 s", e.Message);
                    await Task.Delay(5000, ct);
                }
            }
        }

        public async Task<IDisposable> LockAsync(CancellationToken ct = default)
        {
            await ServerLock.WaitAsync(ct);
            return new Releaser(ServerLock);
        }

        private sealed class Releaser : IDisposable
        {
            private SemaphoreSlim? _s;
            public Releaser(SemaphoreSlim s) => _s = s;
            public void Dispose() => Interlocked.Exchange(ref _s, null)?.Release();
        }

        private static (string, object?) Bump(string name) =>
            ("INSERT INTO doc_stamps (name, stamp) VALUES (@n, 1) ON CONFLICT (name) DO UPDATE SET stamp = doc_stamps.stamp + 1", new { n = name });

        private static string Canon(string jsonText) => PyJson.Dumps(PyJson.Parse(jsonText));

        // ── documents ───────────────────────────────────────────────

        public async Task<JsonNode?> GetDocAsync(string name, CancellationToken ct = default)
        {
            if (MapTable.TryGetValue(name, out var mt))
            {
                var rows = await Query<(string K, string V)>($"SELECT {mt.KeyCol}, data::text FROM {mt.Table} ORDER BY {mt.KeyCol}", null, ct);
                if (rows.Count == 0 && (await Query<int>("SELECT 1 FROM doc_stamps WHERE name = @n", new { n = name }, ct)).Count == 0)
                    return null;
                var o = new JsonObject();
                var cache = new Dictionary<string, string>();
                foreach (var (k, v) in rows)
                {
                    var node = PyJson.Parse(v);
                    o[k] = node;
                    cache[k] = PyJson.Dumps(node);
                }
                await _mapGate.WaitAsync(ct);
                try { _mapCache[name] = cache; } finally { _mapGate.Release(); }
                return o;
            }
            var r = await Query<string>("SELECT value::text FROM app_settings WHERE name = @n", new { n = name }, ct);
            return r.Count > 0 ? PyJson.Parse(r[0]) : null;
        }

        public async Task PutDocAsync(string name, JsonNode obj, CancellationToken ct = default)
        {
            if (MapTable.ContainsKey(name))
            {
                await PutMapAsync(name, obj as JsonObject ?? new JsonObject(), ct);
                return;
            }
            await Tx(new List<(string, object?)>
            {
                ("INSERT INTO app_settings (name, value, updated_at) VALUES (@n, CAST(@v AS json), now()) " +
                 "ON CONFLICT (name) DO UPDATE SET value = EXCLUDED.value, updated_at = now()", new { n = name, v = PyJson.Dumps(obj) }),
                Bump(name)
            }, ct);
        }

        private async Task PutMapAsync(string name, JsonObject obj, CancellationToken ct)
        {
            var (table, kcol) = MapTable[name];
            var nw = new Dictionary<string, string>();
            foreach (var kv in obj) nw[kv.Key] = PyJson.Dumps(kv.Value);
            await _mapGate.WaitAsync(ct);
            try
            {
                if (!_mapCache.TryGetValue(name, out var old))
                {
                    old = new Dictionary<string, string>();
                    foreach (var (k, v) in await Query<(string K, string V)>($"SELECT {kcol}, data::text FROM {table}", null, ct))
                        old[k] = Canon(v);
                }
                var stmts = new List<(string, object?)>();
                foreach (var k in old.Keys)
                    if (!nw.ContainsKey(k)) stmts.Add(($"DELETE FROM {table} WHERE {kcol} = @k", new { k }));
                foreach (var (k, v) in nw)
                {
                    if (old.TryGetValue(k, out var ov) && ov == v) continue;
                    var rec = obj[k] as JsonObject;
                    if (name == DocNames.Users)
                        stmts.Add(("INSERT INTO app_users (email, name, roles, active, data, updated_at) " +
                                   "VALUES (@k, @nm, @r, @a, CAST(@v AS json), now()) ON CONFLICT (email) DO UPDATE SET " +
                                   "name = EXCLUDED.name, roles = EXCLUDED.roles, active = EXCLUDED.active, " +
                                   "data = EXCLUDED.data, updated_at = now()",
                            new { k, nm = rec.GetStr("name"), r = string.Join(",", rec.GetStrList("roles")), a = rec.GetTruthy("active"), v }));
                    else
                        stmts.Add(("INSERT INTO app_sessions (token, email, data, updated_at) VALUES " +
                                   "(@k, @e, CAST(@v AS json), now()) ON CONFLICT (token) DO UPDATE SET " +
                                   "email = EXCLUDED.email, data = EXCLUDED.data, updated_at = now()",
                            new { k, e = rec.GetStr("email"), v }));
                }
                stmts.Add(Bump(name));
                await Tx(stmts, ct);
                _mapCache[name] = nw;
            }
            finally { _mapGate.Release(); }
        }

        public async Task<long> DocStampAsync(string name, CancellationToken ct = default)
        {
            var r = await Query<long>("SELECT stamp FROM doc_stamps WHERE name = @n", new { n = name }, ct);
            return r.Count > 0 ? r[0] : 0;
        }

        // ── logs ────────────────────────────────────────────────────

        private static string Table(string stream) =>
            LogTable.TryGetValue(stream, out var t) ? t : throw new ArgumentException("Unknown log stream " + stream);

        private static List<JsonObject> ParseRecs(IEnumerable<string> texts) =>
            texts.Select(t => PyJson.Parse(t) as JsonObject ?? new JsonObject()).ToList();

        public async Task<List<JsonObject>> ReadLogAsync(string stream, string? day = null, CancellationToken ct = default)
        {
            var table = Table(stream);
            var rows = stream == LogStreams.Requests
                ? await Query<string>("SELECT rec::text FROM request_events WHERE req_date = @d ORDER BY id", new { d = day }, ct)
                : await Query<string>($"SELECT rec::text FROM {table} ORDER BY id", null, ct);
            return ParseRecs(rows);
        }

        public async Task<Dictionary<string, List<JsonObject>>> ReadLogsAsync(string stream, IReadOnlyCollection<string> days, CancellationToken ct = default)
        {
            var outp = days.Distinct().ToDictionary(d => d, _ => new List<JsonObject>());
            if (days.Count == 0) return outp;
            var rows = await Query<(string D, string Rec)>(
                "SELECT req_date, rec::text FROM request_events WHERE req_date = ANY(@ds) ORDER BY id", new { ds = days.ToArray() }, ct);
            foreach (var (d, rec) in rows)
            {
                if (!outp.TryGetValue(d, out var l)) outp[d] = l = new List<JsonObject>();
                l.Add(PyJson.Parse(rec) as JsonObject ?? new JsonObject());
            }
            return outp;
        }

        private static (string, object?) InsertLog(string stream, JsonObject obj, string? day)
        {
            var v = PyJson.Dumps(obj);
            switch (stream)
            {
                case LogStreams.Requests:
                    {
                        var reqId = obj.GetTruthy("reqId") ? obj.GetStr("reqId") : obj.GetObj("request").GetStr("reqId");
                        int? seq = obj.Get("seq") is JsonValue sv && PyJson.IsNumber(sv) ? (int)PyJson.AsDouble(sv) : null;
                        return ("INSERT INTO request_events (req_date, seq, event_type, req_id, rec) VALUES (@d, @s, @t, @r, CAST(@v AS json))",
                            new { d = day, s = seq, t = obj.GetStr("type"), r = reqId, v });
                    }
                case LogStreams.Batches:
                    return ("INSERT INTO upload_batches (upload_id, rec) VALUES (@u, CAST(@v AS json))", new { u = obj.GetStr("uploadId"), v });
                case LogStreams.Runs:
                    return ("INSERT INTO run_index (run_id, rec) VALUES (@u, CAST(@v AS json))", new { u = obj.GetStr("id"), v });
                case LogStreams.Feedback:
                    return ("INSERT INTO feedback_log (fb_id, rec) VALUES (@u, CAST(@v AS json))", new { u = obj.GetStr("id"), v });
                case LogStreams.Uploads:
                    return ("INSERT INTO upload_log (rec) VALUES (CAST(@v AS json))", new { v });
                default:
                    throw new ArgumentException("Unknown log stream " + stream);
            }
        }

        public async Task AppendLogAsync(string stream, JsonObject obj, string? day = null, CancellationToken ct = default)
        {
            var (sql, p) = InsertLog(stream, obj, day);
            await Exec(sql, p, ct);
        }

        public async Task RewriteLogAsync(string stream, IEnumerable<JsonObject> objs, string? day = null, CancellationToken ct = default)
        {
            var table = Table(stream);
            var stmts = new List<(string, object?)>
            {
                stream == LogStreams.Requests
                    ? ("DELETE FROM request_events WHERE req_date = @d", new { d = day })
                    : ($"DELETE FROM {table}", null)
            };
            stmts.AddRange(objs.Select(o => InsertLog(stream, o, day)));
            await Tx(stmts, ct);
        }

        public Task<List<string>> LogDaysAsync(string stream = LogStreams.Requests, CancellationToken ct = default) =>
            Query<string>("SELECT DISTINCT req_date FROM request_events ORDER BY req_date", null, ct);

        public Task DeleteLogDayAsync(string stream, string day, CancellationToken ct = default) =>
            Exec("DELETE FROM request_events WHERE req_date = @d", new { d = day }, ct);

        // ── audit ───────────────────────────────────────────────────

        public Task AuditAppendAsync(string at, string who, string action, string detail, CancellationToken ct = default) =>
            Exec("INSERT INTO audit_log (at, who, action, detail) VALUES (@a, @w, @ac, @d)", new { a = at, w = who, ac = action, d = detail }, ct);

        public async Task<List<AuditLine>> AuditReadAsync(CancellationToken ct = default) =>
            (await Query<(string At, string? Who, string? Action, string? Detail)>("SELECT at, who, action, detail FROM audit_log ORDER BY id", null, ct))
            .Select(r => new AuditLine(r.At, r.Who, r.Action, r.Detail)).ToList();

        /// <summary>Bulk insert of audit lines in one transaction per 2000 (storage.py copy_store).</summary>
        public async Task AuditAppendManyAsync(IReadOnlyList<AuditLine> lines, CancellationToken ct = default)
        {
            for (var i = 0; i < lines.Count; i += 2000)
                await Tx(lines.Skip(i).Take(2000).Select(l => ("INSERT INTO audit_log (at, who, action, detail) VALUES (@a, @w, @ac, @d)",
                    (object?)new { a = l.At, w = l.Who, ac = l.Action, d = l.Detail })).ToList(), ct);
        }

        // ── blobs ───────────────────────────────────────────────────

        public Task PutBlobAsync(string kind, string key, string ver, byte[] data, bool deleted = false, CancellationToken ct = default) =>
            kind == BlobKinds.Dataset
                ? Exec("INSERT INTO dataset_versions (dkey, version, data, bytes) VALUES (@k, @v, @d, @b) " +
                       "ON CONFLICT (dkey, version) DO UPDATE SET data = EXCLUDED.data, bytes = EXCLUDED.bytes",
                       new { k = key, v = ver, d = data, b = data.Length }, ct)
                : Exec("INSERT INTO run_files (run_id, data, bytes, deleted) VALUES (@v, @d, @b, @x) " +
                       "ON CONFLICT (run_id) DO UPDATE SET data = EXCLUDED.data, bytes = EXCLUDED.bytes, deleted = EXCLUDED.deleted",
                       new { v = ver, d = data, b = data.Length, x = deleted }, ct);

        public async Task<byte[]?> GetBlobAsync(string kind, string key, string ver, CancellationToken ct = default)
        {
            var r = kind == BlobKinds.Dataset
                ? await Query<byte[]>("SELECT data FROM dataset_versions WHERE dkey = @k AND version = @v", new { k = key, v = ver }, ct)
                : await Query<byte[]>("SELECT data FROM run_files WHERE run_id = @v AND NOT deleted", new { v = ver }, ct);
            return r.Count > 0 ? r[0] : null;
        }

        public async Task<byte[]?> GetBlobAnyAsync(string kind, string key, string ver, bool deleted, CancellationToken ct = default)
        {
            if (kind == BlobKinds.Dataset) return await GetBlobAsync(kind, key, ver, ct);
            var r = await Query<byte[]>("SELECT data FROM run_files WHERE run_id = @v AND deleted = @x", new { v = ver, x = deleted }, ct);
            return r.Count > 0 ? r[0] : null;
        }

        public async Task<bool> BlobExistsAsync(string kind, string key, string ver, CancellationToken ct = default) =>
            await BlobSizeAsync(kind, key, ver, ct) != null;

        public async Task<long?> BlobSizeAsync(string kind, string key, string ver, CancellationToken ct = default)
        {
            var r = kind == BlobKinds.Dataset
                ? await Query<int?>("SELECT bytes FROM dataset_versions WHERE dkey = @k AND version = @v", new { k = key, v = ver }, ct)
                : await Query<int?>("SELECT bytes FROM run_files WHERE run_id = @v AND NOT deleted", new { v = ver }, ct);
            // storage.py returns the column value (None when the row is missing); a NULL bytes column counts as present
            return r.Count > 0 ? (r[0] ?? 0) : null;
        }

        public Task MarkBlobDeletedAsync(string kind, string key, string ver, CancellationToken ct = default) =>
            kind == BlobKinds.Run ? Exec("UPDATE run_files SET deleted = true WHERE run_id = @v", new { v = ver }, ct) : Task.CompletedTask;

        public Task DeleteBlobAsync(string kind, string key, string ver, CancellationToken ct = default) =>
            kind == BlobKinds.Dataset
                ? Exec("DELETE FROM dataset_versions WHERE dkey = @k AND version = @v", new { k = key, v = ver }, ct)
                : Exec("DELETE FROM run_files WHERE run_id = @v", new { v = ver }, ct);

        public async Task<List<BlobInfo>> ListBlobsAsync(string kind, CancellationToken ct = default)
        {
            if (kind == BlobKinds.Dataset)
                return (await Query<(string K, string V, double T)>(
                        "SELECT dkey, version, extract(epoch from created_at)::float8 FROM dataset_versions ORDER BY dkey, version", null, ct))
                    .Select(r => new BlobInfo(r.K, r.V, false, r.T)).ToList();
            return (await Query<(string V, bool D, double T)>(
                    "SELECT run_id, deleted, extract(epoch from created_at)::float8 FROM run_files ORDER BY run_id", null, ct))
                .Select(r => new BlobInfo("", r.V, r.D, r.T)).ToList();
        }

        // ── misc ────────────────────────────────────────────────────

        public async Task<long> StorageBytesAsync(CancellationToken ct = default) =>
            (await Query<long>("SELECT pg_database_size(current_database())", null, ct))[0];

        public async Task<bool> IsEmptyAsync(CancellationToken ct = default) =>
            (await Query<long>("SELECT (SELECT count(*) FROM app_settings) + (SELECT count(*) FROM app_users) + " +
                               "(SELECT count(*) FROM dataset_versions) + (SELECT count(*) FROM request_events) + " +
                               "(SELECT count(*) FROM feedback_log)", null, ct))[0] == 0;
    }
}
