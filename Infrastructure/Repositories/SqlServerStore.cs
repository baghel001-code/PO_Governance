using System.Data;
using System.Diagnostics;
using System.Net.Sockets;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using Application.Interfaces.V1;
using Dapper;
using Domain.Entities.Store;
using Infrastructure.Utilitys;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;

namespace Infrastructure.Repositories
{
    /// <summary>
    /// SQL Server store — same semantics as <see cref="PgStore"/> (storage.py PgStore), implemented with the
    /// QPS pattern: <see cref="DapperHelper"/> + stored procedures <c>PRO_POGOV_*</c>
    /// (database/sqlserver/02_procedures.sql) with <c>@Error_Code</c> / <c>@Error_Message</c> outputs.
    /// Same tables and columns as schema v2, JSON text stored exactly as written (Python json.dumps format).
    /// <para>
    /// Differences from PgStore: the write lock (<see cref="LockAsync"/>) is a SQL Server application lock,
    /// so several API instances on one database are safe; and a write of a map document ("users",
    /// "sessions") whose stamp moved since this instance read it re-reads the rows before computing the
    /// changed rows (another instance wrote in between).
    /// </para>
    /// </summary>
    public sealed class SqlServerStore : IStore, IDisposable
    {
        public const string LockResource = "POGovernance.ServerLock";

        /// <summary>How long one sp_getapplock call waits before it logs and waits again.</summary>
        public int LockWaitSliceMs { get; set; } = 10_000;

        private static readonly Dictionary<string, string> MapCmd = new() { [DocNames.Users] = "USERS", [DocNames.Sessions] = "SESSIONS" };

        private static readonly HashSet<string> Streams = new()
            { LogStreams.Uploads, LogStreams.Batches, LogStreams.Runs, LogStreams.Requests, LogStreams.Feedback };

        private static readonly JsonWriterOptions WriterOptions = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

        private readonly DapperHelper _db;
        private readonly ILogger<SqlServerStore>? _log;
        private readonly bool _createSchema;

        /// <summary>One lock holder per store instance (the API registers one instance per process).</summary>
        private readonly SemaphoreSlim _localGate = new(1, 1);
        /// <summary>Dedicated (non-pooled) connection that owns the application lock while it is held.</summary>
        private SqlConnection? _lockConn;

        private readonly SemaphoreSlim _mapGate = new(1, 1);
        private readonly Dictionary<string, MapState> _mapCache = new();

        private sealed class MapState
        {
            public Dictionary<string, string> Rows = new();
            public long Stamp;
        }

        /// <param name="createSchema">InitializeAsync runs 01..03 when the schema is missing (else it only checks).</param>
        public SqlServerStore(DapperHelper db, ILogger<SqlServerStore>? log = null, bool createSchema = false)
        {
            _db = db;
            _log = log;
            _createSchema = createSchema;
        }

        public SqlServerStore(string connectionString, ILogger<SqlServerStore>? log = null, bool createSchema = false)
            : this(new DapperHelper(connectionString), log, createSchema)
        {
        }

        public string Kind => "sqlserver";

        public string Describe() => "SQL Server " + _db.Describe();

        public DapperHelper Db => _db;

        public void Dispose()
        {
            DropLockConnection();
        }

        // ── calls ───────────────────────────────────────────────────

        private sealed class KvRow { public string K { get; set; } = ""; public string? V { get; set; } }
        private sealed class LogRow { public string? D { get; set; } public string Rec { get; set; } = ""; }
        private sealed class DayRow { public string D { get; set; } = ""; }
        private sealed class AuditRow { public string At { get; set; } = ""; public string? Who { get; set; } public string? Action { get; set; } public string? Detail { get; set; } }
        private sealed class BlobRow { public byte[]? Data { get; set; } }
        private sealed class SizeRow { public int? Bytes { get; set; } }
        private sealed class ListRow { public string K { get; set; } = ""; public string V { get; set; } = ""; public bool D { get; set; } public double T { get; set; } }

