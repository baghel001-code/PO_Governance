using System.Text.Json.Nodes;
using Application.DTOs.Platform;
using Application.Interfaces.V1;
using Domain.Entities.Common;
using Domain.Entities.Request;
using Domain.Entities.Response;

namespace Admin.Components.Shared.Seasons
{
    /// <summary>
    /// Planner parameters (<c>S.params</c> of v1.1.4) read / saved through PO_PARAMS_GET / PO_PARAMS_SAVE.
    /// The save replaces the whole document, so a save re-reads the current params and merges only
    /// the changed keys into them (the browser kept the whole object and saved it back).
    /// </summary>
    public static class PlannerParams
    {
        public static Task<ApiDataResponse<JsonNode>> ReadAsync(IAPICall api, CancellationToken ct = default) =>
            api.CallingAPI<ApiDataResponse<JsonNode>, EmptyReq>(AllApiNames.PO_PARAMS_GET, EmptyReq.Instance, null, ct);

        public static Task<ApiDataResponse<ParamsSaveResponse>> WriteAsync(IAPICall api, JsonObject full, CancellationToken ct = default) =>
            api.CallingAPI<ApiDataResponse<ParamsSaveResponse>, ParamsSaveReq>(AllApiNames.PO_PARAMS_SAVE, new ParamsSaveReq { Params = full }, null, ct);

        /// <summary>The current params object (empty when none / on failure).</summary>
        public static async Task<JsonObject> GetAsync(IAPICall api, CancellationToken ct = default)
        {
            var res = await ReadAsync(api, ct);
            return res.IsSuccess && res.Data is JsonObject o ? o : new JsonObject();
        }

        /// <summary>Merges <paramref name="changes"/> into the stored params and saves them. Returns null or the error message.</summary>
        public static async Task<string?> SaveAsync(IAPICall api, IReadOnlyDictionary<string, JsonNode?> changes, CancellationToken ct = default)
        {
            var cur = await ReadAsync(api, ct);
            if (!cur.IsSuccess) return cur.responseMessage;
            var obj = cur.Data as JsonObject ?? new JsonObject();
            foreach (var kv in changes) obj[kv.Key] = kv.Value?.DeepClone();
            var res = await WriteAsync(api, obj, ct);
            return res.IsSuccess ? null : res.responseMessage;
        }

        public static JsonArray ToArray(IEnumerable<string> values) => new(values.Select(v => (JsonNode?)JsonValue.Create(v)).ToArray());

        /// <summary>A string param ("grcAsOfPeriod"), null when unset.</summary>
        public static string? GetString(JsonObject p, string key) =>
            p[key] is JsonValue v && v.TryGetValue<string>(out var s) ? s : null;
    }
}
