namespace Application.DTOs.Analytics.Grc
{
    /// <summary>
    /// Season Selection (Data Setup) — port of index.html <c>_allSeasonsSorted</c> / <c>renderSeasonPills</c>
    /// (lines 2701-2740): every distinct Season in Article Master, most recent first, and the effective
    /// Current / Fallback selection after the defaulting rules.
    /// </summary>
    public sealed class SeasonSelectionDto
    {
        /// <summary>Normalised season strings ("SS26", "AW2026" …), most recent first.</summary>
        public List<string> Seasons { get; set; } = new();
        public List<string> SeasonCurrent { get; set; } = new();
        public List<string> SeasonFallback { get; set; } = new();
        /// <summary>"No Season column found in Article Master yet." when there is no parseable season (both pill rows show it).</summary>
        public string? Message { get; set; }
        /// <summary>True when the effective selection differs from the saved parameters — the browser saved it back to S.params.</summary>
        public bool Changed { get; set; }
    }

    public sealed class SeasonToggleRequest
    {
        /// <summary>"current" or "fallback".</summary>
        public string Group { get; set; } = "current";
        public string Season { get; set; } = "";
    }
}