        /// <summary>A store procedure returned a non-zero @Error_Code.</summary>
        public sealed class StoreProcedureException : Exception
        {
            public int ErrorCode { get; }
            public string Procedure { get; }
            public StoreProcedureException(string procedure, int code, string message)
                : base($"{procedure} failed ({code}): {message}")
            {
                Procedure = procedure;
                ErrorCode = code;
            }
        }

        private static DynamicParameters P() => DapperHelper.AddErrorOutputs(new DynamicParameters());

        private static void Check(string sp, DynamicParameters p)
        {
            var code = DapperHelper.ErrorCode(p);
            if (code != 0) throw new StoreProcedureException(sp, code, DapperHelper.ErrorMessage(p));
        }

        /// <summary>Lost connection / server restarting / failover — worth one retry on a fresh connection (PgStore).</summary>
        public static bool IsConnectionError(Exception e)
        {
            for (var x = e; x != null; x = x.InnerException)
            {
                if (x is SqlException se)
                {
                    foreach (SqlError err in se.Errors)
                        if (err.Number is -2 or -1 or 2 or 53 or 64 or 121 or 233 or 4060 or 10053 or 10054 or 10060 or 10061 or 10928 or 10929
                            or 40143 or 40197 or 40501 or 40613 or 49918 or 49919 or 49920)
                            return true;
                    return se.Class >= 20;   // severity 20+ = the connection is closed by the server
                }
                if (x is SocketException || x is IOException || x is TimeoutException) return true;
            }
            return false;
        }

        /// <summary>Deadlock victim / lock timeout returned by a procedure — retried (the transaction was rolled back).</summary>
        private static bool IsTransientProcError(Exception e) => e is StoreProcedureException { ErrorCode: 1205 or 1222 };

        /// <summary>Runs one procedure call; one retry on a lost connection (pool cleared), up to 3 on a deadlock.</summary>
        private async Task<T> Run<T>(Func<Task<T>> work, CancellationToken ct)
        {
            for (var attempt = 1; ; attempt++)
            {
                try
                {
                    return await work();
                }
                catch (Exception e) when (attempt == 1 && IsConnectionError(e) && !ct.IsCancellationRequested)
                {
                    _db.ClearPool();   // the others are most likely dead too (server restarted)
                    _log?.LogWarning("Database connection lost ({Error}) - reconnecting", e.Message);
                }
                catch (Exception e) when (attempt <= 3 && IsTransientProcError(e) && !ct.IsCancellationRequested)
                {
                    _log?.LogWarning("Database call retried ({Error})", e.Message);
                    await Task.Delay(50 * attempt, ct);
                }
            }
        }

        private Task<List<T>> List<T>(string sp, Action<DynamicParameters> fill, CancellationToken ct, int? timeout = null) =>
            Run(async () =>
            {
                var p = P();
                fill(p);
                var rows = (await _db.ExecuteStoredProcedureListAsync<T>(sp, p, timeout, ct)).AsList();
                Check(sp, p);
                return rows;
            }, ct);

        private Task<DynamicParameters> Exec(string sp, Action<DynamicParameters> fill, CancellationToken ct, int? timeout = null) =>
            Run(async () =>
            {
                var p = P();
                fill(p);
                await _db.ExecuteStoredProcedureAsync(sp, p, timeout, ct);
                Check(sp, p);
                return p;
            }, ct);

        private static void Str(DynamicParameters p, string name, string? value, int size = -1) =>
            p.Add(name, value, DbType.String, size: value == null || value.Length > 4000 ? -1 : size == -1 ? 4000 : size);

        private static string Canon(string jsonText) => PyJson.Dumps(PyJson.Parse(jsonText));

        // ── schema ──────────────────────────────────────────────────

        private async Task<string?> SchemaVersionAsync(CancellationToken ct)
        {
            var p = P();
            p.Add("@Cmd", "SCHEMA_VERSION");
            var v = await _db.ExecuteStoredProcedureSingleAsync<string>("PRO_POGOV_STORE_INFO", p, commandTimeout: null, ct: ct);
            Check("PRO_POGOV_STORE_INFO", p);
            return v;
        }

        private static bool IsSchemaMissing(Exception e) =>
            e is SqlException se && se.Errors.Cast<SqlError>().Any(x => x.Number is 2812 or 208);   // procedure / table not found

