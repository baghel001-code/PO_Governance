using System.Text.Json.Nodes;

namespace Application.Interfaces.V1.Analytics
{
    /// <summary>
    /// The AI provider behind the assistant (server.py <c>anthropic_call</c> / <c>insights_call</c>). Implemented
    /// by the platform's AI client (the same one that serves the raw api/v1/ai/* proxy endpoints); the
    /// coordinator wires it. Implementations never throw for HTTP / network failures — they return the
    /// status and the provider's (or a synthesized) error body, exactly like server.py's <c>_post_json</c>.
    /// </summary>
    public interface IAiGateway
    {
        /// <summary>server.py <c>bool(CFG.get("anthropic_api_key"))</c>.</summary>
        bool ChatEnabled { get; }

        /// <summary>server.py <c>bool(CFG.get("groq_api_key") or CFG.get("anthropic_api_key"))</c>.</summary>
        bool InsightsEnabled { get; }

        /// <summary>
        /// <c>anthropic_call(system, messages, max_tokens)</c>: POST https://api.anthropic.com/v1/messages
        /// {model, max_tokens, system, messages}. Data = the Messages API JSON ({content:[{type:"text",text}]…})
        /// or an error body ({error:{message}} / {error:"…"}).
        /// </summary>
        Task<AiGatewayResult> ChatAsync(string system, IReadOnlyList<AiChatMessage> messages, int maxTokens, CancellationToken ct = default);

        /// <summary>
        /// <c>insights_call(prompt)</c>: Groq (JSON mode, temperature 0.5) when a Groq key is set, otherwise Anthropic
        /// (max_tokens 2000, JSON-only system prompt). Data is always the OpenAI-style shape
        /// {choices:[{message:{content}}], _provider} on success, or the provider's error body.
        /// </summary>
        Task<AiGatewayResult> InsightsAsync(string prompt, CancellationToken ct = default);
    }

    /// <summary>One chat turn ("user" / "assistant").</summary>
    public sealed record AiChatMessage(string Role, string Content);

    /// <summary>HTTP status and JSON body returned by the provider (or synthesized: 502 "could not reach the AI service").</summary>
    public sealed record AiGatewayResult(int Status, JsonNode? Data)
    {
        public bool Ok => Status >= 200 && Status < 300;
    }
}
