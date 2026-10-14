namespace Application.DTOs.Analytics.Common
{
    /// <summary>One KPI tile of a dashboard strip (value already formatted, colours as in v1.1.4).</summary>
    public sealed class KpiTileDto
    {
        public string Label { get; set; } = "";
        public string Value { get; set; } = "";
        /// <summary>Text / border colour (border is this colour at 20% alpha: <c>{Color}33</c>).</summary>
        public string Color { get; set; } = "";
        public string Background { get; set; } = "";
    }

    /// <summary>One arc + legend line of the "📊 Decision Mix" donut (SVG 42×42, r = 15.9155, rotated −90°).</summary>
    public sealed class DonutSegmentDto
    {
        public string Decision { get; set; } = "";
        public int Count { get; set; }
        /// <summary>Share of the filtered total in % (unrounded).</summary>
        public double Pct { get; set; }
        /// <summary>Legend percentage: <c>pct.toFixed(0)</c>.</summary>
        public string PctText { get; set; } = "";
        public string Color { get; set; } = "";
        public string? Background { get; set; }
        public string? Border { get; set; }
        /// <summary><c>stroke-dasharray</c> = "{DashArray} {DashGap}", <c>stroke-dashoffset</c> = DashOffset (toFixed(2) strings).</summary>
        public string DashArray { get; set; } = "";
        public string DashGap { get; set; } = "";
        public string DashOffset { get; set; } = "";
    }

    public sealed class DecisionMixDto
    {
        /// <summary>"📊 Decision Mix" or "📊 Decision Mix (filtered)".</summary>
        public string Title { get; set; } = "";
        public List<DonutSegmentDto> Segments { get; set; } = new();
    }

    /// <summary>A "What's driving …" bar: label, count and % of the flagged set (bar width = Pct%).</summary>
    public sealed class DriverRowDto
    {
        public string Label { get; set; } = "";
        public int Count { get; set; }
        public double Pct { get; set; }
    }

    public sealed class DriverBreakdownDto
    {
        public string Title { get; set; } = "";
        /// <summary>Shown instead of the bars when nothing is flagged.</summary>
        public string? EmptyMessage { get; set; }
        public List<DriverRowDto> Rows { get; set; } = new();
    }

    /// <summary>One "💡 Recommendations" line: <see cref="Html"/> keeps the original &lt;strong&gt; markup, <see cref="Text"/> is plain.</summary>
    public sealed class RecommendationLineDto
    {
        public string Html { get; set; } = "";
        public string Text { get; set; } = "";
    }

    public sealed class RecommendationsCardDto
    {
        public string Title { get; set; } = "";
        public List<RecommendationLineDto> Items { get; set; } = new();
    }

    /// <summary>A select option (value "" = the "All …" entry).</summary>
    public sealed class OptionDto
    {
        public OptionDto() { }
        public OptionDto(string value, string label) { Value = value; Label = label; }
        public string Value { get; set; } = "";
        public string Label { get; set; } = "";
    }
}