        public async Task InitializeAsync(int waitSeconds = 0, CancellationToken ct = default)
        {
            var deadline = DateTime.UtcNow.AddSeconds(waitSeconds);
            while (true)
            {
                try
                {
                    string? v;
                    try
                    {
                        v = await SchemaVersionAsync(ct);
                    }
                    catch (Exception e) when (IsSchemaMissing(e) || e is StoreProcedureException { ErrorCode: 208 })
                    {
                        v = null;
                    }
                    if (v == null && _createSchema)
                    {
                        _log?.LogInformation("Creating the SQL Server schema (database/sqlserver 01..03) in {Db}", _db.Describe());
                        await SqlServerSchema.ApplyAsync(_db.ConnectionString, null, ct);
                        v = await SchemaVersionAsync(ct);
                    }
                    if (v == null)
                        throw new InvalidOperationException(
                            $"The SQL Server database {_db.Describe()} has no PO Governance schema. Run database/sqlserver/01_tables.sql, " +
                            "02_procedures.sql and 03_seed.sql in it (see database/sqlserver/README.md), or set PoGovSettings:SqlServerCreateSchema = true.");
                    if (string.CompareOrdinal(v, SqlServerSchema.ExpectedVersion) < 0)
                        throw new InvalidOperationException($"SQL Server schema version {v} is older than {SqlServerSchema.ExpectedVersion}: run database/sqlserver/01..03 again.");
                    return;
                }
                catch (Exception e) when (IsConnectionError(e) && DateTime.UtcNow < deadline && !ct.IsCancellationRequested)
                {
                    _log?.LogWarning("Database not reachable yet ({Error}) - retrying in 5 s", e.Message);
                    _db.ClearPool();
                    await Task.Delay(5000, ct);
                }
            }
        }

        // ── write lock (sp_getapplock, session owned) ───────────────

        /// <summary>
        /// server.py LOCK across processes: an exclusive application lock on <see cref="LockResource"/>, owned
        /// by the session of a dedicated non-pooled connection kept open for as long as the lock is held (and
        /// reused for the next one). In-process callers queue on a semaphore first, so one instance holds at
        /// most one SQL Server session for the lock. If the API dies, the session ends and SQL Server releases
        /// the lock. Not re-entrant.
        /// </summary>
        public async Task<IDisposable> LockAsync(CancellationToken ct = default)
        {
            await _localGate.WaitAsync(ct);
            try
            {
                var sw = Stopwatch.StartNew();
                var connFailures = 0;
                while (true)
                {
                    ct.ThrowIfCancellationRequested();
                    int result;
                    try
                    {
                        var conn = await LockConnectionAsync(ct);
                        result = await AppLockAsync(conn, "GET", LockWaitSliceMs, ct);
                    }
                    catch (Exception e) when (IsConnectionError(e) && ++connFailures <= 2 && !ct.IsCancellationRequested)
                    {
                        _log?.LogWarning("Lock connection lost ({Error}) - reconnecting", e.Message);
                        DropLockConnection();
                        continue;
                    }
                    catch (Exception e)
                    {
                        DropLockConnection();   // a cancelled GET might have been granted: ending the session releases it
                        if (ct.IsCancellationRequested && e is not OperationCanceledException) throw new OperationCanceledException("Waiting for the database write lock was cancelled.", e, ct);
                        throw;
                    }
                    if (result >= 0) return new LockHandle(this);
                    _log?.LogWarning("Waiting for the database write lock (held by another API instance) for {Seconds:0} s", sw.Elapsed.TotalSeconds);
                }
            }
            catch
            {
                _localGate.Release();
                throw;
            }
        }

        private async Task<SqlConnection> LockConnectionAsync(CancellationToken ct)
        {
            if (_lockConn is { State: ConnectionState.Open }) return _lockConn;
            DropLockConnection();
            var c = _db.GetDedicatedConnection("lock");
            try
            {
                await c.OpenAsync(ct);
            }
            catch
            {
                await c.DisposeAsync();
                throw;
            }
            _lockConn = c;
            return c;
        }

