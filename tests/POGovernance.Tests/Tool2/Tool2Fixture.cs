using System.Text.Json;
using System.Text.Json.Nodes;
using Application.DTOs.Recommendation;
using Application.DTOs.Tool2;
using Application.Services.V1.Recommendation;
using Application.Services.V1.Tool2;
using Domain.Entities.Common;
using Domain.Entities.Engine;

namespace POGovernance.Tests.Tool2
{
    /// <summary>
    /// Loads Tool2/Fixtures/dataset.json (tests/parity/tool2/make-fixture.js) and expected.json (the original JS
    /// output, tests/parity/tool2/run-parity.js) and runs the C# port per scenario (cached).
    /// </summary>
    public static class Tool2Fixture
    {
        private static readonly string Dir = Path.Combine(AppContext.BaseDirectory, "Tool2", "Fixtures");
        private static readonly Lazy<JsonObject> DatasetJson = new(() => JsonNode.Parse(File.ReadAllText(Path.Combine(Dir, "dataset.json")))!.AsObject());
        private static readonly Lazy<JsonObject> ExpectedJson = new(() => JsonNode.Parse(File.ReadAllText(Path.Combine(Dir, "expected.json")))!.AsObject());
        public static readonly RecommendationEngine Engine = new();

        public static JsonArray Scenarios => ExpectedJson.Value["scenarios"]!.AsArray();
        public static IEnumerable<object[]> ScenarioNames() => Scenarios.Select(s => new object[] { s!["name"]!.GetValue<string>() });
        public static JsonObject Scenario(string name) => Scenarios.First(s => s!["name"]!.GetValue<string>() == name)!.AsObject();
        public static JsonObject Result(string name) => Scenario(name)["result"]!.AsObject();

        private static readonly Dictionary<string, Tool2Workspace> Cache = new();

        public static RecommendationContext Context(string scenario)
        {
            var sc = Scenario(scenario);
            var drop = sc["drop"]!.AsArray().Select(x => x!.GetValue<string>()).ToHashSet();
            var files = new Dictionary<string, IReadOnlyList<SheetRow>?>();
            var fjson = DatasetJson.Value["files"]!.AsObject();
            foreach (var key in new[] { "f1", "f2", "f3", "f4", "um", "bsr", "artmaster", "colmaster", "attrrel", "catheadmap" })
            {
                if (drop.Contains(key)) { files[key] = null; continue; }
                using var doc = JsonDocument.Parse(fjson[key]!.ToJsonString());
                files[key] = doc.RootElement.EnumerateArray().Select(SheetRow.FromJson).ToList();
            }
            var pj = JsonSerializer.SerializeToNode(new EngineParams())!.AsObject();
            foreach (var kv in sc["params"]!.AsObject()) pj[kv.Key] = kv.Value?.DeepClone();
            var p = pj.Deserialize<EngineParams>()!;
            var reqs = DatasetJson.Value["requests"]!.AsArray().Select(x => x!.DeepClone().AsObject()).ToList();
            return new RecommendationContext(new EngineData(files, p, 1, 1), reqs, sc["today"]!.GetValue<string>());
        }

        /// <summary>The C# run of a scenario (runAssessment on the fixture).</summary>
        public static Tool2Workspace Workspace(string scenario)
        {
            lock (Cache)
            {
                if (Cache.TryGetValue(scenario, out var w)) return w;
                var ctx = Context(scenario);
                w = new Tool2Workspace(Tool2Engine.Run(ctx, Engine), ctx, Engine);
                Cache[scenario] = w;
                return w;
            }
        }

        /// <summary>A workspace built from the JS-saved run (packed results) — re-opening a v1.1.4 run.</summary>
        public static Tool2Workspace ReopenedJsRun(string scenario)
        {
            var ctx = Context(scenario);
            var results = Tool2RunPayload.Unpack(Result(scenario)["saved"]!["packed"]!.AsObject());
            return new Tool2Workspace(results, ctx, Engine);
        }

        public static DateTimeOffset Now(string scenario)
        {
            var d = DateTime.ParseExact(Scenario(scenario)["today"]!.GetValue<string>(), "yyyy-MM-dd", null);
            return new DateTimeOffset(d.Year, d.Month, d.Day, 12, 0, 0, TimeSpan.FromHours(5.5));
        }
    }

