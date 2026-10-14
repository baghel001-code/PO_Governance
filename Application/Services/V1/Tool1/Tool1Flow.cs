using System.Text.Json.Nodes;
using Application.DTOs.Recommendation;
using Application.DTOs.Tool1;
using Application.Interfaces.V1.Recommendation;
using Application.Services.V1.Engines.Common;
using Application.Services.V1.Recommendation;
using Domain.Constants;
using Domain.Entities.Common;
using Domain.Entities.Engine;
using Domain.Exceptions;

namespace Application.Services.V1.Tool1
{
    /// <summary>The answer to the "Confirm Final Quantity" pop-up.</summary>
    public sealed record Tool1Confirmation(double? Value, bool Declined)
    {
        /// <summary>
        /// Port of <c>qcConfirm</c> / <c>qcDecline</c> (index.html line 1866): parseInt(value)||0, clamped to
        /// 0 … parseInt(max)||0; "Don't Raise" = 0.
        /// </summary>
        public double Resolve(double maxPermQty)
        {
            if (Declined) return 0;
            var max = Trunc(maxPermQty);
            var v = Trunc(Value ?? double.NaN);
            if (v < 0) v = 0;
            if (v > max) v = max;
            return v;
        }

        private static double Trunc(double v) => double.IsFinite(v) ? Math.Truncate(v) + 0.0 : 0;
    }

    /// <summary>Everything one Tool 1 decision produced (the <c>result</c> object of formSubmit and its parts).</summary>
    public sealed class Tool1Outcome
    {
        public int Id { get; set; }
        public string Gm { get; set; } = "";
        public string Colour { get; set; } = "";
        public string Month { get; set; } = "";
        public double Qty { get; set; }
        public string Engine { get; set; } = "";
        /// <summary>As stored: "—" for Single Concept, "True Repeat" / "BOBs", "New Option".</summary>
        public string OptionType { get; set; } = "";
        public Tool1InfoDto? Info { get; set; }
        /// <summary>Single Concept result after the confirmation re-run (what the card shows).</summary>
        public Tool1EngineBResult? B { get; set; }
        /// <summary>Single Concept result for the proposed quantity (before the confirmation re-run).</summary>
        public Tool1EngineBResult? BFirst { get; set; }
        public Tool1EngineAResult? A { get; set; }
        public StrCautionDto? StrCaution { get; set; }
        /// <summary>The pop-up is required (Max Permissible &gt; 0) but no answer was given yet.</summary>
        public bool NeedsConfirmation { get; set; }
        /// <summary>Max Permissible of the first run when the pop-up applies.</summary>
        public double? ConfirmMax { get; set; }
        public double? FinalApprovedQty { get; set; }
        public string Decision { get; set; } = "";
        public string DecisionReason { get; set; } = "";
        public string ReqDate { get; set; } = "";
        /// <summary>The JS <c>result</c> object (field names / order as v1.1.4).</summary>
        public JsonObject Result { get; set; } = new();
    }

    /// <summary>
    /// Port of the Category form's <c>formSubmit</c> (index.html line 8471) decision part — after the
    /// ledger refresh and before <c>Shared.saveRequest</c>: field checks, routing, Engine B with the
    /// Confirm Final Quantity step (re-run at the confirmed quantity), Engine A with the
    /// <c>_strCaution</c> heads-up on a Q1/Q2 Allow, and the NEW article result.
    /// </summary>
    public sealed class Tool1Flow
    {
        private readonly Tool1EngineB _b;
        private readonly Tool1EngineA _a;
        private readonly IRecommendationEngine _rec;

        public Tool1Flow(Tool1EngineB b, Tool1EngineA a, IRecommendationEngine rec)
        {
            _b = b; _a = a; _rec = rec;
        }

        /// <summary>formGetColour: a chosen option as is, else the typed text trimmed and upper-cased.</summary>
        public static string GetColour(string? selected, string? text) =>
            !string.IsNullOrEmpty(selected) && selected != "__new__" ? selected : (text ?? "").Trim().ToUpperInvariant();

        /// <summary>
        /// The input checks of formSubmit (toasts → 400): all 4 fields, then the required files.
        /// Returns (gm, colour, month, qty).
        /// </summary>
        public static (string Gm, string Colour, string Month, double Qty) CheckInput(EngineData data, Tool1RequestInputDto input)
        {
            var gm = (input.Gm ?? "").Trim();
            var colour = GetColour(input.Colour, input.ColourText);
            var month = (input.Month ?? "").Trim();
            var q = input.Qty ?? double.NaN;
            var qty = double.IsFinite(q) ? Math.Truncate(q) + 0.0 : 0;
            if (gm.Length == 0 || colour.Length == 0 || month.Length == 0 || qty == 0 || qty <= 0)
                throw new AppValidationException("Please fill all 4 fields.");
            var missing = Tool1Router.MissingRequired(data);
            if (missing.Count > 0) throw new AppValidationException("Upload required files first: " + string.Join(", ", missing));
            return (gm, colour, month, qty);
        }

