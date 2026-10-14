using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Application.Interfaces.V1;
using Application.Interfaces.V1.Analytics;
using Domain.Entities.Common;
using Domain.Entities.Engine;
using Domain.Records;

namespace POGovernance.Tests.Analytics.Screens
{
    /// <summary>
    /// Loads tests/parity/analytics/fixture.json and expected.json (written by tests/parity/analytics/run.js,
    /// which runs the original index.html functions).
    /// </summary>
    public static class AnalyticsParity
    {
        private static readonly Lazy<string> Dir = new(() =>
        {
            var d = new DirectoryInfo(AppContext.BaseDirectory);
            while (d != null)
            {
                var p = Path.Combine(d.FullName, "tests", "parity", "analytics");
                if (File.Exists(Path.Combine(p, "expected.json"))) return p;
                d = d.Parent;
            }
            throw new FileNotFoundException("tests/parity/analytics/expected.json not found — run `node tests/parity/analytics/run.js`");
        });
        private static readonly Lazy<JsonObject> FixtureJson = new(() => JsonNode.Parse(File.ReadAllText(Path.Combine(Dir.Value, "fixture.json")))!.AsObject());
        private static readonly Lazy<JsonObject> ExpectedJson = new(() => JsonNode.Parse(File.ReadAllText(Path.Combine(Dir.Value, "expected.json")))!.AsObject());

        public static JsonObject Expected => ExpectedJson.Value;
        public static JsonObject Fixture => FixtureJson.Value;

        public static IEnumerable<object[]> Names(string section) =>
            Expected[section]!.AsArray().Select((x, i) => new object[] { i, x!["name"]?.GetValue<string>() ?? ("#" + i) });

        public static JsonObject Case(string section, int i) => Expected[section]!.AsArray()[i]!.AsObject();

        public static EngineData Data(IEnumerable<string>? drop = null)
        {
            var dropped = new HashSet<string>(drop ?? Enumerable.Empty<string>());
            var files = new Dictionary<string, IReadOnlyList<SheetRow>?>();
            foreach (var kv in Fixture["files"]!.AsObject())
            {
                if (dropped.Contains(kv.Key)) { files[kv.Key] = null; continue; }
                using var doc = JsonDocument.Parse(kv.Value!.ToJsonString());
                files[kv.Key] = doc.RootElement.EnumerateArray().Select(SheetRow.FromJson).ToList();
            }
            return new EngineData(files, new EngineParams(), 1, 1);
        }

        /// <summary>Fresh request objects (parsed, JsonElement-backed like the store returns them).</summary>
        public static List<JsonObject> Requests(JsonNode? idx) => Pick("requests", idx);
        public static List<JsonObject> Tool2Rows(JsonNode? idx) => Pick("tool2Rows", idx);

        private static List<JsonObject> Pick(string key, JsonNode? idx)
        {
            var all = JsonNode.Parse(Fixture[key]!.ToJsonString())!.AsArray();
            return idx!.AsArray().Select(i => all[i!.GetValue<int>()]!.DeepClone().AsObject()).Select(o => JsonNode.Parse(o.ToJsonString())!.AsObject()).ToList();
        }

        private static readonly Regex Ws = new(@"\s+");
        /// <summary>The harness's strip(): whitespace collapsed and trimmed.</summary>
        public static string Norm(string? s) => s == null ? "" : Ws.Replace(s, " ").Trim();

        public static List<string> Strings(JsonNode? arr) => arr?.AsArray().Select(x => x == null ? "" : x.GetValueKind() == JsonValueKind.String ? x.GetValue<string>() : x.ToJsonString()).ToList() ?? new();

        public static AppUserContext Staff => new("planner@x", "Planner", new[] { "planner" }, false);
        public static AppUserContext Buyer => new("buyer@x", "Buyer", new[] { "buyer" }, false);
    }

    public sealed class FixedClock : IAppClock
    {
        public DateTimeOffset Now => new(2026, 10, 14, 12, 0, 0, TimeSpan.FromHours(5.5));
        public string Today => "2026-10-14";
        public string NowIso => "2026-10-14T12:00:00+05:30";
    }

    public sealed class ListHistorySource : IRequestHistorySource
    {
        private readonly List<JsonObject> _reqs;
        public ListHistorySource(List<JsonObject> reqs) => _reqs = reqs;
        public Task<List<JsonObject>> GetVisibleRequestsAsync(AppUserContext user, string fromDate, string toDate, CancellationToken ct = default) => Task.FromResult(_reqs);
    }

    public sealed class ListRunSource : ITool2ResultSource
    {
        private readonly List<JsonObject> _rows;
        public ListRunSource(List<JsonObject> rows) => _rows = rows;
        public Task<Tool2RunRows?> GetLatestRowsAsync(AppUserContext user, CancellationToken ct = default) =>
            Task.FromResult<Tool2RunRows?>(_rows.Count == 0 ? null : new Tool2RunRows("RUN-1", _rows));
        public Task<Tool2RunRows?> GetRunRowsAsync(string runId, AppUserContext user, CancellationToken ct = default) =>
            Task.FromResult<Tool2RunRows?>(runId == "RUN-1" ? new Tool2RunRows("RUN-1", _rows) : null);
    }

    /// <summary>Gateway stub that records calls and answers from a script.</summary>
    public sealed class ScriptedGateway : IAiGateway
    {
        private readonly Func<int, AiGatewayResult> _chat;
        private readonly AiGatewayResult? _insights;
        public ScriptedGateway(Func<int, AiGatewayResult>? chat = null, AiGatewayResult? insights = null, bool enabled = true)
        {
            _chat = chat ?? (_ => new AiGatewayResult(200, new JsonObject()));
            _insights = insights;
            ChatEnabled = InsightsEnabled = enabled;
        }
        public bool ChatEnabled { get; }
        public bool InsightsEnabled { get; }
        public List<(string System, List<AiChatMessage> Messages, int MaxTokens)> ChatCalls { get; } = new();
        public List<string> Prompts { get; } = new();
        public Task<AiGatewayResult> ChatAsync(string system, IReadOnlyList<AiChatMessage> messages, int maxTokens, CancellationToken ct = default)
        {
            ChatCalls.Add((system, messages.ToList(), maxTokens));
            return Task.FromResult(_chat(ChatCalls.Count));
        }
        public Task<AiGatewayResult> InsightsAsync(string prompt, CancellationToken ct = default)
        {
            Prompts.Add(prompt);
            return Task.FromResult(_insights!);
        }
    }
}
