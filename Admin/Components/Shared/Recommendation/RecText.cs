using System.Globalization;
using System.Text.RegularExpressions;
using Domain.Entities.Common;
using Microsoft.AspNetCore.Components;

namespace Admin.Components.Shared.Recommendation
{
    /// <summary>
    /// Display helpers shared by the Recommendation modal and the GRC screen. The server sends plain
    /// texts; v1.1.4 drew some parts of them bold — these helpers re-apply that emphasis only (no figure
    /// is recomputed here).
    /// </summary>
    public static class RecText
    {
        /// <summary>index.html <c>_fmt(n)</c>: Math.round + en-IN grouping.</summary>
        public static string Fmt(double v) => Js.Fmt(v);
        public static string Fmt(double? v) => v.HasValue ? Js.Fmt(v.Value) : Js.Fmt(double.NaN);
        /// <summary>JS <c>String(number)</c>.</summary>
        public static string Num(double v) => Js.NumberToString(v);
        public static string Num(double? v) => v.HasValue ? Js.NumberToString(v.Value) : "";
        /// <summary>Invariant number for SVG coordinates.</summary>
        public static string Svg(double v) => v.ToString("0.###", CultureInfo.InvariantCulture);

        /// <summary>Minimal HTML encoding (keeps ×, ₹, — … as characters so the patterns below match).</summary>
        public static string Encode(string? s) =>
            (s ?? "").Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;");

        /// <summary>Encodes <paramref name="text"/> and wraps every match of the patterns in &lt;strong&gt;.</summary>
        public static MarkupString Bold(string? text, params string[] patterns)
        {
            var html = Encode(text);
            foreach (var p in patterns)
                html = Regex.Replace(html, p, m => "<strong>" + m.Value + "</strong>");
            return new MarkupString(html.Replace("\n", "<br>"));
        }

        // ── Texts of _paintRec with their bold parts ──
        /// <summary>"<b>{n} colours</b> have headroom … Total recommended units: <b>{fmt}</b>. …" (Single Concept) or
        /// "<b>{n} Colour×GM combinations</b> recommended. P1 (Q1): <b>{p1}</b> · P2 (Q2): <b>{p2}</b>. Total units: <b>{fmt}</b>. …".</summary>
        public static MarkupString Summary(string? s) => Bold(s,
            @"^\d+ colours", @"(?<=Total recommended units: )[-\d,]+", @"^\d+ Colour×GM combinations",
            @"(?<=P1 \(Q1\): )\d+", @"(?<=P2 \(Q2\): )\d+", @"(?<=Total units: )[-\d,]+");

        /// <summary>Colour-trace header: colour code bold.</summary>
        public static MarkupString TraceHeader(string? s) => Bold(s, @"^\S+(?= — )");

        /// <summary>Qty pop-up header: GM and colour bold.</summary>
        public static MarkupString QtyHeader(string? s) => Bold(s, @"^\S+(?= recommended for )", @"(?<= recommended for )\S+");

        /// <summary>GRC Article Trace "Reasoning": the figures after each label are bold (renderArticleTrace).</summary>
        public static MarkupString GrcReasoning(string? s) => Bold(s, @"(?<=(?:Back-calc WOF|PDSTR|WOF|Peer pool): )[^\s)]+");

        /// <summary>GRC month-mapping note paragraph: bold lead-in and key phrases as in view-analytics.</summary>
        public static MarkupString GrcMappingNote(string? s)
        {
            var html = Encode(s);
            html = Regex.Replace(html, @"^.*?\):", m => "<b>" + m.Value + "</b>");
            html = html.Replace("majority of its 7 days", "<b>majority of its 7 days</b>")
                       .Replace("the entire week", "the <i>entire</i> week")
                       .Replace("counted under April", "counted under <b>April</b>");
            return new MarkupString(html);
        }
    }
}
