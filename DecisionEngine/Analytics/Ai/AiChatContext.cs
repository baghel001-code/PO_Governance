using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using DecisionEngine.Analytics.Common;
using DecisionEngine.Common;

namespace DecisionEngine.Analytics.Ai
{
    /// <summary>
    /// Ports of the chat context builders: <c>_aiKeywordFilter</c>, <c>_aiBuildContextTool1</c>,
    /// <c>_aiBuildContextTool2</c> and the system prompt of <c>sendAIChat</c> (index.html 8920–9022).
    /// </summary>
    public static class AiChatContext
    {
        public const int SampleCap = 40;
        /// <summary>Last N history entries sent with each question (<c>_aiChatHistory.slice(-8)</c>).</summary>
        public const int HistoryTurns = 8;
        /// <summary>max_tokens of Shared.aiChat.</summary>
        public const int MaxTokens = 1000;

        public static string ScreenLabel(bool tool2) => tool2 ? "Planner — Raised PO Assessment (Tool 2)" : "Category — PO Validation Requests (Tool 1)";
        public static string ScopeBadge(bool tool2) => tool2 ? "Planner · Tool 2" : "Category · Tool 1";

        /// <summary><c>_aiChatRenderWelcome</c>.</summary>
        public static string Welcome(bool tool2)
        {
            var scope = tool2 ? "this Raised PO Assessment run (Tool 2)" : "your PO validation requests this session (Tool 1)";
            return "Hi! I can answer questions about " + scope + " — e.g. \"why was X blocked?\", \"which MC is riskiest?\", \"summarize this run\". I only answer from data actually computed in this session — I won't invent numbers or override any decision.";
        }

        private static readonly Regex Split = new("[^a-z0-9]+", RegexOptions.Compiled);

        /// <summary>Port of <c>_aiKeywordFilter(rows, question, hayFn, cap)</c>.</summary>
        public static List<T> KeywordFilter<T>(IReadOnlyList<T> rows, string? question, Func<T, string> hay, int cap)
        {
            var words = Split.Split((question ?? "").ToLowerInvariant()).Where(w => w.Length >= 3).ToList();
            if (words.Count == 0) return rows.Take(cap).ToList();
            var matched = rows.Where(r => { var h = hay(r).ToLowerInvariant(); return words.Any(w => h.Contains(w, StringComparison.Ordinal)); }).ToList();
            return (matched.Count > 0 ? matched : rows.Skip(Math.Max(0, rows.Count - cap)).ToList()).Take(cap).ToList();
        }

        private static string Raw(JsonObject r, string key) => AnalyticsJs.Concat(r, key);
        private static string OrEmpty(object? v) => v == null ? "" : AnalyticsJs.Str(v);

        private static JsonObject Counts(JsObjectMap<int> m)
        {
            var o = new JsonObject();
            foreach (var (k, v) in m) o[k] = v;
            return o;
        }

