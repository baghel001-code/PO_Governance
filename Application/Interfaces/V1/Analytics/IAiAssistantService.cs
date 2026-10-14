using Application.DTOs.Analytics.Ai;
using Domain.Records;

namespace Application.Interfaces.V1.Analytics
{
    /// <summary>
    /// The VMM Assistant chat (index.html sendAIChat and its _aiBuildContext* helpers, lines 8859–9046) and
    /// AI Insights (third script, _aiBuildSummary … renderAIInsights, lines 11516–11732). The prompt and the
    /// grounded context are built on the server from the visible requests / a Tool 2 run; the provider is
    /// reached through <see cref="IAiGateway"/>.
    /// </summary>
    public interface IAiAssistantService
    {
        AssistantChatInfoDto GetChatInfo(string? scope);
        Task<AssistantChatContextDto> BuildChatContextAsync(AssistantChatRequest request, AppUserContext user, CancellationToken ct = default);
        Task<AssistantChatResponse> ChatAsync(AssistantChatRequest request, AppUserContext user, CancellationToken ct = default);
        Task<AiInsightsDto> InsightsAsync(AiInsightsRequest request, AppUserContext user, CancellationToken ct = default);
    }
}
