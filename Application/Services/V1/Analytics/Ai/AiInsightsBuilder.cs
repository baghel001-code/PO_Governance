using System.Text.Json.Nodes;
using Application.DTOs.Analytics.Ai;
using Application.Services.V1.Analytics.Common;
using Application.Services.V1.Engines.Common;
using Domain.Entities.Common;

namespace Application.Services.V1.Analytics.Ai
{
    /// <summary>One entry of <c>_aiTopN</c>.</summary>
    public sealed record AiTopItem(string Key, int Count, double Excess);

    /// <summary>Result of <c>_aiBuildSummary</c>.</summary>
    public sealed class AiSummary
    {
        public List<string> Lines { get; set; } = new();
        public int TotalLines { get; set; }
        public int FlaggedCount { get; set; }
        public double TotalExcess { get; set; }
        public List<AiTopItem> TopVendors { get; set; } = new();
        public List<AiTopItem> TopDeliveryMonths { get; set; } = new();
        public List<AiTopItem> TopMRPs { get; set; } = new();
        public List<AiTopItem> TopMCs { get; set; } = new();
        public JsObjectMap<int> ByDecision { get; set; } = new();
    }

    /// <summary>
    /// Ports of the AI Insights script (index.html 11516–11732): <c>_aiTopN</c>, <c>_aiBuildSummary</c>,
    /// <c>_aiPromptFor</c>, <c>_aiParseJSON</c>, <c>_aiRenderKPIs</c>, <c>_aiRenderFindingCards</c>,
    /// <c>_aiRenderCallout</c> and the response handling of <c>renderAIInsights</c>.
    /// </summary>
    public static class AiInsightsBuilder
    {
        public static readonly IReadOnlyDictionary<string, string> FocusLabels = new Dictionary<string, string>
        {
            ["risk"] = "Highest-risk lines", ["brand"] = "Patterns by Brand/MC", ["action"] = "What to action first"
        };

        private sealed class Agg { public int Count; public double Excess; }

        /// <summary>Port of <c>_aiTopN(map, n)</c>.</summary>
        private static List<AiTopItem> TopN(JsObjectMap<Agg> map, int n)
        {
            var l = map.Select(kv => new AiTopItem(kv.Key, kv.Value.Count, kv.Value.Excess)).ToList();
            JsSort.StableSort(l, (a, b) => b.Excess - a.Excess);
            return l.Take(n).ToList();
        }

        private static string? Part(JsonObject r, string key)
        {
            var v = AnalyticsJs.Or(r, key, null);
            return v == null ? null : AnalyticsJs.Str(v);
        }

        /// <summary>Port of <c>_aiBuildSummary()</c>; null when the run has no rows.</summary>
        public static AiSummary? BuildSummary(IReadOnlyList<JsonObject> rows)
        {
            if (rows.Count == 0) return null;
            var flagged = rows.Where(r => AnalyticsJs.Get(r, "decision") is "Cancel" or "Reduce Qty" or "Watch List").ToList();
            var pool = flagged.Count > 0 ? flagged : rows.ToList();
            var byVendor = new JsObjectMap<Agg>();
            var byDm = new JsObjectMap<Agg>();
            var byMrp = new JsObjectMap<Agg>();
            var byMc = new JsObjectMap<Agg>();
            foreach (var r in pool)
            {
                var exc = AnalyticsJs.OrZero(r, "excessQty");
                foreach (var (map, key) in new[] { (byVendor, "vendor"), (byDm, "dm"), (byMrp, "mrp"), (byMc, "mcDesc") })
                {
                    var k = AnalyticsJs.OrStr(r, key, "Unknown");
                    if (!map.TryGetValue(k, out var a)) map[k] = a = new Agg();
                    a.Count++;
                    a.Excess += exc;
                }
            }
            var sample = pool.ToList();
            JsSort.StableSort(sample, (a, b) => AnalyticsJs.OrZero(b, "excessQty") - AnalyticsJs.OrZero(a, "excessQty"));
            var lines = sample.Take(60).Select(r =>
            {
                var bsrPart = AnalyticsJs.Or(r, "quartile_", null) != null ? "BSR " + AnalyticsJs.Concat(r, "quartile_") : "";
                var strPart = AnalyticsJs.NotNull(r, "artSTR") ? "STR " + AnalyticsJs.Concat(r, "artSTR") + "%"
                    : AnalyticsJs.NotNull(r, "pdstr") ? "%D-STR " + AnalyticsJs.Concat(r, "pdstr") : "";
                var parts = new[]
                {
                    Part(r, "div"), Part(r, "mcDesc"), "MRP " + AnalyticsJs.Concat(r, "mrp"), Part(r, "gm"), Part(r, "colour"),
                    bsrPart, strPart, "Vendor " + AnalyticsJs.OrStr(r, "vendor", "—"), "DelMonth " + AnalyticsJs.OrStr(r, "dm", "—"),
                    "RelQty " + AnalyticsJs.Concat(r, "relQty"), "RecQty " + AnalyticsJs.Concat(r, "recQty"), "Excess " + AnalyticsJs.Concat(r, "excessQty"),
                    "Stock " + AnalyticsJs.Concat(r, "opStock"), "Decision: " + AnalyticsJs.Concat(r, "decision")
                };
                return string.Join(" | ", parts.Where(p => !string.IsNullOrEmpty(p)));
            }).ToList();
            double totalExcess = 0;
            foreach (var r in flagged) totalExcess += AnalyticsJs.OrZero(r, "excessQty");
            var byDecision = new JsObjectMap<int>();
            foreach (var r in rows) { var k = AnalyticsJs.Concat(r, "decision"); byDecision[k] = byDecision.Get(k) + 1; }
            return new AiSummary
            {
                Lines = lines, TotalLines = rows.Count, FlaggedCount = flagged.Count, TotalExcess = totalExcess,
                TopVendors = TopN(byVendor, 5), TopDeliveryMonths = TopN(byDm, 5), TopMRPs = TopN(byMrp, 5), TopMCs = TopN(byMc, 5),
                ByDecision = byDecision
            };
        }

