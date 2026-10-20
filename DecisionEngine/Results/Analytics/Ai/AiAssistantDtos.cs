namespace DecisionEngine.Results.Analytics.Ai
{
    public sealed class AiChatMessageDto
    {
        public AiChatMessageDto() { }
        public AiChatMessageDto(string role, string content) { Role = role; Content = content; }
        /// <summary>"user" or "assistant".</summary>
        public string Role { get; set; } = "";
        public string Content { get; set; } = "";
    }

    /// <summary>PO_ASSISTANT_CHAT (api/v1/AssistantChat).</summary>
    public sealed class AssistantChatRequest
    {
        /// <summary>"tool1" (Category — requests) or "tool2" (Planner — Raised PO Assessment; staff only).</summary>
        public string Scope { get; set; } = "tool1";
        public string Question { get; set; } = "";
        /// <summary>The conversation so far (the portal keeps it; the server sends only the last 8 turns).</summary>
        public List<AiChatMessageDto> History { get; set; } = new();
        /// <summary>Tool 2 only: a saved run; null = the latest run.</summary>
        public string? RunId { get; set; }
    }

    public sealed class AssistantChatResponse
    {
        public bool Ok { get; set; }
        /// <summary>The assistant's answer (text blocks joined with "\n", trimmed; "(no response text)" when empty).</summary>
        public string? Answer { get; set; }
        /// <summary>Error bubble text ("Error: …" / "Could not reach the portal server: …").</summary>
        public string? Error { get; set; }
        /// <summary>History to send next time (question + answer appended on success; unchanged on error).</summary>
        public List<AiChatMessageDto> History { get; set; } = new();
    }

    /// <summary>Chat panel header / welcome (_aiChatRefreshView, _aiChatRenderWelcome).</summary>
    public sealed class AssistantChatInfoDto
    {
        public bool Enabled { get; set; }
        /// <summary>"Planner · Tool 2" / "Category · Tool 1".</summary>
        public string ScopeBadge { get; set; } = "";
        public string Welcome { get; set; } = "";
        /// <summary>Shown instead of the conversation when chat is not configured on the server.</summary>
        public string? DisabledTitle { get; set; }
        public string? DisabledMessage { get; set; }
    }

    /// <summary>The grounded context of one question (for tests / diagnostics).</summary>
    public sealed class AssistantChatContextDto
    {
        public string SystemPrompt { get; set; } = "";
        /// <summary>JSON.stringify(summary) / JSON.stringify(sample) exactly as embedded in the prompt.</summary>
        public string SummaryJson { get; set; } = "";
        public string SampleJson { get; set; } = "";
        public List<AiChatMessageDto> Messages { get; set; } = new();
    }

    /// <summary>PO_ASSISTANT_INSIGHTS (api/v1/AssistantInsights).</summary>
    public sealed class AiInsightsRequest
    {
        /// <summary>"risk" (Highest-risk lines, default), "brand" (Patterns by Brand/MC), "action" (What to action first).</summary>
        public string? Focus { get; set; }
        public string? RunId { get; set; }
    }

    public sealed class AiKpiDto
    {
        public string Label { get; set; } = "";
        public string Value { get; set; } = "";
        /// <summary>CSS colour (top border + value): var(--vmm-navy) / var(--vmm-pink) / #D97706.</summary>
        public string Color { get; set; } = "";
    }

    public sealed class AiFindingCardDto
    {
        /// <summary>🧩 / 🔗 / ⚙️ for the first three, ▪ after.</summary>
        public string Icon { get; set; } = "";
        public string Title { get; set; } = "";
        public double? ImpactQty { get; set; }
        /// <summary>impactQty.toLocaleString('en-IN') followed by " units impacted"; null = no number line.</summary>
        public string? ImpactQtyText { get; set; }
        public string Insight { get; set; } = "";
    }

    public sealed class AiCalloutDto
    {
        public string Icon { get; set; } = "✅";
        public string Label { get; set; } = "Recommended Action";
        public string BorderColor { get; set; } = "var(--grn)";
        public string Background { get; set; } = "var(--grnl)";
        public string Title { get; set; } = "";
        public string Detail { get; set; } = "";
    }

    /// <summary>AI Insights view (renderAIInsights).</summary>
    public sealed class AiInsightsDto
    {
        /// <summary>"disabled", "noRun", "ok" (cards), "fallback" (raw text shown) or "error".</summary>
        public string State { get; set; } = "ok";
        /// <summary>Toast the browser showed (null = none).</summary>
        public string? Toast { get; set; }
        public string Focus { get; set; } = "risk";
        public int TotalLines { get; set; }
        public int FlaggedCount { get; set; }
        public double TotalExcess { get; set; }
        public List<AiKpiDto> Kpis { get; set; } = new();
        /// <summary>"⏳ Looking for cross-cutting patterns across N flagged lines…" (while the portal waits).</summary>
        public string? LoadingText { get; set; }
        public List<AiFindingCardDto> Findings { get; set; } = new();
        public AiCalloutDto? Action { get; set; }
        /// <summary>"Lens: … · based on N flagged of M total lines · generated just now, not saved".</summary>
        public string Meta { get; set; } = "";
        /// <summary>Raw text / error shown in the fallback card (pre-wrap).</summary>
        public string? FallbackText { get; set; }
        public string? Provider { get; set; }
    }
}
