using System.Text.RegularExpressions;
using Application.DTOs.Analytics.Common;
using Application.Services.V1.Engines.Common;
using Domain.Entities.Common;

namespace Application.Services.V1.Analytics.Common
{
    /// <summary>Pieces shared by the Category / Planner dashboards and the AI context builders.</summary>
    public static class DashboardMath
    {
        private const RegexOptions I = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;
        private static readonly (Regex Re, string Label)[] T1Buckets =
        {
            (new Regex("Absolute Block", I), "Absolute Block — PDSTR < 1%/day, ≥4wk on floor"),
            (new Regex("Colour Mix", I), "Colour Mix% below threshold (broken/cut sizes)"),
            (new Regex("Max Permissible Qty = 0", I), "Stock cover already sufficient — 0 headroom"),
            (new Regex("STR exceeds", I), "Stock cover breach — STR > threshold"),
            (new Regex("BOBs fallthrough", I), "BOBs — PDSTR gap vs peer siblings"),
            (new Regex("True Repeat", I), "True Repeat — PDSTR gap vs peer group"),
            (new Regex("WOF.*gate|Insufficient selling history", I), "Insufficient selling history (WOF gate)"),
            (new Regex("no peer pool|No peer pool", I), "No peer pool available for comparison"),
            (new Regex("Not found in Universe Master|New article|untracked parent", I), "New / untracked article"),
            (new Regex("Safe to raise|within limit", I), "Within safe stock cover")
        };
        private static readonly (Regex Re, string Label)[] T2Buckets =
        {
            (new Regex("Absolute Block", I), "Absolute Block — confirmed dead stock"),
            (new Regex("Colour Mix", I), "Colour Mix% below threshold"),
            (new Regex("Max Permissible Qty = 0", I), "No headroom — stock already covers threshold"),
            (new Regex("STR exceeds|exceeds T=", I), "Stock cover breach — STR exceeds threshold"),
            (new Regex("BOBs fallthrough", I), "BOBs — PDSTR gap vs peer siblings"),
            (new Regex("True Repeat", I), "True Repeat — PDSTR gap vs peer group"),
            (new Regex("WOF.*gate|Insufficient selling history", I), "Insufficient selling history (WOF gate)"),
            (new Regex("no peer pool|No peer pool", I), "No peer pool available"),
            (new Regex("Not found in Universe Master", I), "Data/master mismatch")
        };

        /// <summary>Port of <c>classifyReasonBucket(r)</c> (Tool 1 request → driver label).</summary>
        public static string ClassifyReasonBucket(System.Text.Json.Nodes.JsonObject r)
        {
            var txt = AnalyticsJs.OrStr(r, "decisionReason", "");
            foreach (var (re, label) in T1Buckets) if (re.IsMatch(txt)) return label;
            return "Other";
        }

        /// <summary>Port of <c>_aiDriverBucketT2(reason)</c> (Tool 2 line → driver label).</summary>
        public static string DriverBucketT2(object? reason)
        {
            var txt = Js.Truthy(reason) ? AnalyticsJs.Str(reason) : "";
            foreach (var (re, label) in T2Buckets) if (re.IsMatch(txt)) return label;
            return "Within safe stock cover";
        }

        /// <summary><c>DECISION_COLORS</c> / <c>_decColor(d)</c>: text, background, border.</summary>
        public static (string C, string Bg, string Bd) DecColor(string? d) => d switch
        {
            "Auto Allow" or "Allow" => ("#00873E", "#DCFCE7", "#86EFAC"),
            "Watch List" => ("#B45309", "#FFF3CD", "#FDE68A"),
            "Recommend Reduced Qty" => ("#D97706", "#FFFBEB", "#FDE68A"),
            "Block" or "Hold — Don't Raise" => ("#DC2626", "#FEF2F2", "#FECACA"),
            _ => ("#6B7280", "#F5F5F5", "#D1D5DB")
        };

        /// <summary><c>dpill(d)</c> class.</summary>
        public static string DecisionPill(string? d) => d switch
        {
            "Auto Allow" or "Allow" => "dp-allow",
            "Recommend Reduced Qty" => "dp-reduce",
            "Watch List" => "dp-watch",
            "Hold — Don't Raise" => "dp-hold",
            "Block" => "dp-block",
            _ => "dp-nodata"
        };

        /// <summary>The donut arcs + legend of "📊 Decision Mix" for the decisions present, in <paramref name="order"/>.</summary>
        public static List<DonutSegmentDto> Donut(IEnumerable<string> order, Func<string, int> count, int total, Func<string, (string C, string? Bg, string? Bd)> color)
        {
            const double R = 15.9155;
            var circ = 2 * Math.PI * R;
            double cumPct = 0;
            var list = new List<DonutSegmentDto>();
            foreach (var d in order)
            {
                var n = count(d);
                if (n == 0) continue;
                var pct = (double)n / total * 100;
                var dash = AnalyticsJs.ToFixed(pct / 100 * circ, 2);
                var gap = AnalyticsJs.ToFixed(circ - Js.Number(dash), 2);
                var offset = AnalyticsJs.ToFixed(-cumPct / 100 * circ, 2);
                cumPct += pct;
                var col = color(d);
                list.Add(new DonutSegmentDto
                {
                    Decision = d, Count = n, Pct = pct, PctText = AnalyticsJs.ToFixed((double)n / total * 100, 0),
                    Color = col.C, Background = col.Bg, Border = col.Bd, DashArray = dash, DashGap = gap, DashOffset = offset
                });
            }
            return list;
        }

        /// <summary><c>Object.entries(counts).sort((a,b)=>b[1]-a[1])</c> → driver rows with % of <paramref name="flagged"/>.</summary>
        public static List<DriverRowDto> DriverRows(JsObjectMap<int> counts, int flagged)
        {
            var rows = counts.Select(kv => new DriverRowDto { Label = kv.Key, Count = kv.Value }).ToList();
            JsSort.StableSort(rows, (a, b) => b.Count - a.Count);
            foreach (var r in rows) r.Pct = Js.Round((double)r.Count / flagged * 100);
            return rows;
        }

        public static string RiskColor(double risk) => risk >= 50 ? "#DC2626" : risk >= 20 ? "#D97706" : "#00873E";

        private static readonly Regex Tag = new("<[^>]+>", RegexOptions.Compiled);
        public static RecommendationLineDto Rec(string html) => new() { Html = html, Text = Tag.Replace(html, "") };

        /// <summary>
        /// <c>_populateSelectPreserving</c>: the selected value survives a repopulation only when it is still an
        /// option (or ""); otherwise the select falls back to "All".
        /// </summary>
        public static string Preserve(string? current, IReadOnlyList<string> values)
        {
            var c = current ?? "";
            return c.Length == 0 || values.Contains(c) ? c : "";
        }

        /// <summary>A static select: a value that is not one of its options cannot be selected (→ "").</summary>
        public static string StaticSelect(string? current, IEnumerable<OptionDto> options)
        {
            var c = current ?? "";
            return options.Any(o => o.Value == c) ? c : "";
        }
    }
}