        /// <summary>
        /// Decides one request. <paramref name="confirmation"/> answers the quantity pop-up; null = not
        /// answered yet (the outcome then has <see cref="Tool1Outcome.NeedsConfirmation"/> set when it applies).
        /// </summary>
        public Tool1Outcome Decide(EngineData data, IReadOnlyList<JsonObject> ledger, string today, int id,
                                   string gm, string colour, string month, double qty, Tool1Confirmation? confirmation)
        {
            var info = Tool1Router.Resolve(data, gm);
            var (engine, optionType) = Tool1Router.Route(data, gm, colour);
            var o = new Tool1Outcome { Id = id, Gm = gm, Colour = colour, Month = month, Qty = qty, Info = info, ReqDate = today };
            JsonObject Base(string eng, string opt) => new()
            {
                ["id"] = id, ["pfx"] = "c", ["gm"] = gm, ["colour"] = colour, ["month"] = month, ["qty"] = qty,
                ["engine"] = eng, ["optionType"] = opt, ["info"] = Tool1Json.ToObject(info)
            };

            if (engine == "B")
            {
                o.Engine = "B"; o.OptionType = "—";
                var eng = _b.Run(data, ledger, today, gm, colour, month, qty, info);
                o.BFirst = o.B = eng;
                o.Result = Base("B", "—");
                Assign(o.Result, Tool1Json.ToObject(eng)!);
                if (eng.MaxPermQty is > 0)
                {
                    o.ConfirmMax = eng.MaxPermQty;
                    if (confirmation == null) o.NeedsConfirmation = true;
                    else
                    {
                        var confirmed = confirmation.Resolve(eng.MaxPermQty.Value);
                        o.FinalApprovedQty = confirmed;
                        o.Result["finalApprovedQty"] = confirmed;
                        if (confirmed > 0 && confirmed != qty)
                        {
                            var rerun = _b.Run(data, ledger, today, gm, colour, month, confirmed, info);
                            o.B = rerun;
                            Assign(o.Result, Tool1Json.ToObject(rerun)!);
                            o.Result["finalApprovedQty"] = confirmed;
                        }
                    }
                }
                o.Decision = o.B!.Decision; o.DecisionReason = o.B.DecisionReason;
            }
            else if (engine == "A")
            {
                o.Engine = "A"; o.OptionType = optionType ?? "";
                var eng = _a.Run(data, gm, colour, o.OptionType, info);
                o.A = eng;
                if (eng.Decision == Decisions.Allow && (eng.Quartile == "Q1" || eng.Quartile == "Q2"))
                {
                    if (info == null) throw new AppValidationException("Error: Cannot read properties of null (reading 'mcCd')");
                    o.StrCaution = _rec.StrCaution(new RecommendationContext(data, ledger, today), info.McCd, info.Mrp, info.Brand, info.Flag, colour, gm, month);
                }
                o.Result = Base("A", o.OptionType);
                Assign(o.Result, Tool1Json.ToObject(eng)!);
                if (eng.Decision == Decisions.Allow && (eng.Quartile == "Q1" || eng.Quartile == "Q2"))
                    o.Result["strCaution"] = CautionJson(o.StrCaution);
                o.Decision = eng.Decision; o.DecisionReason = eng.DecisionReason;
            }
            else
            {
                o.Engine = "NEW"; o.OptionType = "New Option";
                o.Decision = Decisions.AutoAllow;
                o.DecisionReason = "Not in Universe Master or CY BSR. New article.";
                o.Result = Base("NEW", "New Option");
                o.Result["decision"] = o.Decision;
                o.Result["decisionReason"] = o.DecisionReason;
                o.Result["chain"] = new JsonArray();
                o.Result["mixFailed"] = false;
            }
            o.Result["reqDate"] = today;
            return o;
        }

        /// <summary>
        /// v1.1.4 README "What changed" item 1: a Single Concept result whose STR breaches the threshold but
        /// whose Max Permissible is above 0 is "Recommend Reduced Qty" (Hold means zero headroom only), and it
        /// stays Reduced after the planner confirms a smaller quantity (the re-run alone would relabel it Auto Allow).
        /// </summary>
        public static void ApplyReducedLabel(Tool1Outcome o)
        {
            if (o.Engine != "B" || o.BFirst == null) return;
            if (o.BFirst.Decision == Decisions.Hold && o.BFirst.MaxPermQty is > 0)
            {
                o.Decision = Decisions.ReduceRecommended;
                o.Result["decision"] = o.Decision;
            }
        }

        /// <summary>The stored <c>strCaution</c> object: the JS fields only (effT2, delMonth, items[{label, str, cs}]).</summary>
        public static JsonNode? CautionJson(StrCautionDto? c)
        {
            var n = Tool1Json.ToObject(c);
            if (n?["items"] is JsonArray items)
                foreach (var it in items.OfType<JsonObject>()) { it.Remove("text"); it.Remove("footer"); }
            return n;
        }

        /// <summary>Object.assign(target, src): existing keys keep their position.</summary>
        public static void Assign(JsonObject target, JsonObject src)
        {
            foreach (var kv in src.ToList()) target[kv.Key] = kv.Value?.DeepClone();
        }
    }
}