        private void DropLockConnection()
        {
            var c = Interlocked.Exchange(ref _lockConn, null);
            if (c == null) return;
            try { c.Dispose(); } catch { /* already broken */ }
        }

        private static DynamicParameters LockParams(string cmd, int timeoutMs)
        {
            var p = P();
            p.Add("@Cmd", cmd);
            p.Add("@Resource", LockResource);
            p.Add("@Timeout_Ms", timeoutMs);
            p.Add("@Result", dbType: DbType.Int32, direction: ParameterDirection.Output);
            return p;
        }

        private static async Task<int> AppLockAsync(SqlConnection conn, string cmd, int timeoutMs, CancellationToken ct)
        {
            var p = LockParams(cmd, timeoutMs);
            await conn.ExecuteAsync(new CommandDefinition("PRO_POGOV_APP_LOCK", p, commandType: CommandType.StoredProcedure,
                commandTimeout: timeoutMs / 1000 + 30, cancellationToken: ct));
            Check("PRO_POGOV_APP_LOCK", p);
            return p.Get<int?>("@Result") ?? -999;
        }

        private void ReleaseLock()
        {
            try
            {
                var conn = _lockConn;
                if (conn is { State: ConnectionState.Open })
                {
                    var p = LockParams("RELEASE", 0);
                    conn.Execute(new CommandDefinition("PRO_POGOV_APP_LOCK", p, commandType: CommandType.StoredProcedure, commandTimeout: 15));
                    Check("PRO_POGOV_APP_LOCK", p);
                }
                else DropLockConnection();
            }
            catch (Exception e)
            {
                // ending the session releases the lock in any case
                _log?.LogWarning("Releasing the database write lock failed ({Error}) - closing its connection", e.Message);
                DropLockConnection();
            }
            finally
            {
                _localGate.Release();
            }
        }

        /// <summary>True when this instance's lock session holds the lock (tests / diagnostics).</summary>
        public async Task<bool> HoldsLockAsync(CancellationToken ct = default)
        {
            var conn = _lockConn;
            if (conn is not { State: ConnectionState.Open }) return false;
            return await AppLockAsync(conn, "STATE", 0, ct) == 1;
        }

        private sealed class LockHandle : IDisposable
        {
            private SqlServerStore? _s;
            public LockHandle(SqlServerStore s) => _s = s;
            public void Dispose() => Interlocked.Exchange(ref _s, null)?.ReleaseLock();
        }

        // ── documents ───────────────────────────────────────────────

        private async Task<(List<KvRow> Rows, long Stamp)> DocGetAsync(string cmd, string name, CancellationToken ct)
        {
            const string sp = "PRO_POGOV_DOC_GET";
            return await Run(async () =>
            {
                var p = P();
                p.Add("@Cmd", cmd);
                Str(p, "@Name", name, 450);
                p.Add("@Stamp", dbType: DbType.Int64, direction: ParameterDirection.Output);
                var rows = (await _db.ExecuteStoredProcedureListAsync<KvRow>(sp, p, commandTimeout: null, ct: ct)).AsList();
                Check(sp, p);
                return (rows, p.Get<long?>("@Stamp") ?? 0);
            }, ct);
        }

        public async Task<JsonNode?> GetDocAsync(string name, CancellationToken ct = default)
        {
            if (MapCmd.TryGetValue(name, out var cmd))
            {
                var (rows, stamp) = await DocGetAsync("GET_" + cmd, name, ct);
                if (rows.Count == 0 && stamp == 0) return null;
                var o = new JsonObject();
                var cache = new MapState { Stamp = stamp };
                foreach (var r in rows)
                {
                    var node = PyJson.Parse(r.V);
                    o[r.K] = node;
                    cache.Rows[r.K] = PyJson.Dumps(node);
                }
                await _mapGate.WaitAsync(ct);
                try { _mapCache[name] = cache; } finally { _mapGate.Release(); }
                return o;
            }
            var (doc, _) = await DocGetAsync("GET_DOC", name, ct);
            return doc.Count > 0 ? PyJson.Parse(doc[0].V) : null;
        }

