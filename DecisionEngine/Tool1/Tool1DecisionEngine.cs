using System.Text.Json.Nodes;
using DecisionEngine.Model;
using DecisionEngine.Recommendation;
using DecisionEngine.Results.Tool1;

namespace DecisionEngine.Tool1
{
    /// <summary>
    /// Tool 1 ("✏️ New Request") decision façade — index.html <c>formSubmit</c> after the ledger refresh:
    /// input checks, routing (<c>routeRequest</c>), Single Concept <c>runEngineB</c> with the Confirm Final
    /// Quantity step, Multi Concept <c>runEngineA</c> with the <c>_strCaution</c> heads-up, the NEW article
    /// result, and the result card (<c>renderResult</c>). Inputs are the data snapshot, today's requests of all
    /// users (the same-day ledger, as stored JSON objects) and "today" ("yyyy-MM-dd"); nothing is read from a
    /// clock or a store.
    /// </summary>
    public interface ITool1DecisionEngine
    {
        /// <summary>The form's dropdowns (GM → colours, months from <paramref name="today"/>) — <c>formPopulate*</c>.</summary>
        Tool1FormOptionsDto FormOptions(EngineData data, string? gm, string today);

        /// <summary>The field / required-file checks of formSubmit. Throws <c>EngineValidationException</c> with the v1.1.4 toast.</summary>
        (string Gm, string Colour, string Month, double Qty) CheckInput(EngineData data, Tool1RequestInputDto input);

        /// <summary>
        /// Decides one request. <paramref name="confirmation"/> answers the quantity pop-up (null = not answered yet:
        /// the outcome then has <see cref="Tool1Outcome.NeedsConfirmation"/> set when it applies).
        /// <paramref name="reducedLabel"/> applies the v1.1.4 README "Recommend Reduced Qty" relabel.
        /// </summary>
        Tool1Outcome Decide(EngineData data, IReadOnlyList<JsonObject> todayRequests, string today, int id,
                            string gm, string colour, string month, double qty, Tool1Confirmation? confirmation, bool reducedLabel = false);

        /// <summary>Single Concept engine alone (<c>runEngineB</c>) — e.g. the headroom re-check after a save conflict.</summary>
        Tool1EngineBResult RunEngineB(EngineData data, IReadOnlyList<JsonObject> todayRequests, string today,
                                      string gm, string colour, string month, double qty, Tool1InfoDto? info);

        /// <summary>The result card texts / figures (<c>renderResult</c>).</summary>
        Tool1CardDto BuildCard(Tool1Outcome outcome, EngineParams p, string today);
    }

    /// <summary>Default <see cref="ITool1DecisionEngine"/> over <see cref="Tool1Flow"/>, <see cref="Tool1EngineB"/>, <see cref="Tool1FormOptions"/> and <see cref="Tool1CardBuilder"/>.</summary>
    public sealed class Tool1DecisionEngine : ITool1DecisionEngine
    {
        private readonly Tool1Flow _flow;
        private readonly Tool1EngineB _engineB;
        private readonly Tool1FormOptions _options;

        /// <summary>Uses the given recommendation engine for the STR caution (default: a new <see cref="RecommendationEngine"/>).</summary>
        public Tool1DecisionEngine(IRecommendationEngine? recommendation = null)
        {
            _engineB = new Tool1EngineB();
            _flow = new Tool1Flow(_engineB, new Tool1EngineA(), recommendation ?? new RecommendationEngine());
            _options = new Tool1FormOptions();
        }

        public Tool1FormOptionsDto FormOptions(EngineData data, string? gm, string today) => _options.Build(data, gm, today);

        public (string Gm, string Colour, string Month, double Qty) CheckInput(EngineData data, Tool1RequestInputDto input) =>
            Tool1Flow.CheckInput(data, input);

        public Tool1Outcome Decide(EngineData data, IReadOnlyList<JsonObject> todayRequests, string today, int id,
                                   string gm, string colour, string month, double qty, Tool1Confirmation? confirmation, bool reducedLabel = false)
        {
            var o = _flow.Decide(data, todayRequests, today, id, gm, colour, month, qty, confirmation);
            if (reducedLabel) Tool1Flow.ApplyReducedLabel(o);
            return o;
        }

        public Tool1EngineBResult RunEngineB(EngineData data, IReadOnlyList<JsonObject> todayRequests, string today,
                                             string gm, string colour, string month, double qty, Tool1InfoDto? info) =>
            _engineB.Run(data, todayRequests, today, gm, colour, month, qty, info);

        public Tool1CardDto BuildCard(Tool1Outcome outcome, EngineParams p, string today) => Tool1CardBuilder.Build(outcome, p, today);
    }
}
