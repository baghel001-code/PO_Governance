using System.Text.Json.Nodes;
using DecisionEngine.Common;
using DecisionEngine.Model;
using DecisionEngine.Recommendation;
using DecisionEngine.Results.Recommendation;
using DecisionEngine.Results.Tool1;

namespace DecisionEngine.Tool1
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
        /// <summary>v1.1.6 S5: the buyer's "Why introduce this colour?" (single concept Watch List; saved as <c>colourReason</c>).</summary>
        public string? ColourReason { get; set; }
        /// <summary>v1.1.6 B1: the BOBs sibling colours (saved as <c>bobsSiblings</c>); null when not BOBs.</summary>
        public List<Tool1BobsSiblingDto>? BobsSiblings { get; set; }
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
                throw new EngineValidationException("Please fill all 4 fields.");
            var missing = Tool1Router.MissingRequired(data);
            if (missing.Count > 0) throw new EngineValidationException("Upload required files first: " + string.Join(", ", missing));
            // v1.1.6 S6: an article that is not in the Article Master (or no Article Master) stops the request, single
            // and multi concept; nothing is saved. The portal shows it in the "Article not in Article Master" box.
            if (!InArticleMaster(data, gm)) throw new EngineValidationException(ArticleMasterMessage(data, gm));
            return (gm, colour, month, qty);
        }

        /// <summary>Title of the v1.1.6 message box for <see cref="ArticleMasterMessage"/>.</summary>
        public const string ArticleMasterTitle = "Article not in Article Master";

        /// <summary>The v1.1.6 <c>_amMsg</c> text (Article Master uploaded or not).</summary>
        public static string ArticleMasterMessage(EngineData data, string gm) => data.ArtMaster != null
            ? "Article " + gm + " is not in the Article Master. Ask the planning team to add it; this request cannot proceed. Nothing is saved."
            : "The Article Master is not uploaded, so article " + gm + " cannot be checked. Ask the planning team to upload it; this request cannot proceed. Nothing is saved.";

        /// <summary>Whether a text is one of the <see cref="ArticleMasterMessage"/> texts (the portal shows those in a message box).</summary>
        public static bool IsArticleMasterMessage(string? text) =>
            text != null && text.EndsWith("this request cannot proceed. Nothing is saved.", StringComparison.Ordinal) && text.Contains("Article Master", StringComparison.Ordinal);

        /// <summary>
        /// Port of the v1.1.6 <c>_inArticleMaster(code)</c>: the Generic Materials of the Article Master
        /// (<c>trim().replace(/\.0$/,'')</c>); false when the Article Master is missing or empty.
        /// </summary>
        public static bool InArticleMaster(EngineData data, string? gm)
        {
            var src = data.ArtMaster;
            if (src == null || src.Count == 0) return false;
            var set = data.GetOrBuild("tool1.artMasterGms", d =>
            {
                var s = new HashSet<string>(StringComparer.Ordinal);
                foreach (var r in d.ArtMaster!)
                {
                    var k = Tool1JsHelpers.StripDot0(r.Col("Generic Material", "generic material", "GENERIC MATERIAL", "GenMat"));
                    if (k.Length > 0) s.Add(k);
                }
                return s;
            });
            return set.Contains(Tool1JsHelpers.StripDot0(gm));
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
                    if (info == null) throw new EngineValidationException("Error: Cannot read properties of null (reading 'mcCd')");
                    o.StrCaution = _rec.StrCaution(new RecommendationContext(data, ledger, today), info.McCd, info.Mrp, info.Brand, info.Flag, colour, gm, month);
                }
                o.Result = Base("A", o.OptionType);
                Assign(o.Result, Tool1Json.ToObject(eng)!);
                // v1.1.6 B1: result.bobsSiblings = _bobsSibList(eng) (null when not BOBs)
                o.BobsSiblings = BobsSiblingList(eng);
                o.Result["bobsSiblings"] = Tool1Json.ToNode(o.BobsSiblings);
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

        private static readonly System.Text.RegularExpressions.Regex First3Week = new(@"first\s*3\s*week", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        private static readonly System.Text.RegularExpressions.Regex ThruSell = new(@"(thru|through|st\s*%|sell)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        /// <summary>
        /// Port of the v1.1.6 <c>_bobsSibList(eng)</c>: the BSR rows of a BOBs request's sibling colours (generic article,
        /// colour, weeks on floor, rank, sell-through per day, first 3 weeks sale / sell-through, BSR season), ranked first
        /// (Q1 … Q4, not ranked last), then most weeks on floor; at most 60. Null when not BOBs or no siblings. Display only.
        /// </summary>
        public static List<Tool1BobsSiblingDto>? BobsSiblingList(Tool1EngineAResult? eng)
        {
            if (eng == null || eng.OptionType != "BOBs" || eng.Siblings.Count == 0) return null;
            string? RankOf(SheetRow r)
            {
                foreach (var s in eng.SibRanked) if (ReferenceEquals(s.Row, r)) return s.Q;
                return null;
            }
            double? F3St(SheetRow r)
            {
                var k = Tool1JsHelpers.JsKeys(r).FirstOrDefault(h => First3Week.IsMatch(h) && ThruSell.IsMatch(h));
                return k != null && Js.ToJsString(r[k]).Trim().Length > 0 ? Js.Num(r[k]) : null;
            }
            var outL = eng.Siblings.Select(r => new Tool1BobsSiblingDto
            {
                Gm = r.Col("Generic Material", "generic material", "GENERIC MATERIAL").Trim(),
                Colour = r.Col("Color", "Colour", "COLOR", "COLOUR").Trim(),
                Wof = BsrPeerRank.GetWof(r), Q = RankOf(r), Pdstr = BsrPeerRank.GetPdstr(r),
                First3 = Js.Num(r.Col("First 3 Weeks Sale", "FIRST 3 WEEKS SALE")), First3St = F3St(r),
                Season = r.Col("SEASON", "Season").Trim()
            }).ToList();
            static double Qo(string? q) => q switch { "Q1" => 0, "Q2" => 1, "Q3" => 2, "Q4" => 3, _ => 9 };
            JsSort.StableSort(outL, (a, b) => Qo(a.Q) - Qo(b.Q) is var d && d != 0 ? d : b.Wof - a.Wof);
            return outL.Take(60).ToList();
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