        private static string FmtAgg(List<AiTopItem> arr, string keyLabel) =>
            arr.Count == 0 ? "  (none)"
                : string.Join("\n", arr.Select(a => "  " + keyLabel + " \"" + a.Key + "\": " + a.Count + " lines, " + Js.LocaleIN(a.Excess) + " excess units"));

        /// <summary>JSON.stringify(summary.byDecision).</summary>
        public static string ByDecisionJson(AiSummary s)
        {
            var o = new JsonObject();
            foreach (var (k, v) in s.ByDecision) o[k] = v;
            return AnalyticsJs.Stringify(o);
        }

        /// <summary>Port of <c>_aiPromptFor(focus, summary)</c>.</summary>
        public static string PromptFor(string? focus, AiSummary summary)
        {
            var header = "You are a senior retail merchandise planning analyst for VMM (a large Indian retail chain), doing ROOT-CAUSE analysis on a purchase-order batch — not summarizing it.\n\n" +
                "IMPORTANT CONTEXT: the planner already sees, per line, the decision (Cancel/Reduce/Watch/Retain), excess quantity, BSR quartile and STR — that is all visible elsewhere in this tool. " +
                "Do NOT repeat \"line X has Y excess units\" as a finding — that has zero new value. Your job is to find things ONLY visible by looking ACROSS lines: concentration, clustering, correlation, and signs the rule engine itself might be miscalibrated.\n\n" +
                "Aggregated concentration stats, computed across ALL " + summary.FlaggedCount + " flagged lines (not just the sample below):\n" +
                "By Vendor:\n" + FmtAgg(summary.TopVendors, "Vendor") + "\n" +
                "By Delivery Month:\n" + FmtAgg(summary.TopDeliveryMonths, "Month") + "\n" +
                "By MRP band:\n" + FmtAgg(summary.TopMRPs, "MRP") + "\n" +
                "By MC:\n" + FmtAgg(summary.TopMCs, "MC") + "\n\n" +
                "Sample of individual flagged lines for supporting detail (Division | MC | MRP | Generic Material | Colour | BSR/STR | Vendor | Delivery Month | RelQty | RecQty | Excess | Stock | Decision):\n" +
                string.Join("\n", summary.Lines) + "\n\n" +
                "Decision breakdown across the full batch: " + ByDecisionJson(summary) + ".\n\n";

            var lensSpec = new Dictionary<string, string>
            {
                ["risk"] = "LENS: Highest-risk lines. Your 3 findings MUST be, in this order:\n" +
                    "  1. A VENDOR OR MRP CONCENTRATION finding — name the specific vendor or MRP band from the stats above carrying disproportionate excess, and why that matters commercially.\n" +
                    "  2. A TIMING/DELIVERY-MONTH CLUSTERING finding — name the specific delivery month from the stats above where excess clusters, and hypothesize why (e.g. forecast miss for that period vs. a vendor delivery/lead-time issue).\n" +
                    "  3. A RULE-MISCALIBRATION SIGNAL — find a specific line or MC in the sample where the decision looks inconsistent with its own BSR/STR (e.g. a healthy BSR quartile still flagged Reduce/Cancel, or a weak one Retained), and flag it for rule review.",
                ["brand"] = "LENS: Patterns by Brand/MC. Your 3 findings MUST be, in this order:\n" +
                    "  1. A DIVISION-LEVEL COMPARISON — compare which Division is carrying a disproportionate share of flagged lines or excess vs. the others, using the sample and decision breakdown.\n" +
                    "  2. An MC-LEVEL STRUCTURAL finding — name the specific MC from the \"By MC\" stats above that dominates excess, and whether that looks like one problematic style/fit or a broader MC-wide demand miss.\n" +
                    "  3. A CROSS-LINE or REPEAT-OFFENDER pattern — e.g. the same Generic Material or colour family repeating across multiple flagged lines/divisions, suggesting a single root SKU decision rather than many independent ones.",
                ["action"] = "LENS: What to action first. Your 3 findings MUST be a PRIORITIZED SEQUENCE, in this order:\n" +
                    "  1. DO TODAY — the single highest-urgency line or vendor/MRP cluster to act on immediately, naming specifics from the data above.\n" +
                    "  2. DO THIS WEEK — the next-priority cluster or pattern, less urgent but still concrete.\n" +
                    "  3. STRUCTURAL FIX — a process or rule-threshold change (not a line-level cancellation) that would prevent this category of excess recurring next cycle."
            };

            return header +
                "Respond with ONLY a valid JSON object (no markdown fences, no commentary before or after) matching exactly this shape:\n" +
                "{\n" +
                "  \"findings\": [ {\"title\": \"short headline, under 8 words\", \"impactQty\": number-or-null, \"insight\": \"1-2 sentences, specific and evidence-based per the lens instructions below. Never just restate one line's excess qty.\"} ],\n" +
                "  \"action\": {\"title\": \"under 8 words, imperative\", \"detail\": \"1-2 sentences — a process/vendor/forecast-level fix, not a generic 'cancel these lines'\"}\n" +
                "}\n" +
                "Include exactly 3 items in \"findings\", following this lens exactly:\n" + (focus != null && lensSpec.TryGetValue(focus, out var l) ? l : lensSpec["risk"]) + "\n\n" +
                "If a finding has no natural single number attached, set impactQty to null rather than forcing one. Output raw JSON only.";
        }