    /// <summary>Tolerant JSON comparison (numbers within 1e-9 relative); keys only in the actual value are ignored.</summary>
    public static class Cmp
    {
        public static void Equal(JsonNode? e, JsonNode? a, string path, List<string> errors)
        {
            if (errors.Count > 25) return;
            if (e == null || (e is JsonValue ev0 && ev0.GetValueKind() == JsonValueKind.Null))
            {
                if (a != null && !(a is JsonValue av0 && (av0.GetValueKind() == JsonValueKind.Null || IsNaN(av0))))
                    errors.Add($"{path}: expected null, got {S(a)}");
                return;
            }
            switch (e)
            {
                case JsonObject eo:
                    if (a is not JsonObject ao) { errors.Add($"{path}: expected object {S(e)}, got {S(a)}"); return; }
                    foreach (var kv in eo) Equal(kv.Value, ao.TryGetPropertyValue(kv.Key, out var v) ? v : null, path + "." + kv.Key, errors);
                    return;
                case JsonArray ea:
                    if (a is not JsonArray aa) { errors.Add($"{path}: expected array {S(e)}, got {S(a)}"); return; }
                    if (ea.Count != aa.Count) { errors.Add($"{path}: length {ea.Count} vs {aa.Count}: {S(e)} / {S(a)}"); return; }
                    for (var i = 0; i < ea.Count; i++) Equal(ea[i], aa[i], $"{path}[{i}]", errors);
                    return;
                case JsonValue ev:
                    if (a is not JsonValue av) { errors.Add($"{path}: expected {S(e)}, got {S(a)}"); return; }
                    if (ev.GetValueKind() == JsonValueKind.Number)
                    {
                        if (av.GetValueKind() != JsonValueKind.Number) { errors.Add($"{path}: expected {S(e)}, got {S(a)}"); return; }
                        double x = D(ev), y = D(av);
                        if (Math.Abs(x - y) > 1e-9 * Math.Max(1, Math.Max(Math.Abs(x), Math.Abs(y)))) errors.Add($"{path}: expected {x:R}, got {y:R}");
                        return;
                    }
                    if (ev.ToJsonString() != av.ToJsonString()) errors.Add($"{path}: expected {S(e)}, got {S(a)}");
                    return;
            }
        }

        private static double D(JsonValue v) => double.Parse(v.ToJsonString(), System.Globalization.CultureInfo.InvariantCulture);
        private static bool IsNaN(JsonValue v) => v.TryGetValue<string>(out var s) && s == "NaN";
        private static string S(JsonNode? n) { var s = n?.ToJsonString() ?? "null"; return s.Length > 160 ? s[..160] + "…" : s; }

        public static void Check(JsonNode? expected, JsonNode? actual, string what)
        {
            var errors = new List<string>();
            Equal(expected, actual, what, errors);
            Assert.True(errors.Count == 0, string.Join("\n", errors));
        }

        private static readonly JsonSerializerOptions Opts = new() { NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowNamedFloatingPointLiterals };
        public static JsonNode? Node(object? o) => o == null ? null : JsonSerializer.SerializeToNode(o, o.GetType(), Opts);

        public static List<string> Strings(JsonNode? n) => n!.AsArray().Select(x => x!.GetValue<string>()).ToList();

        public static void Tokens(JsonNode? expected, List<string> actual, string what)
        {
            var e = Strings(expected);
            var a = actual.Select(t => t.Trim()).Where(t => t.Length > 0).ToList();
            for (var i = 0; i < Math.Min(e.Count, a.Count); i++)
                Assert.True(e[i] == a[i], $"{what}: token {i}: expected «{e[i]}» got «{a[i]}»\ncontext: {string.Join(" | ", e.Skip(Math.Max(0, i - 4)).Take(8))}");
            Assert.True(e.Count == a.Count, $"{what}: {e.Count} tokens expected, got {a.Count}. Extra: {string.Join(" | ", (e.Count > a.Count ? e : a).Skip(Math.Min(e.Count, a.Count)).Take(10))}");
        }
    }
}