        /// <summary>Port of <c>_aiBuildContextTool1(question)</c> over the user's visible requests.</summary>
        public static (JsonObject Summary, JsonArray Sample) Tool1(IReadOnlyList<JsonObject> reqs, string? question)
        {
            if (reqs.Count == 0)
                return (new JsonObject { ["note"] = "No PO validation requests have been submitted yet this session." }, new JsonArray());
            var dec = new JsObjectMap<int>();
            foreach (var r in reqs) { var k = Raw(r, "decision"); dec[k] = dec.Get(k) + 1; }
            var flagged = reqs.Where(r => AnalyticsJs.Get(r, "decision") is not ("Auto Allow" or "Allow")).ToList();
            var drivers = new JsObjectMap<int>();
            foreach (var r in flagged) { var b = DashboardMath.ClassifyReasonBucket(r); drivers[b] = drivers.Get(b) + 1; }
            var mcMap = new JsObjectMap<int[]>();
            foreach (var r in reqs)
            {
                var mc = AnalyticsJs.InfoStr(r, "mcDesc", "Unclassified");
                if (!mcMap.TryGetValue(mc, out var m)) mcMap[mc] = m = new int[4];
                m[0]++;
                var d = AnalyticsJs.Get(r, "decision") as string;
                if (d is "Auto Allow" or "Allow") m[1]++;
                else if (d == "Watch List") m[2]++;
                else if (d is "Block" or "Hold — Don't Raise") m[3]++;
            }
            var mcObj = new JsonObject();
            foreach (var (k, m) in mcMap) mcObj[k] = new JsonObject { ["total"] = m[0], ["allow"] = m[1], ["watch"] = m[2], ["block"] = m[3] };
            var summary = new JsonObject
            {
                ["totalRequests"] = reqs.Count, ["decisionCounts"] = Counts(dec), ["driverBreakdown"] = Counts(drivers), ["mcBreakdown"] = mcObj
            };
            var sample = new JsonArray();
            foreach (var r in KeywordFilter(reqs, question, r => Raw(r, "gm") + " " + Raw(r, "colour") + " " + OrEmpty(AnalyticsJs.Or(LedgerHelpers.Info(r), "mcDesc", null))
                                                               + " " + Raw(r, "decision") + " " + OrEmpty(AnalyticsJs.Or(r, "decisionReason", null)), SampleCap))
            {
                var o = new JsonObject();
                AnalyticsJs.CopyIfDefined(o, "gm", r, "gm");
                AnalyticsJs.CopyIfDefined(o, "colour", r, "colour");
                o["mc"] = AnalyticsJs.Node(AnalyticsJs.Or(LedgerHelpers.Info(r), "mcDesc", ""));
                AnalyticsJs.CopyIfDefined(o, "qty", r, "qty");
                AnalyticsJs.CopyIfDefined(o, "decision", r, "decision");
                AnalyticsJs.CopyIfDefined(o, "reason", r, "decisionReason");
                sample.Add(o);
            }
            return (summary, sample);
        }

        /// <summary>Port of <c>_aiBuildContextTool2(question)</c> over the rows of a Tool 2 run.</summary>
        public static (JsonObject Summary, JsonArray Sample) Tool2(IReadOnlyList<JsonObject> rows, string? question)
        {
            if (rows.Count == 0)
                return (new JsonObject { ["note"] = "No Raised PO Assessment has been run yet this session." }, new JsonArray());
            var dec = new JsObjectMap<int>();
            foreach (var r in rows) { var k = Raw(r, "decision"); dec[k] = dec.Get(k) + 1; }
            var flagged = rows.Where(r => AnalyticsJs.Get(r, "decision") is not ("Retain" or "Cannot Validate")).ToList();
            var drivers = new JsObjectMap<int>();
            foreach (var r in flagged) { var b = DashboardMath.DriverBucketT2(AnalyticsJs.Get(r, "decisionReason")); drivers[b] = drivers.Get(b) + 1; }
            var mcMap = new JsObjectMap<int[]>();
            foreach (var r in rows)
            {
                var mc = AnalyticsJs.OrStr(r, "mcDesc", "Unclassified");
                if (!mcMap.TryGetValue(mc, out var m)) mcMap[mc] = m = new int[4];
                m[0]++;
                var d = AnalyticsJs.Get(r, "decision") as string;
                if (d == "Retain") m[1]++;
                else if (d == "Watch List") m[2]++;
                else if (d is "Reduce Qty" or "Cancel") m[3]++;
            }
            var mcObj = new JsonObject();
            foreach (var (k, m) in mcMap) mcObj[k] = new JsonObject { ["total"] = m[0], ["retain"] = m[1], ["watch"] = m[2], ["cut"] = m[3] };
            var summary = new JsonObject
            {
                ["totalLines"] = rows.Count, ["decisionCounts"] = Counts(dec), ["driverBreakdown"] = Counts(drivers), ["mcBreakdown"] = mcObj
            };
            var sample = new JsonArray();
            foreach (var r in KeywordFilter(rows, question, r => OrEmpty(AnalyticsJs.Or(r, "poNum", null)) + " " + Raw(r, "gm") + " " + Raw(r, "colour") + " "
                                                               + OrEmpty(AnalyticsJs.Or(r, "mcDesc", null)) + " " + Raw(r, "decision") + " " + OrEmpty(AnalyticsJs.Or(r, "decisionReason", null)), SampleCap))
            {
                var o = new JsonObject();
                AnalyticsJs.CopyIfDefined(o, "po", r, "poNum");
                AnalyticsJs.CopyIfDefined(o, "gm", r, "gm");
                AnalyticsJs.CopyIfDefined(o, "colour", r, "colour");
                AnalyticsJs.CopyIfDefined(o, "mc", r, "mcDesc");
                o["engine"] = AnalyticsJs.Get(r, "engine") is "B" ? "Single Concept MRP" : "Multi Concept MRP";
                AnalyticsJs.CopyIfDefined(o, "strDays", r, "artSTR");
                AnalyticsJs.CopyIfDefined(o, "decision", r, "decision");
                AnalyticsJs.CopyIfDefined(o, "reason", r, "decisionReason");
                sample.Add(o);
            }
            return (summary, sample);
        }