        /// <summary>Port of <c>_aiParseJSON(raw)</c>: strips ``` fences, takes the outermost {…}, parses; null on failure.</summary>
        public static JsonNode? ParseJson(string? raw)
        {
            if (string.IsNullOrEmpty(raw)) return null;
            var cleaned = raw.Trim();
            if (cleaned.StartsWith("```json", StringComparison.OrdinalIgnoreCase)) cleaned = cleaned[7..];
            if (cleaned.StartsWith("```", StringComparison.Ordinal)) cleaned = cleaned[3..];
            if (cleaned.EndsWith("```", StringComparison.Ordinal)) cleaned = cleaned[..^3];
            cleaned = cleaned.Trim();
            var start = cleaned.IndexOf('{');
            var end = cleaned.LastIndexOf('}');
            if (start == -1 || end == -1) return null;
            if (end < start) return null; // JS: slice(start, end+1) is "" → JSON.parse throws
            try
            {
                var node = JsonNode.Parse(cleaned.Substring(start, end - start + 1));
                if (node is JsonObject o) _ = o.Count; // force materialisation (duplicate keys throw here)
                return node;
            }
            catch (Exception) { return null; }
        }

        /// <summary>Port of <c>_aiRenderKPIs(summary)</c>.</summary>
        public static List<AiKpiDto> Kpis(AiSummary s) => new()
        {
            new() { Label = "Lines Analyzed", Value = s.TotalLines.ToString(System.Globalization.CultureInfo.InvariantCulture), Color = "var(--vmm-navy)" },
            new() { Label = "Flagged Lines", Value = s.FlaggedCount.ToString(System.Globalization.CultureInfo.InvariantCulture), Color = "var(--vmm-pink)" },
            new() { Label = "Total Excess Qty", Value = Js.LocaleIN(s.TotalExcess), Color = "#D97706" }
        };

        /// <summary><c>_aiEscape</c>'s String(s == null ? '' : s), without the HTML escaping (the portal escapes).</summary>
        private static string Text(JsonNode? n) => n switch
        {
            null => "",
            JsonValue v when AnalyticsJs.Get(new JsonObject { ["x"] = v.DeepClone() }, "x") is { } x => AnalyticsJs.Str(x),
            _ => AnalyticsJs.Str(n)
        };

