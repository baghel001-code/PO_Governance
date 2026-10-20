using System.Text.Json;
using System.Text.Json.Serialization;

namespace DecisionEngine.Model
{
    /// <summary>
    /// Planner-editable parameters (browser <c>S.params</c>, stored as the "params" document).
    /// Defaults are the v1.1.4 defaults. Unknown keys are kept in <see cref="Extra"/> so a round-trip
    /// through the API never loses anything the planner saved.
    /// </summary>
    public sealed class EngineParams
    {
        /// <summary>Single concept target days of cover (T).</summary>
        [JsonPropertyName("T1")] public double T1 { get; set; } = 75;
        /// <summary>Tool 2 target days of cover.</summary>
        [JsonPropertyName("T2")] public double T2 { get; set; } = 100;
        /// <summary>Delivery horizon / days used by Tool 2 (D).</summary>
        [JsonPropertyName("D")] public double D { get; set; } = 75;
        /// <summary>Colour mix gate in % (below → Watch List).</summary>
        [JsonPropertyName("mix")] public double Mix { get; set; } = 2;
        [JsonPropertyName("N")] public double N { get; set; } = 2;
        /// <summary>Weeks of floor (WOF) minimum, in weeks.</summary>
        [JsonPropertyName("wof")] public double Wof { get; set; } = 4;
        /// <summary>Minimum peer pool size before fallback.</summary>
        [JsonPropertyName("peer")] public double Peer { get; set; } = 5;
        [JsonPropertyName("mixWindowDays")] public double MixWindowDays { get; set; } = 45;
        /// <summary>Border zone tolerance in %.</summary>
        [JsonPropertyName("borderTolerance")] public double BorderTolerance { get; set; } = 10;
        /// <summary>STR buffer in % (effective T = T × (1 + buffer/100)).</summary>
        [JsonPropertyName("strBuffer")] public double StrBuffer { get; set; } = 5;
        [JsonPropertyName("prevMonthPendingCutoffDay")] public double PrevMonthPendingCutoffDay { get; set; } = 5;
        [JsonPropertyName("seasonCurrent")] public List<string> SeasonCurrent { get; set; } = new();
        [JsonPropertyName("seasonFallback")] public List<string> SeasonFallback { get; set; } = new();
        /// <summary>GRC back-testing "as of" period ("WW.YYYY" or "MM.YYYY").</summary>
        [JsonPropertyName("grcAsOfPeriod")] public string? GrcAsOfPeriod { get; set; }

        [JsonExtensionData] public Dictionary<string, JsonElement>? Extra { get; set; }

        public EngineParams Clone() =>
            JsonSerializer.Deserialize<EngineParams>(JsonSerializer.Serialize(this))!;
    }
}
