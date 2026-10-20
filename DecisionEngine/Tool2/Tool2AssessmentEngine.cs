using System.Text.Json.Nodes;
using DecisionEngine.Common;
using DecisionEngine.Model;
using DecisionEngine.Recommendation;

namespace DecisionEngine.Tool2
{
    /// <summary>
    /// Tool 2 (Raised PO Assessment) façade — index.html <c>runTool2Batch</c> (Engine B2 fast path, Engine A,
    /// NEW) over every assessable F2 line, plus the workspace that builds the screen views, filters, roll-ups and
    /// the Excel / CSV export model of a run. Inputs: a <see cref="RecommendationContext"/> (data snapshot +
    /// today's requests + today); nothing is read from a clock or a store.
    /// </summary>
    public interface ITool2AssessmentEngine
    {
        /// <summary>Assesses every F2 line. Throws <c>EngineValidationException</c> when F2 is missing / has no valid line.</summary>
        List<Tool2LineResult> Assess(RecommendationContext ctx);

        /// <summary>The F2 rows that would be assessed (Generic Material + Colour + Delivery Month present).</summary>
        int AssessableLines(EngineData data);

        /// <summary>The workspace (views, filters, roll-ups, export model) of a set of results shown with <paramref name="ctx"/>'s data.</summary>
        Tool2Workspace Workspace(IReadOnlyList<Tool2LineResult> results, RecommendationContext ctx);

        /// <summary>shared.js <c>runSummary(list)</c>: decision counts, qty and ₹ at risk (stored in the run's meta).</summary>
        (JsonObject Counts, double AtRisk, double ValueAtRisk) RunSummary(IReadOnlyList<Tool2LineResult> results);
    }

    /// <summary>Default <see cref="ITool2AssessmentEngine"/> over <see cref="Tool2Engine"/> and <see cref="Tool2Workspace"/>.</summary>
    public sealed class Tool2AssessmentEngine : ITool2AssessmentEngine
    {
        private readonly IRecommendationEngine _rec;

        /// <summary>Uses the given recommendation engine (STR caution, recommended quantities); default a new <see cref="RecommendationEngine"/>.</summary>
        public Tool2AssessmentEngine(IRecommendationEngine? recommendation = null) => _rec = recommendation ?? new RecommendationEngine();

        public List<Tool2LineResult> Assess(RecommendationContext ctx) => Tool2Engine.Run(ctx, _rec);

        public int AssessableLines(EngineData data) => Tool2Engine.AssessableRows(data).Count;

        public Tool2Workspace Workspace(IReadOnlyList<Tool2LineResult> results, RecommendationContext ctx) => new(results, ctx, _rec);

        (JsonObject Counts, double AtRisk, double ValueAtRisk) ITool2AssessmentEngine.RunSummary(IReadOnlyList<Tool2LineResult> results) => RunSummary(results);

        /// <summary>shared.js <c>runSummary(list)</c>: decision counts, qty and ₹ at risk.</summary>
        public static (JsonObject Counts, double AtRisk, double ValueAtRisk) RunSummary(IReadOnlyList<Tool2LineResult> list)
        {
            var counts = new JsonObject();
            double atRisk = 0, value = 0;
            foreach (var r in list)
            {
                counts[r.Decision] = (counts[r.Decision]?.GetValue<int>() ?? 0) + 1;
                if (r.Decision != Decisions.Retain && r.Decision != Decisions.CannotValidate)
                {
                    var cut = r.ExcessQty;
                    atRisk += cut;
                    value += Tool2Workspace.ValueAtRisk(r, cut);
                }
            }
            return (counts, Js.Round(atRisk), Js.Round(value));
        }
    }
}
