using System.Text.Json.Nodes;
using Application.Interfaces.V1;
using Application.Services.V1.Platform;
using DecisionEngine.Model;
using Domain.Entities.AppSettings;
using Domain.Entities.Store;
using Domain.Records;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace POGovernance.Tests.Platform
{
    /// <summary>In-memory IStore with the PgStore semantics (records are deep-copied in and out).</summary>
    public sealed class FakeStore : IStore
    {
        private readonly SemaphoreSlim _lock = new(1, 1);
        public readonly Dictionary<string, JsonNode> Docs = new();
        public readonly Dictionary<string, long> Stamps = new();
        public readonly Dictionary<string, List<JsonObject>> Logs = new();
        public readonly SortedDictionary<string, List<JsonObject>> Requests = new(StringComparer.Ordinal);
        public readonly List<AuditLine> Audit = new();
        public readonly Dictionary<(string Kind, string Key, string Ver), (byte[] Data, bool Deleted, double T)> Blobs = new();

        public string Kind => "memory";
        public string Describe() => "memory";
        public Task InitializeAsync(int waitSeconds = 0, CancellationToken ct = default) => Task.CompletedTask;

        public async Task<IDisposable> LockAsync(CancellationToken ct = default)
        {
            await _lock.WaitAsync(ct);
            return new R(_lock);
        }

        private sealed class R : IDisposable
        {
            private SemaphoreSlim? _s;
            public R(SemaphoreSlim s) => _s = s;
            public void Dispose() => Interlocked.Exchange(ref _s, null)?.Release();
        }

        public Task<JsonNode?> GetDocAsync(string name, CancellationToken ct = default) =>
            Task.FromResult(Docs.TryGetValue(name, out var d) ? d.DeepClone() : null);

        public Task PutDocAsync(string name, JsonNode obj, CancellationToken ct = default)
        {
            Docs[name] = obj.DeepClone();
            Stamps[name] = Stamps.GetValueOrDefault(name) + 1;
            return Task.CompletedTask;
        }

        public Task<long> DocStampAsync(string name, CancellationToken ct = default) => Task.FromResult(Stamps.GetValueOrDefault(name));

        private List<JsonObject> L(string stream, string? day) =>
            stream == LogStreams.Requests
                ? (Requests.TryGetValue(day!, out var r) ? r : Requests[day!] = new())
                : (Logs.TryGetValue(stream, out var l) ? l : Logs[stream] = new());

        public Task<List<JsonObject>> ReadLogAsync(string stream, string? day = null, CancellationToken ct = default) =>
            Task.FromResult(stream == LogStreams.Requests && !Requests.ContainsKey(day!)
                ? new List<JsonObject>()
                : L(stream, day).Select(x => x.DeepClone().AsObject()).ToList());

        public async Task<Dictionary<string, List<JsonObject>>> ReadLogsAsync(string stream, IReadOnlyCollection<string> days, CancellationToken ct = default)
        {
            var d = new Dictionary<string, List<JsonObject>>();
            foreach (var day in days) d[day] = await ReadLogAsync(stream, day, ct);
            return d;
        }

        public Task AppendLogAsync(string stream, JsonObject obj, string? day = null, CancellationToken ct = default)
        {
            L(stream, day).Add(obj.DeepClone().AsObject());
            return Task.CompletedTask;
        }

        public Task RewriteLogAsync(string stream, IEnumerable<JsonObject> objs, string? day = null, CancellationToken ct = default)
        {
            var l = L(stream, day);
            l.Clear();
            l.AddRange(objs.Select(o => o.DeepClone().AsObject()));
            return Task.CompletedTask;
        }

        public Task<List<string>> LogDaysAsync(string stream = LogStreams.Requests, CancellationToken ct = default) =>
            Task.FromResult(Requests.Where(kv => kv.Value.Count > 0).Select(kv => kv.Key).ToList());

        public Task DeleteLogDayAsync(string stream, string day, CancellationToken ct = default)
        {
            Requests.Remove(day);
            return Task.CompletedTask;
        }

        public Task AuditAppendAsync(string at, string who, string action, string detail, CancellationToken ct = default)
        {
            Audit.Add(new AuditLine(at, who, action, detail));
            return Task.CompletedTask;
        }

        public Task<List<AuditLine>> AuditReadAsync(CancellationToken ct = default) => Task.FromResult(Audit.ToList());

        private static string K(string kind, string key) => kind == BlobKinds.Dataset ? key : "";

        public Task PutBlobAsync(string kind, string key, string ver, byte[] data, bool deleted = false, CancellationToken ct = default)
        {
            Blobs[(kind, K(kind, key), ver)] = (data, deleted, 1_700_000_000);
            return Task.CompletedTask;
        }

        public Task<byte[]?> GetBlobAsync(string kind, string key, string ver, CancellationToken ct = default) =>
            Task.FromResult(Blobs.TryGetValue((kind, K(kind, key), ver), out var b) && !b.Deleted ? b.Data : null);

        public Task<byte[]?> GetBlobAnyAsync(string kind, string key, string ver, bool deleted, CancellationToken ct = default) =>
            Task.FromResult(Blobs.TryGetValue((kind, K(kind, key), ver), out var b) && b.Deleted == deleted ? b.Data : null);

        public async Task<bool> BlobExistsAsync(string kind, string key, string ver, CancellationToken ct = default) =>
            await GetBlobAsync(kind, key, ver, ct) != null;

        public async Task<long?> BlobSizeAsync(string kind, string key, string ver, CancellationToken ct = default) =>
            (await GetBlobAsync(kind, key, ver, ct))?.Length;

        public Task MarkBlobDeletedAsync(string kind, string key, string ver, CancellationToken ct = default)
        {
            if (kind == BlobKinds.Run && Blobs.TryGetValue((kind, "", ver), out var b)) Blobs[(kind, "", ver)] = (b.Data, true, b.T);
            return Task.CompletedTask;
        }

        public Task DeleteBlobAsync(string kind, string key, string ver, CancellationToken ct = default)
        {
            Blobs.Remove((kind, K(kind, key), ver));
            return Task.CompletedTask;
        }

        public Task<List<BlobInfo>> ListBlobsAsync(string kind, CancellationToken ct = default) =>
            Task.FromResult(Blobs.Where(kv => kv.Key.Kind == kind).OrderBy(kv => kv.Key.Key).ThenBy(kv => kv.Key.Ver)
                .Select(kv => new BlobInfo(kv.Key.Key, kv.Key.Ver, kv.Value.Deleted, kv.Value.T)).ToList());

        public Task<long> StorageBytesAsync(CancellationToken ct = default) => Task.FromResult(Blobs.Values.Sum(b => (long)b.Data.Length));
        public Task<bool> IsEmptyAsync(CancellationToken ct = default) => Task.FromResult(Docs.Count == 0);
    }

    public sealed class FakeClock : IAppClock
    {
        public DateTimeOffset Now { get; set; } = new(2026, 10, 14, 10, 30, 0, TimeSpan.FromHours(5.5));
        public string Today => Now.ToString("yyyy-MM-dd");
        public string NowIso => Now.ToString("yyyy-MM-dd'T'HH:mm:sszzz");
    }

    /// <summary>Fast hasher for tests (the real one is tested separately).</summary>
    public sealed class PlainHasher : IPasswordHasher
    {
        public string Hash(string password) => "plain$" + password;
        public bool Verify(string password, string? stored) => stored == "plain$" + password;
    }

    public sealed class FakeCache : IDatasetCache
    {
        public int Invalidations;
        public Task<EngineData> GetCurrentAsync(CancellationToken ct = default) => throw new NotSupportedException();
        public Task<EngineData> GetForVersionsAsync(IReadOnlyDictionary<string, string> versions, EngineParams p, CancellationToken ct = default) => throw new NotSupportedException();
        public void Invalidate() => Invalidations++;
    }

    /// <summary>All platform services wired on a FakeStore.</summary>
    public sealed class Platform
    {
        public FakeStore Store { get; } = new();
        public FakeClock Clock { get; } = new();
        public PoGovSettings Cfg { get; } = new() { AdminEmail = "admin.user@x.com", LoginMode = LoginModes.Local, LoginFailureMinMs = 0 };
        public FakeCache Cache { get; } = new();
        public AuditService Audit { get; }
        public AccountsService Accounts { get; }
        public RequestService Requests { get; }
        public ApprovalService Approvals { get; }
        public DataService Data { get; }

        public Platform()
        {
            var o = Options.Create(Cfg);
            Audit = new AuditService(Store, Clock, NullLogger<AuditService>.Instance);
            Accounts = new AccountsService(Store, o, Clock, Audit, new PlainHasher(), new ScrSource(Store));
            Requests = new RequestService(Store, Clock, Accounts, Audit, o);
            Approvals = new ApprovalService(Store, Clock, Audit);
            Data = new DataService(Store, Clock, Accounts, Audit, Cache, NullLogger<DataService>.Instance);
        }

        public static AppUserContext User(string email, string name, params string[] roles) => new(email, name, roles, false);

        public static readonly AppUserContext Admin = User("admin.user@x.com", "Admin User", "admin", "planner");

        public static SheetRow Row(params (string K, object? V)[] cells) => new(cells.Select(c => new KeyValuePair<string, object?>(c.K, c.V)));

        /// <summary>Uploads an SCR products file (Brand / MC CD / MRP + Level 1 buyer + Level 3 category head).</summary>
        public async Task UploadScrAsync(params (string Brand, object Mc, object Mrp, string Buyer, string CatHead)[] rows)
        {
            var p = new DatasetPayload();
            foreach (var r in rows)
                p.Rows.Add(Row(("Brand", r.Brand), ("MC CD", r.Mc), ("MRP", r.Mrp), ("Level 1 Email", r.Buyer), ("Buyer Name", ""),
                               ("Level 3 Email", r.CatHead), ("Cat Head Name", "Cat Head")));
            await Data.SaveVersionAsync("catheadmap", p, "scr.xlsx", null, Admin);
        }
    }
}