        public async Task PutDocAsync(string name, JsonNode obj, CancellationToken ct = default)
        {
            if (MapCmd.ContainsKey(name))
            {
                await PutMapAsync(name, obj as JsonObject ?? new JsonObject(), ct);
                return;
            }
            var v = PyJson.Dumps(obj);
            await Exec("PRO_POGOV_DOC_PUT", p =>
            {
                Str(p, "@Name", name, 450);
                Str(p, "@Value", v);
                p.Add("@Stamp", dbType: DbType.Int64, direction: ParameterDirection.Output);
            }, ct);
        }

        private async Task<MapState> LoadMapAsync(string name, CancellationToken ct)
        {
            var (rows, stamp) = await DocGetAsync("GET_" + MapCmd[name], name, ct);
            var st = new MapState { Stamp = stamp };
            foreach (var r in rows) st.Rows[r.K] = Canon(r.V ?? "null");
            return st;
        }

        private async Task PutMapAsync(string name, JsonObject obj, CancellationToken ct)
        {
            var nw = new Dictionary<string, string>();
            foreach (var kv in obj) nw[kv.Key] = PyJson.Dumps(kv.Value);
            await _mapGate.WaitAsync(ct);
            try
            {
                if (!_mapCache.TryGetValue(name, out var old)) old = await LoadMapAsync(name, ct);
                for (var attempt = 1; ; attempt++)
                {
                    var deleted = old.Rows.Keys.Where(k => !nw.ContainsKey(k)).ToList();
                    var rowsJson = MapRowsJson(name, obj, nw, old.Rows);
                    var deletedJson = JsonArray(deleted);
                    try
                    {
                        var res = await Exec("PRO_POGOV_DOC_MAP_PUT", p =>
                        {
                            p.Add("@Cmd", MapCmd[name]);
                            Str(p, "@Rows", rowsJson);
                            Str(p, "@Deleted", deletedJson);
                            p.Add("@Expected_Stamp", old.Stamp, DbType.Int64);
                            p.Add("@Stamp", dbType: DbType.Int64, direction: ParameterDirection.Output);
                        }, ct);
                        _mapCache[name] = new MapState { Rows = nw, Stamp = res.Get<long?>("@Stamp") ?? old.Stamp + 1 };
                        return;
                    }
                    catch (StoreProcedureException e) when (e.ErrorCode == 50409 && attempt <= 3)
                    {
                        // another API instance wrote this document since we read it: diff against what is stored now
                        _log?.LogInformation("Document {Name} changed in the database - re-reading before the write", name);
                        old = await LoadMapAsync(name, ct);
                    }
                }
            }
            finally { _mapGate.Release(); }
        }

        /// <summary>JSON array of the changed rows of a map document (only rows whose JSON text differs).</summary>
        private static string MapRowsJson(string name, JsonObject obj, Dictionary<string, string> nw, Dictionary<string, string> old)
        {
            using var ms = new MemoryStream();
            using (var w = new Utf8JsonWriter(ms, WriterOptions))
            {
                w.WriteStartArray();
                foreach (var (k, v) in nw)
                {
                    if (old.TryGetValue(k, out var ov) && ov == v) continue;
                    var rec = obj[k] as JsonObject;
                    w.WriteStartObject();
                    w.WriteString("k", k);
                    if (name == DocNames.Users)
                    {
                        WriteNullable(w, "name", rec.GetStr("name"));
                        w.WriteString("roles", string.Join(",", rec.GetStrList("roles")));
                        w.WriteBoolean("active", rec.GetTruthy("active"));
                    }
                    else
                        WriteNullable(w, "email", rec.GetStr("email"));
                    w.WriteString("v", v);
                    w.WriteEndObject();
                }
                w.WriteEndArray();
            }
            return Encoding.UTF8.GetString(ms.ToArray());
        }

        private static void WriteNullable(Utf8JsonWriter w, string prop, string? value)
        {
            if (value == null) w.WriteNull(prop); else w.WriteString(prop, value);
        }

        private static string JsonArray(IEnumerable<string> values)
        {
            using var ms = new MemoryStream();
            using (var w = new Utf8JsonWriter(ms, WriterOptions))
            {
                w.WriteStartArray();
                foreach (var v in values) w.WriteStringValue(v);
                w.WriteEndArray();
            }
            return Encoding.UTF8.GetString(ms.ToArray());
        }

