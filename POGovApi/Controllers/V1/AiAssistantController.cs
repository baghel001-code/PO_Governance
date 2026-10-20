using Application.Interfaces.V1.Analytics;
using DecisionEngine.Results.Analytics.Ai;
using Domain.Entities.Request;
using Domain.Entities.Response;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace POGovApi.Controllers.V1
{
    /// <summary>
    /// VMM Assistant chat and AI Insights with server-built, grounded prompts. (The raw provider proxy of
    /// v1.1.4 — /api/ai/chat, /api/ai/insights — is AiChat / AiInsights on the platform AiController.)
    /// </summary>
    [Route("api/v1/[Action]")]
    public sealed class AiAssistantController : ApiControllerBase
    {
        private readonly IAiAssistantService _ai;

        public AiAssistantController(IAiAssistantService ai) => _ai = ai;

        /// <summary>Chat panel header: enabled flag, scope badge, welcome text (Scope = tool1 / tool2).</summary>
        [HttpPost]
        public ActionResult<ApiDataResponse<AssistantChatInfoDto>> AssistantChatInfo([FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] ScopeReq? req)
        {
            if (string.Equals(req?.Scope, "tool2", StringComparison.OrdinalIgnoreCase)) RequireStaff();
            return Success(_ai.GetChatInfo(req?.Scope));
        }

        /// <summary>Asks a question; the answer is grounded in the visible requests (tool1) or a Tool 2 run (tool2, staff).</summary>
        [HttpPost]
        public async Task<ActionResult<ApiDataResponse<AssistantChatResponse>>> AssistantChat([FromBody] AssistantChatRequest req, CancellationToken ct) =>
            Success(await _ai.ChatAsync(req ?? new AssistantChatRequest(), Me, ct));

        /// <summary>AI Insights for the latest (or a saved) Tool 2 run, focus = risk / brand / action. Staff.</summary>
        [HttpPost]
        public async Task<ActionResult<ApiDataResponse<AiInsightsDto>>> AssistantInsights([FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] AiInsightsRequest? req, CancellationToken ct)
        {
            RequireStaff();
            return Success(await _ai.InsightsAsync(req ?? new AiInsightsRequest(), Me, ct));
        }
    }
}