        private static double? ImpactQty(JsonNode? n)
        {
            if (n == null) return null;
            if (n is JsonObject) return null;
            if (n is JsonArray a) return a.Count == 0 ? 0 : a.Count == 1 ? ImpactQty(a[0]) : null;
            var v = AnalyticsJs.Get(new JsonObject { ["x"] = n.DeepClone() }, "x");
            var d = AnalyticsJs.Number(v);
            return double.IsNaN(d) ? null : d;
        }

        /// <summary>Port of <c>_aiRenderFindingCards(findings)</c>.</summary>
        public static List<AiFindingCardDto> FindingCards(JsonArray findings)
        {
            var icons = new[] { "🧩", "🔗", "⚙️" };
            var list = new List<AiFindingCardDto>();
            for (var i = 0; i < findings.Count; i++)
            {
                var f = findings[i] as JsonObject;
                var qty = ImpactQty(f?["impactQty"]);
                list.Add(new AiFindingCardDto
                {
                    Icon = i < icons.Length ? icons[i] : "▪",
                    Title = Text(f?["title"]),
                    ImpactQty = qty,
                    ImpactQtyText = qty == null ? null : Js.LocaleIN(qty.Value),
                    Insight = Text(f?["insight"])
                });
            }
            return list;
        }

        /// <summary>Port of <c>_aiRenderCallout('✅','Recommended Action', …, obj)</c>; null when obj is falsy.</summary>
        public static AiCalloutDto? Callout(JsonNode? obj)
        {
            if (obj == null) return null;
            if (obj is JsonValue v)
            {
                var x = AnalyticsJs.Get(new JsonObject { ["x"] = v.DeepClone() }, "x");
                if (!Js.Truthy(x)) return null;
            }
            var o = obj as JsonObject;
            return new AiCalloutDto { Title = Text(o?["title"]), Detail = Text(o?["detail"]) };
        }

        /// <summary>
        /// The response handling of <c>renderAIInsights</c>: error → "AI error: …" in the fallback card; parsed
        /// findings → cards + callout; otherwise the raw text in the fallback card.
        /// </summary>
        public static AiInsightsDto Render(string? focus, AiSummary summary, int status, JsonNode? data)
        {
            var f = string.IsNullOrEmpty(focus) ? "risk" : focus!;
            var dto = new AiInsightsDto
            {
                Focus = f, TotalLines = summary.TotalLines, FlaggedCount = summary.FlaggedCount, TotalExcess = summary.TotalExcess,
                Kpis = Kpis(summary),
                LoadingText = "⏳ Looking for cross-cutting patterns across " + summary.FlaggedCount + " flagged lines…"
            };
            var obj = data as JsonObject ?? new JsonObject();
            dto.Provider = AnalyticsJs.Get(obj, "_provider") as string;
            var err = obj["error"];
            var errTruthy = err is JsonObject or JsonArray || (err is JsonValue && Js.Truthy(AnalyticsJs.Get(obj, "error")));
            var ok = status >= 200 && status < 300;
            if (!ok || errTruthy)
            {
                string msg;
                var m = err is JsonObject eo ? AnalyticsJs.Or(eo, "message", null) : null;
                if (m != null) msg = AnalyticsJs.Str(m);
                else if (errTruthy) msg = err is JsonValue ? AnalyticsJs.Str(AnalyticsJs.Get(obj, "error")) : AnalyticsJs.Str(err);
                else msg = "HTTP " + status;
                dto.State = "error";
                dto.FallbackText = "AI error: " + msg;
                dto.Toast = "AI Insights error — see message";
                return dto;
            }
            var raw = "";
            if (obj["choices"] is JsonArray ch && ch.Count > 0 && ch[0] is JsonObject c0 && c0["message"] is JsonObject msgObj)
            {
                var content = AnalyticsJs.Or(msgObj, "content", null);
                if (content != null) raw = AnalyticsJs.Str(content);
            }
            var parsed = ParseJson(raw) as JsonObject;
            var lensTag = "Lens: " + (FocusLabels.TryGetValue(f, out var lbl) ? lbl : FocusLabels["risk"]) + " · ";
            dto.Meta = lensTag + "based on " + summary.FlaggedCount + " flagged of " + summary.TotalLines + " total lines · generated just now, not saved";
            if (parsed != null && parsed["findings"] is JsonArray findings && findings.Count > 0)
            {
                dto.State = "ok";
                dto.Findings = FindingCards(findings);
                dto.Action = Callout(parsed["action"]);
            }
            else
            {
                dto.State = "fallback";
                dto.FallbackText = raw.Length > 0 ? raw : "No response generated.";
            }
            return dto;
        }
    }
}
