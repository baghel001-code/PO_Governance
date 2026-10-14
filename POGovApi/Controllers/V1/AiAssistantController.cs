using Application.DTOs.Analytics.Ai;
using Application.Interfaces.V1.Analytics;
using Domain.Entities.Response;
using Microsoft.AspNetCore.Mvc;

namespace POGovApi.Controllers.V1
{
    /// <summary>
    /// VMM Assistant chat and AI Insights with server-built, grounded prompts. (The raw provider proxy of
    /// v1.1.4 — /api/ai/chat, /api/ai/insights — stays at api/v1/ai/* on the platform controller.)
    /// </summary>
    [Route("api/v1/assistant")]
    public sealed class AiAssistantController : ApiControllerBase
    {
        private readonly IAiAssistantService _ai;

        public AiAssistantController(IAiAssistantService ai) => _ai = ai;

        /// <summary>Chat panel header: enabled flag, scope badge, welcome text (scope = tool1 / tool2).</summary>
        [HttpGet("chat/info")]
        public ActionResult<ApiResponse<AssistantChatInfoDto>> ChatInfo([FromQuery] string? scope)
        {
            if (string.Equals(scope, "tool2", StringComparison.OrdinalIgnoreCase)) RequireStaff();
            return Success(_ai.GetChatInfo(scope));
        }

        /// <summary>Asks a question; the answer is grounded in the visible requests (tool1) or a Tool 2 run (tool2, staff).</summary>
        [HttpPost("chat")]
        public async Task<ActionResult<ApiResponse<AssistantChatResponse>>> Chat([FromBody] AssistantChatRequest request, CancellationToken ct) =>
            Success(await _ai.ChatAsync(request ?? new AssistantChatRequest(), Me, ct));

        /// <summary>AI Insights for the latest (or a saved) Tool 2 run, focus = risk / brand / action. Staff.</summary>
        [HttpPost("insights")]
        public async Task<ActionResult<ApiResponse<AiInsightsDto>>> Insights([FromBody] AiInsightsRequest? request, CancellationToken ct)
        {
            RequireStaff();
            return Success(await _ai.InsightsAsync(request ?? new AiInsightsRequest(), Me, ct));
        }
    }
}
