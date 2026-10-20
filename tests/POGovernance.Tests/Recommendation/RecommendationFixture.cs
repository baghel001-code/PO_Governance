using System.Text.Json;
using System.Text.Json.Nodes;
using DecisionEngine.Model;
using DecisionEngine.Recommendation;

namespace POGovernance.Tests.Recommendation
{
    /// <summary>
    /// Loads Fixtures/dataset.json (synthetic data, written by tests/parity/recommendation/make-fixture.js)
    /// and Fixtures/expected.json (the original JS results, written by run-parity.js).
    /// </summary>
    public static class RecommendationFixture
    {
        private static readonly string Dir = Path.Combine(AppContext.BaseDirectory, "Recommendation", "Fixtures");
        private static readonly Lazy<JsonObject> DatasetJson = new(() => JsonNode.Parse(File.ReadAllText(Path.Combine(Dir, "dataset.json")))!.AsObject());
        private static readonly Lazy<JsonObject> ExpectedJson = new(() => JsonNode.Parse(File.ReadAllText(Path.Combine(Dir, "expected.json")))!.AsObject());

        public static JsonObject Expected => ExpectedJson.Value;

        public static JsonArray Scenarios => Expected["scenarios"]!.AsArray();

        public static JsonObject Scenario(string name) =>
            Scenarios.First(s => s!["name"]!.GetValue<string>() == name)!.AsObject();

        public static IEnumerable<object[]> Cases() =>
            Scenarios.SelectMany(s => s!["results"]!.AsArray().Select(r => new object[] { s!["name"]!.GetValue<string>(), r!["name"]!.GetValue<string>() }));

        private static readonly Dictionary<string, RecommendationContext> Contexts = new();

        /// <summary>The C# equivalent of the JS sandbox of one scenario (S.files, S.params, S.requests, today).</summary>
        public static RecommendationContext Context(string scenario)
        {
            lock (Contexts)
            {
                if (Contexts.TryGetValue(scenario, out var c)) return c;
                var sc = Scenario(scenario);
                var drop = sc["drop"]!.AsArray().Select(x => x!.GetValue<string>()).ToHashSet();
                var files = new Dictionary<string, IReadOnlyList<SheetRow>?>();
                var fjson = DatasetJson.Value["files"]!.AsObject();
                foreach (var key in new[] { "f1", "f2", "f3", "f4", "um", "bsr", "artmaster", "colmaster", "attrrel", "catheadmap" })
                {
                    if (drop.Contains(key)) { files[key] = null; continue; }
                    var rows = new List<SheetRow>();
                    using var doc = JsonDocument.Parse(fjson[key]!.ToJsonString());
                    foreach (var r in doc.RootElement.EnumerateArray()) rows.Add(SheetRow.FromJson(r));
                    files[key] = rows;
                }
                var pj = JsonSerializer.SerializeToNode(new EngineParams())!.AsObject();
                foreach (var kv in sc["params"]!.AsObject()) pj[kv.Key] = kv.Value?.DeepClone();
                var p = pj.Deserialize<EngineParams>()!;
                var data = new EngineData(files, p, 1, 1);
                var reqs = DatasetJson.Value["requests"]!.AsArray().Select(x => x!.DeepClone().AsObject()).ToList();
                c = new RecommendationContext(data, reqs, sc["today"]!.GetValue<string>());
                Contexts[scenario] = c;
                return c;
            }
        }

        public static JsonObject Case(string scenario, string name) =>
            Scenario(scenario)["results"]!.AsArray().First(r => r!["name"]!.GetValue<string>() == name)!.AsObject();
    }

    /// <summary>Field-by-field comparison of the JS result (expected) with the C# DTO serialised to JSON.</summary>
    public static class JsonParity
    {
        private static readonly HashSet<string> DisplayOnly = new() { "tip", "formula", "text", "footer" };

        public static JsonNode? ToNode(object? o) => o == null ? null : JsonSerializer.SerializeToNode(o, o.GetType());

        public static void AssertEqual(JsonNode? expected, JsonNode? actual, string path, List<string> errors)
        {
            if (errors.Count > 20) return;
            if (expected == null)
            {
                if (actual != null && !(actual is JsonValue av0 && av0.GetValueKind() == JsonValueKind.Null))
                    errors.Add($"{path}: expected null/undefined, got {Short(actual)}");
                return;
            }
            switch (expected)
            {
                case JsonObject eo:
                    if (actual is not JsonObject ao) { errors.Add($"{path}: expected object, got {Short(actual)}"); return; }
                    foreach (var kv in eo) AssertEqual(kv.Value, ao.TryGetPropertyValue(kv.Key, out var v) ? v : null, path + "." + kv.Key, errors);
                    foreach (var kv in ao)
                        if (!eo.ContainsKey(kv.Key) && !DisplayOnly.Contains(kv.Key) && kv.Value != null)
                            errors.Add($"{path}.{kv.Key}: not in JS result but C# has {Short(kv.Value)}");
                    return;
                case JsonArray ea:
                    if (actual is not JsonArray aa) { errors.Add($"{path}: expected array, got {Short(actual)}"); return; }
                    if (ea.Count != aa.Count) { errors.Add($"{path}: length {ea.Count} vs {aa.Count}"); return; }
                    for (var i = 0; i < ea.Count; i++) AssertEqual(ea[i], aa[i], $"{path}[{i}]", errors);
                    return;
                case JsonValue ev:
                    var k = ev.GetValueKind();
                    if (actual is not JsonValue avv) { errors.Add($"{path}: expected {ev.ToJsonString()}, got {Short(actual)}"); return; }
                    var ak = avv.GetValueKind();
                    if (k == JsonValueKind.Number)
                    {
                        if (ak != JsonValueKind.Number) { errors.Add($"{path}: expected {ev.ToJsonString()}, got {avv.ToJsonString()}"); return; }
                        double e = ev.GetValue<double>(), a = avv.GetValue<double>();
                        if (Math.Abs(e - a) > 1e-9 * Math.Max(1, Math.Max(Math.Abs(e), Math.Abs(a))))
                            errors.Add($"{path}: expected {e:R}, got {a:R}");
                        return;
                    }
                    if (k == JsonValueKind.Null) { if (ak != JsonValueKind.Null) errors.Add($"{path}: expected null, got {avv.ToJsonString()}"); return; }
                    if (ev.ToJsonString() != avv.ToJsonString()) errors.Add($"{path}: expected {ev.ToJsonString()}, got {avv.ToJsonString()}");
                    return;
            }
        }

        private static string Short(JsonNode? n)
        {
            var s = n?.ToJsonString() ?? "null";
            return s.Length > 120 ? s[..120] + "…" : s;
        }

        public static void Check(JsonNode? expected, object? actual, string what)
        {
            var errors = new List<string>();
            AssertEqual(expected, actual as JsonNode ?? ToNode(actual), what, errors);
            Assert.True(errors.Count == 0, string.Join("\n", errors));
        }
    }
}