        /// <summary>The system prompt of <c>sendAIChat</c>.</summary>
        public static string SystemPrompt(bool tool2, string summaryJson, string sampleJson) =>
            "You are the VMM Assistant, embedded in a PO governance portal. You are currently helping a user on the \"" + ScreenLabel(tool2) + "\" screen.\n\n"
            + "You will be given: (1) an aggregate JSON summary of this session's already-computed decisions, and (2) a sample of individual rows relevant to the question.\n\n"
            + "RULES — follow strictly:\n"
            + "1. Answer ONLY from the JSON data provided below. Never invent GM codes, colours, quantities, or reasons that are not present in the data.\n"
            + "2. Never override, second-guess, or re-derive any decision (Allow/Watch List/Block/Retain/Reduce Qty/Cancel). Those come from a deterministic rule engine elsewhere in this portal — your job is to explain and summarize them, not to make or change them.\n"
            + "3. If the answer isn't in the data provided (e.g. asking about a GM/PO not in the sample), say so plainly rather than guessing — suggest the user rephrase with the specific GM/colour/PO number if useful.\n"
            + "4. Keep answers concise and business-friendly — this is for a category/planning team, not engineers. Use plain numbers, not code.\n\n"
            + "SESSION DATA (JSON):\n"
            + "Aggregate summary: " + summaryJson + "\n"
            + "Sample of relevant rows (up to 40, matched to the question where possible): " + sampleJson;

        /// <summary>
        /// The text of a successful Messages API reply: the "text" blocks joined with "\n", trimmed, or
        /// "(no response text)".
        /// </summary>
        public static string AnswerText(JsonNode? data)
        {
            var parts = new List<string>();
            if (data is JsonObject o && o["content"] is JsonArray arr)
                foreach (var b in arr)
                    if (b is JsonObject bo && AnalyticsJs.Get(bo, "type") is "text")
                    {
                        var t = AnalyticsJs.Get(bo, "text");
                        parts.Add(t == null ? "" : AnalyticsJs.Str(t));
                    }
            var answer = string.Join("\n", parts).Trim();
            return answer.Length > 0 ? answer : "(no response text)";
        }

        /// <summary>The error text of a failed chat call (data.error.message → data.error string → "HTTP {status}").</summary>
        public static string ErrorText(int status, JsonNode? data)
        {
            var err = (data as JsonObject)?["error"];
            var msg = err is JsonObject eo && Js(eo, "message") is { } m ? m : "HTTP " + status;
            if (err is JsonValue && AnalyticsJs.Get((JsonObject)data!, "error") is string s) msg = s;
            return msg;
        }

        private static string? Js(JsonObject o, string key)
        {
            var v = AnalyticsJs.Or(o, key, null);
            return v == null ? null : AnalyticsJs.Str(v);
        }
    }
}