        public async Task<long> DocStampAsync(string name, CancellationToken ct = default) =>
            (await DocGetAsync("GET_STAMP", name, ct)).Stamp;

        // ── logs ────────────────────────────────────────────────────

        private static void CheckStream(string stream)
        {
            if (!Streams.Contains(stream)) throw new ArgumentException("Unknown log stream " + stream);
        }

        private static List<JsonObject> ParseRecs(IEnumerable<string> texts) =>
            texts.Select(t => PyJson.Parse(t) as JsonObject ?? new JsonObject()).ToList();

        public async Task<List<JsonObject>> ReadLogAsync(string stream, string? day = null, CancellationToken ct = default)
        {
            CheckStream(stream);
            var rows = await List<LogRow>("PRO_POGOV_LOG_READ", p =>
            {
                p.Add("@Cmd", "READ");
                p.Add("@Stream", stream);
                Str(p, "@Day", day, 64);
            }, ct);
            return ParseRecs(rows.Select(r => r.Rec));
        }

        public async Task<Dictionary<string, List<JsonObject>>> ReadLogsAsync(string stream, IReadOnlyCollection<string> days, CancellationToken ct = default)
        {
            var outp = days.Distinct().ToDictionary(d => d, _ => new List<JsonObject>());
            if (days.Count == 0) return outp;
            var daysJson = JsonArray(outp.Keys);
            var rows = await List<LogRow>("PRO_POGOV_LOG_READ", p =>
            {
                p.Add("@Cmd", "READ_DAYS");
                p.Add("@Stream", LogStreams.Requests);
                Str(p, "@Days", daysJson);
            }, ct);
            foreach (var r in rows)
            {
                var d = r.D ?? "";
                if (!outp.TryGetValue(d, out var l)) outp[d] = l = new List<JsonObject>();
                l.Add(PyJson.Parse(r.Rec) as JsonObject ?? new JsonObject());
            }
            return outp;
        }

        /// <summary>The indexed columns of a log record (PgStore InsertLog): ref id, seq, event type.</summary>
        private static (string? Ref, int? Seq, string? Type) LogColumns(string stream, JsonObject obj)
        {
            switch (stream)
            {
                case LogStreams.Requests:
                    {
                        var reqId = obj.GetTruthy("reqId") ? obj.GetStr("reqId") : obj.GetObj("request").GetStr("reqId");
                        int? seq = obj.Get("seq") is JsonValue sv && PyJson.IsNumber(sv) ? (int)PyJson.AsDouble(sv) : null;
                        return (reqId, seq, obj.GetStr("type"));
                    }
                case LogStreams.Batches: return (obj.GetStr("uploadId"), null, null);
                case LogStreams.Runs: return (obj.GetStr("id"), null, null);
                case LogStreams.Feedback: return (obj.GetStr("id"), null, null);
                case LogStreams.Uploads: return (null, null, null);
                default: throw new ArgumentException("Unknown log stream " + stream);
            }
        }

        public async Task AppendLogAsync(string stream, JsonObject obj, string? day = null, CancellationToken ct = default)
        {
            CheckStream(stream);
            var (rf, seq, type) = LogColumns(stream, obj);
            var v = PyJson.Dumps(obj);
            await Exec("PRO_POGOV_LOG_APPEND", p =>
            {
                p.Add("@Stream", stream);
                Str(p, "@Rec", v);
                Str(p, "@Day", day, 64);
                Str(p, "@Ref_Id", rf);
                p.Add("@Seq", seq, DbType.Int32);
                Str(p, "@Event_Type", type);
            }, ct);
        }

