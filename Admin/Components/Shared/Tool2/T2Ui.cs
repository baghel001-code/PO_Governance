using Application.DTOs.Recommendation;
using Application.DTOs.Tool2;
using Domain.Entities.Common;

namespace Admin.Components.Shared.Tool2
{
    /// <summary>
    /// Presentation-only helpers of the Tool 2 screen: the fixed colour maps the v1.1.4 markup used
    /// (expander frames, quartile chips, rollup chips) and number display. No business rules — every
    /// figure and text comes from the API (Tool2ViewDto / Tool2LineDto).
    /// </summary>
    public static class T2Ui
    {
        /// <summary>Frame colours of a line's "Detailed Computation" expander (renderBrandSummary dCol / dBg).</summary>
        public static (string Color, string Bg) ReasonColors(string decision) => decision switch
        {
            "Cancel" => ("#DC2626", "#FEF2F2"),
            "Reduce Qty" => ("#D97706", "#FFFBEB"),
            "Watch List" => ("#92400E", "#FFF7ED"),
            _ => ("#00873E", "#DCFCE7"),
        };

        /// <summary>Quartile chip colour of the Multi Concept table.</summary>
        public static string QuartileColor(string? q) => q switch
        {
            "Q1" => "#00873E",
            "Q2" => "#059669",
            "Q3" => "#D97706",
            _ => "#DC2626",
        };

        /// <summary>fmt(): Math.round + en-IN grouping.</summary>
        public static string Fmt(double v) => Js.Fmt(v);

        /// <summary>The " (filtered)" etc. come from the server; this only builds the Multi Concept percentile cell.</summary>
        public static string Pct(double? p) => p == null ? "—" : Js.NumberToString(p.Value) + "th";

        public static string MrpText(string? mrp) => "₹" + (mrp ?? "");

        /// <summary>The recommendation modal for a line (GM + its bucket and engine).</summary>
        public static RecommendationRequestDto RecRequest(Tool2LineDto l) => new()
        {
            Gm = l.Gm,
            Brand = l.Info?.Brand,
            McCd = string.IsNullOrEmpty(l.McCd) ? null : l.McCd,
            Mrp = string.IsNullOrEmpty(l.Mrp) ? null : l.Mrp,
            Flag = string.IsNullOrEmpty(l.Flag) ? null : l.Flag,
            Div = l.Div,
            McDesc = l.McDesc,
            Engine = l.Engine is "A" or "B" ? l.Engine : null,
        };

        /// <summary>The recommendation modal for a Single Concept colour group (first GM of the group).</summary>
        public static RecommendationRequestDto RecRequest(Tool2ColourGroupDto g) => new()
        {
            Gm = g.Gm,
            Brand = g.Brand == "NON-PL" ? null : g.Brand,
            McCd = string.IsNullOrEmpty(g.McCd) ? null : g.McCd,
            Mrp = string.IsNullOrEmpty(g.Mrp) ? null : g.Mrp,
            Flag = string.IsNullOrEmpty(g.Flag) ? null : g.Flag,
            Div = g.Div,
            McDesc = g.McDesc,
            Engine = "B",
        };
    }
}
