using System.Globalization;
using DecisionEngine.Common;
using DecisionEngine.Model;

namespace DecisionEngine.Sap
{
    /// <summary>
    /// Turns the identifiers SAP sends (spec v1.4 §3: article numbers in SAP internal format with leading zeros,
    /// the colour characteristic value, dates YYYY-MM-DD) into the keys of the uploaded tool data:
    /// <list type="bullet">
    /// <item><b>GM</b> = <c>generic_article</c> (else <c>article</c> for a single article): exact, then without leading zeros —
    /// checked against the same lookups <see cref="ResolveIndex"/> resolves and routes by (Article Master / Universe Master /
    /// F2 resolve map, Universe Master set, BSR GM set).</item>
    /// <item><b>Colour</b>: Colour Master code (case-insensitive) → the code; else Colour Master description → its code; else a
    /// colour the data already holds for this GM (F1 / F2 / F3 / BSR, case-insensitive) in the data's spelling; else the
    /// value trimmed and upper-cased (what the New Request form does with a typed colour, <c>formGetColour</c>).</item>
    /// <item><b>Delivery month</b> = <c>delivery_date</c> as MM.YYYY; a date before the current month counts as the current month.</item>
    /// </list>
    /// </summary>
    public static class SapInputNormalizer
    {
        private const string ColourMasterKey = "sap.colourMaster";
        private const string GmColoursKey = "sap.gmColours";

        /// <summary>"000000000100234500" → "100234500"; all zeros → "0".</summary>
        public static string StripLeadingZeros(string s)
        {
            var t = (s ?? "").Trim();
            var r = t.TrimStart('0');
            return r.Length == 0 && t.Length > 0 ? "0" : r;
        }

        /// <summary>The tool's GM for an item: (gm, how) with how = "exact" / "no-leading-zeros" / "article" / "article-no-leading-zeros"; (null, "unknown") when none is known.</summary>
        public static (string? Gm, string How) ResolveGm(EngineData data, string? genericArticle, string? article)
        {
            foreach (var (value, label) in new[] { (genericArticle, ""), (article, "article") })
            {
                if (string.IsNullOrWhiteSpace(value)) continue;
                var exact = value.Trim();
                if (ResolveIndex.IsKnownGm(data, exact)) return (exact, label.Length == 0 ? "exact" : label);
                var stripped = StripLeadingZeros(exact);
                if (stripped != exact && ResolveIndex.IsKnownGm(data, stripped))
                    return (stripped, label.Length == 0 ? "no-leading-zeros" : label + "-no-leading-zeros");
            }
            return (null, "unknown");
        }

        /// <summary>The tool's colour key: (colour, how) with how = "master-code" / "master-desc" / "data" / "as-sent"; (null, "missing") when no colour was sent.</summary>
        public static (string? Colour, string How) ResolveColour(EngineData data, string? gm, string? colour)
        {
            var raw = (colour ?? "").Trim();
            if (raw.Length == 0) return (null, "missing");
            var master = ColourMaster(data);
            var gmCols = gm == null ? null : GmColours(data).GetValueOrDefault(gm.Trim());
            string InDataSpelling(string c) =>
                gmCols?.FirstOrDefault(x => string.Equals(x, c, StringComparison.OrdinalIgnoreCase)) ?? c;

            if (master.Codes.TryGetValue(raw, out var code)) return (InDataSpelling(code), "master-code");
            if (master.Descs.TryGetValue(raw, out var byDesc)) return (InDataSpelling(byDesc), "master-desc");
            var inData = gmCols?.FirstOrDefault(x => string.Equals(x, raw, StringComparison.OrdinalIgnoreCase));
            if (inData != null) return (inData, "data");
            return (raw.ToUpperInvariant(), "as-sent");
        }

        /// <summary>
        /// "2026-10-20" → "10.2026". A month before <paramref name="today"/>'s month → today's month (an overdue delivery is
        /// expected now). Null when the date is missing or not YYYY-MM-DD.
        /// </summary>
        public static string? DeliveryMonth(string? deliveryDate, string today)
        {
            if (!TryParseDate(deliveryDate, out var d)) return null;
            var (ty, tm, _) = MonthMath.ParseIsoDate(today);
            var (y, m) = d.Year * 12 + d.Month < ty * 12 + tm ? (ty, tm) : (d.Year, d.Month);
            return MonthMath.Mmyyyy(y, m);
        }

        /// <summary>Strict YYYY-MM-DD (spec v1.4 §3).</summary>
        public static bool TryParseDate(string? s, out DateOnly date) =>
            DateOnly.TryParseExact((s ?? "").Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out date);

        private sealed class ColourMasterIndex
        {
            public Dictionary<string, string> Codes { get; } = new(StringComparer.OrdinalIgnoreCase);
            public Dictionary<string, string> Descs { get; } = new(StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>Colour Master rows the way <c>formPopulateColours</c> reads them ("Colour" / "Colour Desc").</summary>
        private static ColourMasterIndex ColourMaster(EngineData data) => data.GetOrBuild(ColourMasterKey, d =>
        {
            var ix = new ColourMasterIndex();
            if (d.ColMaster == null) return ix;
            foreach (var r in d.ColMaster)
            {
                var c = r.Col("Colour", "Color", "colour", "color").Trim();
                if (c.Length == 0) continue;
                ix.Codes.TryAdd(c, c);
                var desc = r.Col("Colour Desc", "Color Desc", "Description", "Desc").Trim();
                if (desc.Length > 0) ix.Descs.TryAdd(desc, c);
            }
            return ix;
        });

        /// <summary>GM → colours present in F1 / F2 / F3 / BSR (trimmed, as written).</summary>
        private static Dictionary<string, List<string>> GmColours(EngineData data) => data.GetOrBuild(GmColoursKey, d =>
        {
            var map = new Dictionary<string, List<string>>(StringComparer.Ordinal);
            foreach (var src in new[] { d.F1, d.F2, d.F3, d.Bsr })
            {
                if (src == null) continue;
                foreach (var r in src)
                {
                    var gm = r.Col("Generic Material", "generic material", "GENERIC MATERIAL").Trim();
                    var c = r.Col("Color", "Colour", "COLOR", "COLOUR", "colour", "color").Trim();
                    if (gm.Length == 0 || c.Length == 0) continue;
                    if (!map.TryGetValue(gm, out var list)) map[gm] = list = new List<string>();
                    if (!list.Contains(c)) list.Add(c);
                }
            }
            return map;
        });
    }
}
