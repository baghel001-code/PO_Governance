using System.Text.Json.Nodes;
using DecisionEngine.Analytics.Common;
using DecisionEngine.Results.Analytics.Ai;

namespace DecisionEngine.Analytics.Ai
{
    /// <summary>
    /// Prompt / context building and response parsing of the VMM Assistant chat (index.html sendAIChat and its
    /// _aiBuildContext* helpers, lines 8859–9046) and of AI Insights (third script, _aiBuildSummary …
    /// renderAIInsights, lines 11516–11732). No transport: the caller sends the prompt to the provider and
    /// passes the provider's answer back for parsing.
    /// </summary>
    public interface IAiAssistantEngine
    {
        /// <summary>Scope badge + welcome text of the chat panel.</summary>
        AssistantChatInfoDto ChatInfo(bool tool2, bool enabled);

        /// <summary>
        /// System prompt + messages (last 8 history entries + the question) grounded on the visible Tool 1
        /// requests (<paramref name="tool2"/> = false) or the rows of a Tool 2 run.
        /// </summary>
        AssistantChatContextDto BuildChatContext(bool tool2, IReadOnlyList<JsonObject> rows, string question, IEnumerable<AiChatMessageDto>? history);

        /// <summary>Output-token cap of a chat call.</summary>
        int ChatMaxTokens { get; }

        /// <summary>The answer text of a successful chat response (text blocks joined, trimmed).</summary>
        string ChatAnswerText(JsonNode? data);

        /// <summary>The error text shown for a failed chat response.</summary>
        string ChatErrorText(int status, JsonNode? data);

        /// <summary>Port of _aiBuildSummary over the rows of a Tool 2 run; null when there are no rows.</summary>
        AiSummary? BuildInsightsSummary(IReadOnlyList<JsonObject> rows);

        /// <summary>The AI Insights prompt for a focus ("risk", "vendor", …).</summary>
        string InsightsPrompt(string focus, AiSummary summary);

        /// <summary>Port of renderAIInsights: KPIs, finding cards and callouts from the provider response.</summary>
        AiInsightsDto RenderInsights(string focus, AiSummary summary, int status, JsonNode? data);
    }

    /// <summary>Default <see cref="IAiAssistantEngine"/> over <see cref="AiChatContext"/> and <see cref="AiInsightsBuilder"/>.</summary>
    public sealed class AiAssistantEngine : IAiAssistantEngine
    {
        public AssistantChatInfoDto ChatInfo(bool tool2, bool enabled) => new()
        {
            Enabled = enabled, ScopeBadge = AiChatContext.ScopeBadge(tool2), Welcome = AiChatContext.Welcome(tool2)
        };

        public AssistantChatContextDto BuildChatContext(bool tool2, IReadOnlyList<JsonObject> rows, string question, IEnumerable<AiChatMessageDto>? history)
        {
            var (summary, sample) = tool2 ? AiChatContext.Tool2(rows, question) : AiChatContext.Tool1(rows, question);
            return BuildContext(tool2, summary, sample, history, question);
        }

        /// <summary>System prompt + messages (last 8 history entries + the question).</summary>
        public static AssistantChatContextDto BuildContext(bool tool2, JsonObject summary, JsonArray sample, IEnumerable<AiChatMessageDto>? history, string question)
        {
            var summaryJson = AnalyticsJs.Stringify(summary);
            var sampleJson = AnalyticsJs.Stringify(sample);
            var hist = (history ?? Enumerable.Empty<AiChatMessageDto>()).ToList();
            var messages = hist.Skip(Math.Max(0, hist.Count - AiChatContext.HistoryTurns)).Select(m => new AiChatMessageDto(m.Role, m.Content)).ToList();
            messages.Add(new AiChatMessageDto("user", question));
            return new AssistantChatContextDto
            {
                SystemPrompt = AiChatContext.SystemPrompt(tool2, summaryJson, sampleJson),
                SummaryJson = summaryJson, SampleJson = sampleJson, Messages = messages
            };
        }

        public int ChatMaxTokens => AiChatContext.MaxTokens;

        public string ChatAnswerText(JsonNode? data) => AiChatContext.AnswerText(data);

        public string ChatErrorText(int status, JsonNode? data) => AiChatContext.ErrorText(status, data);

        public AiSummary? BuildInsightsSummary(IReadOnlyList<JsonObject> rows) => AiInsightsBuilder.BuildSummary(rows);

        public string InsightsPrompt(string focus, AiSummary summary) => AiInsightsBuilder.PromptFor(focus, summary);

        public AiInsightsDto RenderInsights(string focus, AiSummary summary, int status, JsonNode? data) =>
            AiInsightsBuilder.Render(focus, summary, status, data);
    }
}
