using System.Text.Json.Nodes;
using Application.Interfaces.V1;
using Application.Interfaces.V1.Analytics;
using DecisionEngine.Analytics.Ai;
using DecisionEngine.Results.Analytics.Ai;
using Domain.Exceptions;
using Domain.Records;

namespace Application.Services.V1.Analytics.Ai
{
    /// <summary>
    /// Server side of the VMM Assistant chat (sendAIChat) and of AI Insights (renderAIInsights). The context,
    /// prompts and parsing are the v1.1.4 ones (<see cref="IAiAssistantEngine"/>, DecisionEngine); this service
    /// loads the rows the user may see and calls the provider through <see cref="IAiGateway"/> instead of the
    /// browser posting to /api/ai/*.
    /// </summary>
    public sealed class AiAssistantService : IAiAssistantService
    {
        /// <summary>server.py api_POST_ai truncates system / prompt to 400 000 characters.</summary>
        public const int MaxPromptChars = 400000;
        public const string ChatDisabledTitle = "AI chat is not enabled on this server";
        public const string ChatDisabledMessage = "The assistant runs through the portal server, which holds the company API key — users never paste keys into the browser. To turn it on, an administrator adds an Anthropic API key in the server configuration and restarts the service. The server also needs outbound HTTPS access to api.anthropic.com (directly or through the outbound proxy). Everything else in the portal works without it.";
        public const string InsightsDisabledToast = "AI Insights is not enabled on this server — ask your administrator (see README)";

        private readonly IAiGateway _ai;
        private readonly IRequestHistorySource _history;
        private readonly ITool2ResultSource _runs;
        private readonly IAppClock _clock;
        private readonly IAiAssistantEngine _engine;

        public AiAssistantService(IAiGateway ai, IRequestHistorySource history, ITool2ResultSource runs, IAppClock clock, IAiAssistantEngine engine)
        {
            _ai = ai;
            _history = history;
            _runs = runs;
            _clock = clock;
            _engine = engine;
        }

        private static bool IsTool2(string? scope) =>
            string.Equals(scope, "tool2", StringComparison.OrdinalIgnoreCase) || string.Equals(scope, "planner", StringComparison.OrdinalIgnoreCase);

        public AssistantChatInfoDto GetChatInfo(string? scope)
        {
            var enabled = _ai.ChatEnabled;
            var dto = _engine.ChatInfo(IsTool2(scope), enabled);
            dto.DisabledTitle = enabled ? null : ChatDisabledTitle;
            dto.DisabledMessage = enabled ? null : ChatDisabledMessage;
            return dto;
        }

        private async Task<IReadOnlyList<JsonObject>> Tool2Rows(string? runId, AppUserContext user, CancellationToken ct)
        {
            if (!string.IsNullOrEmpty(runId))
            {
                var run = await _runs.GetRunRowsAsync(runId!, user, ct);
                if (run == null) throw new NotFoundException("Run not found");
                return run.Rows;
            }
            return (await _runs.GetLatestRowsAsync(user, ct))?.Rows ?? Array.Empty<JsonObject>();
        }

        public async Task<AssistantChatContextDto> BuildChatContextAsync(AssistantChatRequest request, AppUserContext user, CancellationToken ct = default)
        {
            var t2 = IsTool2(request.Scope);
            if (t2 && !user.IsStaff) throw new ForbiddenException("Only planners and admins can do this");
            var rows = t2
                ? await Tool2Rows(request.RunId, user, ct)
                : await _history.GetVisibleRequestsAsync(user, _clock.Today, _clock.Today, ct);
            return _engine.BuildChatContext(t2, rows, request.Question, request.History);
        }

        public async Task<AssistantChatResponse> ChatAsync(AssistantChatRequest request, AppUserContext user, CancellationToken ct = default)
        {
            var q = (request.Question ?? "").Trim();
            if (q.Length == 0) throw new AppValidationException("Type a question");
            if (!_ai.ChatEnabled) throw new ServiceUnavailableException("AI chat is not enabled on this server (no anthropic_api_key in config.json).");
            request.Question = q;
            var ctx = await BuildChatContextAsync(request, user, ct);
            var history = request.History?.ToList() ?? new List<AiChatMessageDto>();
            var system = ctx.SystemPrompt.Length > MaxPromptChars ? ctx.SystemPrompt[..MaxPromptChars] : ctx.SystemPrompt;
            AiGatewayResult res;
            try
            {
                res = await _ai.ChatAsync(system, ctx.Messages.Select(m => new AiChatMessage(m.Role, m.Content)).ToList(), _engine.ChatMaxTokens, ct);
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                return new AssistantChatResponse { Ok = false, Error = "Could not reach the portal server: " + e.Message, History = history };
            }
            if (!res.Ok)
                return new AssistantChatResponse { Ok = false, Error = "Error: " + _engine.ChatErrorText(res.Status, res.Data), History = history };
            var answer = _engine.ChatAnswerText(res.Data);
            history.Add(new AiChatMessageDto("user", q));
            history.Add(new AiChatMessageDto("assistant", answer));
            return new AssistantChatResponse { Ok = true, Answer = answer, History = history };
        }

        public async Task<AiInsightsDto> InsightsAsync(AiInsightsRequest request, AppUserContext user, CancellationToken ct = default)
        {
            var focus = string.IsNullOrEmpty(request.Focus) ? "risk" : request.Focus!;
            if (!_ai.InsightsEnabled) return new AiInsightsDto { State = "disabled", Focus = focus, Toast = InsightsDisabledToast };
            var summary = _engine.BuildInsightsSummary(await Tool2Rows(request.RunId, user, ct));
            if (summary == null) return new AiInsightsDto { State = "noRun", Focus = focus, Toast = "Run an assessment first — no batch results found" };
            var prompt = _engine.InsightsPrompt(focus, summary);
            if (prompt.Length > MaxPromptChars) prompt = prompt[..MaxPromptChars];
            try
            {
                var res = await _ai.InsightsAsync(prompt, ct);
                return _engine.RenderInsights(focus, summary, res.Status, res.Data);
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                var dto = _engine.RenderInsights(focus, summary, 200, new JsonObject());
                dto.State = "error";
                dto.Meta = "";
                dto.FallbackText = "Request failed: " + e.Message;
                dto.Toast = "Could not reach the portal server";
                return dto;
            }
        }
    }
}