        public async Task RewriteLogAsync(string stream, IEnumerable<JsonObject> objs, string? day = null, CancellationToken ct = default)
        {
            CheckStream(stream);
            using var ms = new MemoryStream();
            using (var w = new Utf8JsonWriter(ms, WriterOptions))
            {
                w.WriteStartArray();
                foreach (var o in objs)
                {
                    var (rf, seq, type) = LogColumns(stream, o);
                    w.WriteStartObject();
                    w.WriteString("rec", PyJson.Dumps(o));
                    WriteNullable(w, "ref", rf);
                    if (seq.HasValue) w.WriteNumber("seq", seq.Value); else w.WriteNull("seq");
                    WriteNullable(w, "type", type);
                    w.WriteEndObject();
                }
                w.WriteEndArray();
            }
            var rows = Encoding.UTF8.GetString(ms.ToArray());
            await Exec("PRO_POGOV_LOG_REWRITE", p =>
            {
                p.Add("@Stream", stream);
                Str(p, "@Day", day, 64);
                Str(p, "@Rows", rows);
            }, ct, timeout: 600);
        }

        public async Task<List<string>> LogDaysAsync(string stream = LogStreams.Requests, CancellationToken ct = default) =>
            (await List<DayRow>("PRO_POGOV_LOG_READ", p => p.Add("@Cmd", "DAYS"), ct)).Select(r => r.D).ToList();

        public Task DeleteLogDayAsync(string stream, string day, CancellationToken ct = default) =>
            Exec("PRO_POGOV_LOG_DELETE_DAY", p =>
            {
                p.Add("@Stream", stream);
                Str(p, "@Day", day, 64);
            }, ct);

        // ── audit ───────────────────────────────────────────────────

        public Task AuditAppendAsync(string at, string who, string action, string detail, CancellationToken ct = default) =>
            Exec("PRO_POGOV_AUDIT", p =>
            {
                p.Add("@Cmd", "APPEND");
                Str(p, "@At", at);
                Str(p, "@Who", who);
                Str(p, "@Action", action);
                Str(p, "@Detail", detail);
            }, ct);

        public async Task<List<AuditLine>> AuditReadAsync(CancellationToken ct = default) =>
            (await List<AuditRow>("PRO_POGOV_AUDIT", p => p.Add("@Cmd", "READ"), ct))
            .Select(r => new AuditLine(r.At, r.Who, r.Action, r.Detail)).ToList();

        /// <summary>Bulk insert of audit lines, one transaction per 2000 (storage.py copy_store).</summary>
        public async Task AuditAppendManyAsync(IReadOnlyList<AuditLine> lines, CancellationToken ct = default)
        {
            for (var i = 0; i < lines.Count; i += 2000)
            {
                using var ms = new MemoryStream();
                using (var w = new Utf8JsonWriter(ms, WriterOptions))
                {
                    w.WriteStartArray();
                    foreach (var l in lines.Skip(i).Take(2000))
                    {
                        w.WriteStartObject();
                        w.WriteString("at", l.At);
                        WriteNullable(w, "who", l.Who);
                        WriteNullable(w, "action", l.Action);
                        WriteNullable(w, "detail", l.Detail);
                        w.WriteEndObject();
                    }
                    w.WriteEndArray();
                }
                var rows = Encoding.UTF8.GetString(ms.ToArray());
                await Exec("PRO_POGOV_AUDIT", p =>
                {
                    p.Add("@Cmd", "APPEND_MANY");
                    Str(p, "@Rows", rows);
                }, ct, timeout: 600);
            }
        }

        // ── blobs ───────────────────────────────────────────────────

        public Task PutBlobAsync(string kind, string key, string ver, byte[] data, bool deleted = false, CancellationToken ct = default) =>
            PutBlobAsync(kind, key, ver, data, deleted, null, ct);

        /// <summary>PutBlob keeping the original created_at (migration: history retention uses it).</summary>
        public Task PutBlobAsync(string kind, string key, string ver, byte[] data, bool deleted, DateTimeOffset? createdAt, CancellationToken ct = default) =>
            Exec("PRO_POGOV_BLOB_PUT", p =>
            {
                p.Add("@Kind", kind);
                Str(p, "@Key", kind == BlobKinds.Dataset ? key : "", 150);
                Str(p, "@Version", ver, 450);
                p.Add("@Data", data, DbType.Binary, size: -1);
                p.Add("@Deleted", deleted, DbType.Boolean);
                p.Add("@Created_At", createdAt, DbType.DateTimeOffset);
            }, ct, timeout: 600);

