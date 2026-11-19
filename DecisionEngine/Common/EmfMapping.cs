using System.Text.RegularExpressions;
using DecisionEngine.Model;

namespace DecisionEngine.Common
{
    /// <summary>
    /// The Entry / Mid / Fashion (EMF) mapping of the multi concept rule: Brand × MC × Flag × MRP → Entry, Mid or Fashion.
    /// Uploaded on Data Setup (dataset <see cref="DatasetKeys.Emf"/>); each upload replaces the previous one. Rows are stored
    /// with the columns below and the tag already normalised (<see cref="NormalizeTag"/>).
    /// <para>Step 1 of the multi concept rule only loads and checks the mapping; Step 2 will use it for the minimum
    /// sell-thru per type (Entry 2.0 %, Mid 1.5 %, Fashion 1.5 % per day).</para>
    /// </summary>
    public static class EmfMapping
    {
        public const string Entry = "Entry";
        public const string Mid = "Mid";
        public const string Fashion = "Fashion";

        /// <summary>The only tags a row may carry.</summary>
        public static readonly IReadOnlyList<string> Tags = new[] { Entry, Mid, Fashion };

        // stored columns (the upload writes exactly these)
        public const string ColBrand = "Brand Type";
        public const string ColDivision = "Division";
        public const string ColMc = "MC CD";
        public const string ColMcDesc = "MC DESC";
        public const string ColFlag = "Flag";
        public const string ColMrp = "MRP";
        public const string ColTag = "EMF";

        private static readonly Regex Prefix = new(@"^\s*\d+\s*[.)\-:]?\s*", RegexOptions.CultureInvariant);

        /// <summary>
        /// "Entry", "ENTRY", "1.ENTRY ", "2. Mid", "3-fashion" → Entry / Mid / Fashion (a leading sort number and spaces are
        /// ignored, case-insensitive); anything else, blank included → null (the row is rejected).
        /// </summary>
        public static string? NormalizeTag(string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return null;
            var t = Prefix.Replace(raw.Trim(), "").Trim();
            foreach (var tag in Tags)
                if (string.Equals(t, tag, StringComparison.OrdinalIgnoreCase)) return tag;
            return null;
        }

        /// <summary>
        /// The lookup key of a Brand × MC × Flag × MRP: brand and flag trimmed, case-insensitive ("Normal" = "NORMAL");
        /// MC and MRP trimmed without a trailing ".0" ("399.0" = "399").
        /// </summary>
        public static string Key(string? brand, string? mc, string? flag, string? mrp) =>
            string.Join("|", (brand ?? "").Trim().ToUpperInvariant(), StripDot0(mc), (flag ?? "").Trim().ToUpperInvariant(), StripDot0(mrp));

        /// <summary>The tag of a Brand × MC × Flag × MRP in the uploaded mapping; null when it is not mapped (or no mapping is loaded).</summary>
        public static string? Lookup(EngineData data, string? brand, string? mc, string? flag, string? mrp)
        {
            if (data.Emf is not { Count: > 0 }) return null;
            var map = data.GetOrBuild("common.emfMap", d =>
            {
                var m = new Dictionary<string, string>(StringComparer.Ordinal);
                foreach (var r in d.Emf!)
                {
                    var tag = NormalizeTag(r.Col(ColTag));
                    if (tag != null) m.TryAdd(Key(r.Col(ColBrand), r.Col(ColMc), r.Col(ColFlag), r.Col(ColMrp)), tag);
                }
                return m;
            });
            return map.TryGetValue(Key(brand, mc, flag, mrp), out var t) ? t : null;
        }

        private static string StripDot0(string? v)
        {
            var s = (v ?? "").Trim();
            return s.EndsWith(".0", StringComparison.Ordinal) ? s[..^2] : s;
        }
    }
}
