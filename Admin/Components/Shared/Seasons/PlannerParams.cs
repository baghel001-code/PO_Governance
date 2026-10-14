using System.Text.Json.Nodes;
using Admin.Interfaces;

namespace Admin.Components.Shared.Seasons
{
    /// <summary>
    /// Planner parameters (<c>S.params</c> of v1.1.4) read / saved through <c>api/v1/params</c>.
    /// The PUT replaces the whole document, so a save re-reads the current params and merges only
    /// the changed keys into them (the browser kept the whole object and saved it back).
    /// </summary>
    public static class PlannerParams
    {
        public const string Path = "api/v1/params";

        /// <summary>The current params object (empty when none / on failure).</summary>
        public static async Task<JsonObject> GetAsync(IAPICallService api, CancellationToken ct = default)
        {
            var res = await api.GetAsync<JsonNode>(Path, ct);
            return res.IsSuccess && res.Data is JsonObject o ? o : new JsonObject();
        }

        /// <summary>Merges <paramref name="changes"/> into the stored params and saves them. Returns null or the error message.</summary>
        public static async Task<string?> SaveAsync(IAPICallService api, IReadOnlyDictionary<string, JsonNode?> changes, CancellationToken ct = default)
        {
            var cur = await api.GetAsync<JsonNode>(Path, ct);
            if (!cur.IsSuccess) return cur.ResponseMessage;
            var obj = cur.Data as JsonObject ?? new JsonObject();
            foreach (var kv in changes) obj[kv.Key] = kv.Value?.DeepClone();
            var res = await api.PutAsync<JsonNode>(Path, obj, ct);
            return res.IsSuccess ? null : res.ResponseMessage;
        }

        public static JsonArray ToArray(IEnumerable<string> values) => new(values.Select(v => (JsonNode?)JsonValue.Create(v)).ToArray());

        /// <summary>A string param ("grcAsOfPeriod"), null when unset.</summary>
        public static string? GetString(JsonObject p, string key) =>
            p[key] is JsonValue v && v.TryGetValue<string>(out var s) ? s : null;
    }
}