        private async Task<byte[]?> BlobGetAsync(string cmd, string kind, string key, string ver, bool deleted, CancellationToken ct)
        {
            var r = await List<BlobRow>("PRO_POGOV_BLOB_GET", p =>
            {
                p.Add("@Cmd", cmd);
                p.Add("@Kind", kind);
                Str(p, "@Key", kind == BlobKinds.Dataset ? key : "", 150);
                Str(p, "@Version", ver, 450);
                p.Add("@Deleted", deleted, DbType.Boolean);
            }, ct, timeout: 600);
            return r.Count > 0 ? r[0].Data : null;
        }

        public Task<byte[]?> GetBlobAsync(string kind, string key, string ver, CancellationToken ct = default) =>
            BlobGetAsync("GET", kind, key, ver, false, ct);

        public Task<byte[]?> GetBlobAnyAsync(string kind, string key, string ver, bool deleted, CancellationToken ct = default) =>
            BlobGetAsync("GET_ANY", kind, key, ver, deleted, ct);

        public async Task<bool> BlobExistsAsync(string kind, string key, string ver, CancellationToken ct = default) =>
            await BlobSizeAsync(kind, key, ver, ct) != null;

        public async Task<long?> BlobSizeAsync(string kind, string key, string ver, CancellationToken ct = default)
        {
            var r = await List<SizeRow>("PRO_POGOV_BLOB_GET", p =>
            {
                p.Add("@Cmd", "SIZE");
                p.Add("@Kind", kind);
                Str(p, "@Key", kind == BlobKinds.Dataset ? key : "", 150);
                Str(p, "@Version", ver, 450);
            }, ct);
            // storage.py returns the column value (None when the row is missing); a NULL bytes column counts as present
            return r.Count > 0 ? (r[0].Bytes ?? 0) : null;
        }

        public async Task MarkBlobDeletedAsync(string kind, string key, string ver, CancellationToken ct = default)
        {
            if (kind != BlobKinds.Run) return;
            await Exec("PRO_POGOV_BLOB_DELETE", p =>
            {
                p.Add("@Cmd", "MARK_DELETED");
                p.Add("@Kind", kind);
                Str(p, "@Version", ver, 450);
            }, ct);
        }

        public Task DeleteBlobAsync(string kind, string key, string ver, CancellationToken ct = default) =>
            Exec("PRO_POGOV_BLOB_DELETE", p =>
            {
                p.Add("@Cmd", "DELETE");
                p.Add("@Kind", kind);
                Str(p, "@Key", kind == BlobKinds.Dataset ? key : "", 150);
                Str(p, "@Version", ver, 450);
            }, ct);

        public async Task<List<BlobInfo>> ListBlobsAsync(string kind, CancellationToken ct = default) =>
            (await List<ListRow>("PRO_POGOV_BLOB_GET", p =>
            {
                p.Add("@Cmd", "LIST");
                p.Add("@Kind", kind);
            }, ct))
            .Select(r => new BlobInfo(kind == BlobKinds.Dataset ? r.K : "", r.V, r.D, r.T)).ToList();

        // ── misc ────────────────────────────────────────────────────

        private async Task<long> InfoAsync(string cmd, CancellationToken ct) =>
            (await List<long>("PRO_POGOV_STORE_INFO", p => p.Add("@Cmd", cmd), ct)).FirstOrDefault();

        public Task<long> StorageBytesAsync(CancellationToken ct = default) => InfoAsync("STORAGE_BYTES", ct);

        public async Task<bool> IsEmptyAsync(CancellationToken ct = default) => await InfoAsync("IS_EMPTY", ct) == 1;

        /// <summary>storage.py truncate_all — empties every data table (migration --force).</summary>
        public async Task TruncateAllAsync(CancellationToken ct = default)
        {
            await Exec("PRO_POGOV_TRUNCATE_ALL", p => p.Add("@Confirm", "TRUNCATE ALL"), ct, timeout: 600);
            await _mapGate.WaitAsync(ct);
            try { _mapCache.Clear(); } finally { _mapGate.Release(); }
        }
    }
}
