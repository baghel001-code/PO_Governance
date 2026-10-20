using System.Collections.Concurrent;
using System.Text.Json.Nodes;
using DecisionEngine.Common;
using DecisionEngine.Model;
using DecisionEngine.Results.Recommendation;

namespace DecisionEngine.Recommendation
{
    /// <summary>
    /// Everything one recommendation run reads: the data snapshot (<c>S.files</c> + <c>S.params</c>),
    /// today's requests of all users (<c>S.requests</c>, for the same-day Max Permissible ledger) and
    /// the server's today. v1.1.4 used the PC's clock for the STR chain and the server's date for the
    /// ledger; here both are the server's date.
    /// </summary>
    public sealed class RecommendationContext
    {
        public RecommendationContext(EngineData data, IReadOnlyList<JsonObject>? requests, string today)
        {
            Data = data;
            Requests = requests ?? Array.Empty<JsonObject>();
            Today = today;
            (Y, M, D) = MonthMath.ParseIsoDate(today);
        }

        public EngineData Data { get; }
        public EngineParams P => Data.Params;
        /// <summary>Today's requests (v1.1.4 JSON objects).</summary>
        public IReadOnlyList<JsonObject> Requests { get; }
        /// <summary>"yyyy-MM-dd".</summary>
        public string Today { get; }
        public int Y { get; }
        public int M { get; }
        public int D { get; }
    }

    /// <summary>
    /// Memo of <c>_runACached</c> / <c>_runBCached</c> (index.html lines 7562 / 7570): the export-scoped
    /// <c>_runACache</c> / <c>_runBCache</c> Maps. Create one per batch / export and pass it to
    /// <see cref="DecisionEngine.Recommendation.IRecommendationEngine.RunACached"/>; pass null for no caching.
    /// </summary>
    public sealed class RecommendationRunCache
    {
        internal ConcurrentDictionary<string, RecAResultDto?> A { get; } = new(StringComparer.Ordinal);
        internal ConcurrentDictionary<string, RecBResultDto?> B { get; } = new(StringComparer.Ordinal);
    }
}
