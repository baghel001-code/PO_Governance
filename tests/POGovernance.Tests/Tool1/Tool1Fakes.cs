using System.Text.Json;
using System.Text.Json.Nodes;
using Application.Interfaces.V1;
using Domain.Entities.Common;
using Domain.Entities.Engine;
using Domain.Exceptions;
using Domain.Records;

namespace POGovernance.Tests.Tool1
{
    /// <summary>In-memory request log behaving like server.py api_POST_requests (and the harness's fake fetch).</summary>
    public sealed class FakeRequestLedger : IRequestLedger
    {
        public List<JsonObject> Reqs = new();
        public int Seq;
        public string Today = "";
        public string SavedAt = "2026-10-14T10:00:00+05:30";
        public List<(int SeenSeq, bool Force)> Posts = new();
        /// <summary>Called once, right before the next save (simulates another user saving in between).</summary>
        public Action<FakeRequestLedger>? BeforeSave;

        public Task<(List<JsonObject> Requests, int Seq)> GetDayAsync(string date, CancellationToken ct = default) =>
            Task.FromResult((Reqs.Select(r => r.DeepClone().AsObject()).ToList(), Seq));

        public Task<JsonObject> SaveAsync(JsonObject request, int seenSeq, bool force, AppUserContext user, CancellationToken ct = default)
        {
            var hook = BeforeSave; BeforeSave = null; hook?.Invoke(this);
            Posts.Add((seenSeq, force));
            var r = request.DeepClone().AsObject();
            r["reqDate"] = Today;
            var key = r["colourKey"]?.GetValue<string>();
            if (!force && !string.IsNullOrEmpty(key))
            {
                var newer = Reqs.Where(x => (x["seq"]?.GetValue<double>() ?? 0) > seenSeq && x["colourKey"]?.ToString() == key).ToList();
                if (newer.Count > 0)
                    throw new ConflictException("conflict", new { conflict = true,
                        byOthers = string.Join(", ", newer.Select(x => x["requestedBy"]?.ToString() ?? "?").Distinct().OrderBy(x => x, StringComparer.Ordinal)), seq = Seq });
            }
            return Task.FromResult(Store(r, user.Name, user.Email));
        }

        public JsonObject Store(JsonObject r, string name, string email)
        {
            Seq++;
            r["seq"] = Seq;
            r["id"] = Reqs.Count + 1;
            r["reqId"] = "REQ-" + Today.Replace("-", "") + "-" + Seq.ToString("0000");
            r["requestedBy"] = name;
            r["requestedByEmail"] = email;
            r["savedAt"] = SavedAt;
            r.Remove("approval");
            if (r["decision"]?.ToString() == "Watch List")
            {
                var i = r["info"] as JsonObject;
                string N(string k) => Js.N(i?[k]?.ToString());
                var mapped = i != null && N("brand") == "non-pl" && N("mcCd") == "2203" && N("mrp") == "599";
                r["approval"] = new JsonObject
                {
                    ["status"] = "pending",
                    ["approvers"] = mapped ? new JsonArray("ch@vmm.test") : new JsonArray(),
                    ["approverNames"] = mapped ? new JsonArray("Cat Head") : new JsonArray()
                };
            }
            r = JsonNode.Parse(r.ToJsonString())!.AsObject();
            Reqs.Add(r);
            return r.DeepClone().AsObject();
        }
    }

    public sealed class FakeDatasetCache : IDatasetCache
    {
        public EngineData Data = null!;
        public Task<EngineData> GetCurrentAsync(CancellationToken ct = default) => Task.FromResult(Data);
        public Task<EngineData> GetForVersionsAsync(IReadOnlyDictionary<string, string> versions, EngineParams p, CancellationToken ct = default) => Task.FromResult(Data);
        public void Invalidate() { }
    }

    public sealed class FakeAppClock : IAppClock
    {
        public string TodayValue = "2026-10-14";
        public DateTimeOffset Now => DateTimeOffset.Parse(TodayValue + "T10:00:00+05:30");
        public string Today => TodayValue;
        public string NowIso => Now.ToString("yyyy-MM-dd'T'HH:mm:sszzz");
    }

    /// <summary>Loads Tool1/Fixtures (dataset.json from make-fixture.js, expected.json from run-parity.js).</summary>
    public static class Tool1Fixture
    {
        private static readonly string Dir = Path.Combine(AppContext.BaseDirectory, "Tool1", "Fixtures");
        private static readonly Lazy<JsonObject> DatasetJson = new(() => JsonNode.Parse(File.ReadAllText(Path.Combine(Dir, "dataset.json")))!.AsObject());
        private static readonly Lazy<JsonObject> ExpectedJson = new(() => JsonNode.Parse(File.ReadAllText(Path.Combine(Dir, "expected.json")))!.AsObject());
        private static readonly Dictionary<string, EngineData> Datas = new();

        public static JsonObject Expected => ExpectedJson.Value;
        public static JsonObject Dataset => DatasetJson.Value;

        public static JsonObject Scenario(string name) =>
            Expected["scenarios"]!.AsArray().First(s => s!["name"]!.GetValue<string>() == name)!.AsObject();

        public static IEnumerable<object[]> Cases(string kind) =>
            Expected["scenarios"]!.AsArray().SelectMany(s => s!["results"]!.AsArray()
                .Where(r => r!["kind"]!.GetValue<string>() == kind)
                .Select(r => new object[] { s!["name"]!.GetValue<string>(), r!["name"]!.GetValue<string>() }));

        public static JsonObject Case(string scenario, string name) =>
            Scenario(scenario)["results"]!.AsArray().First(r => r!["name"]!.GetValue<string>() == name)!.AsObject();

        public static EngineData Data(string scenario)
        {
            lock (Datas)
            {
                if (Datas.TryGetValue(scenario, out var d)) return d;
                var sc = Scenario(scenario);
                var drop = sc["drop"]!.AsArray().Select(x => x!.GetValue<string>()).ToHashSet();
                var files = new Dictionary<string, IReadOnlyList<SheetRow>?>();
                var fjson = Dataset["files"]!.AsObject();
                foreach (var key in new[] { "f1", "f2", "f3", "f4", "um", "bsr", "artmaster", "colmaster", "attrrel", "catheadmap" })
                {
                    if (drop.Contains(key)) { files[key] = null; continue; }
                    var rows = new List<SheetRow>();
                    using var doc = JsonDocument.Parse(fjson[key]!.ToJsonString());
                    foreach (var r in doc.RootElement.EnumerateArray()) rows.Add(SheetRow.FromJson(r));
                    files[key] = rows;
                }
                var pj = JsonSerializer.SerializeToNode(new EngineParams())!.AsObject();
                pj["seasonCurrent"] = new JsonArray("AW26");
                pj["seasonFallback"] = new JsonArray("SS26");
                foreach (var kv in sc["params"]!.AsObject()) pj[kv.Key] = kv.Value?.DeepClone();
                d = new EngineData(files, pj.Deserialize<EngineParams>()!, 1, 1);
                Datas[scenario] = d;
                return d;
            }
        }
    }
}
