using System.Text.RegularExpressions;
using DecisionEngine.Model;

namespace DecisionEngine.Common
{
    /// <summary>
    /// The Entry / Mid / Fashion (EMF) mapping of the multi concept rule: Brand × MC × Flag × MRP → Entry, Mid or Fashion.
    /// Uploaded on Data Setup (dataset <see cref="DatasetKeys.Emf"/>); each upload replaces the previous one. Rows are stored
    /// with the columns below and the tag already normalised (<see cref="NormalizeTag"/>).
    /// <para>Multi concept rule Step 2: a Q1 option is allowed only when its per-day sell-thru (first 3 weeks, the BSR
    /// "AVG PERDAY SELL THRU%") is at least the minimum of its type (<see cref="MinPdstr"/>: Entry 2.0 %, Mid 1.5 %,
    /// Fashion 1.5 % per day); a Brand × MC × Flag × MRP that is not in the mapping is a Hold with
    /// <see cref="NotMaintainedMessage"/>.</para>
    /// </summary>
    public static class EmfMapping
    {
        public const string Entry = "Entry";
        public const string Mid = "Mid";
        public const string Fashion = "Fashion";

        /// <summary>The Hold message when the option's Brand × MC × Flag × MRP is not in the mapping (business wording).</summary>
        public const string NotMaintainedMessage = "Planner has not maintained this MC-MRP in the master.";

        /// <summary><see cref="Tool1.Tool1EngineA"/> result of the minimum check: met, below the minimum, not in the mapping.</summary>
        public const string CheckOk = "ok", CheckBelow = "below", CheckNotMapped = "notMapped";

        /// <summary>The minimum per-day sell-thru (fraction: 0.02 = 2.0 %/day) of a tag; null for anything else.</summary>
        public static double? MinPdstr(string? tag) => tag switch { Entry => 0.02, Mid => 0.015, Fashion => 0.015, _ => null };

        /// <summary>
        /// Whether a per-day sell-thru meets a minimum, compared in basis points (0.01 %) so a file value of "1.50%" equals
        /// the 1.5 % minimum exactly ("at least" = equal is fine).
        /// </summary>
        public static bool MeetsMinimum(double pdstr, double min) => Js.Round(pdstr * 10000) >= Js.Round(min * 10000);

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
        /// MC and MRP trimmed without a trailing ".0" ("399.0" = "399"). The flag "NON-BOGO" (the tool's data files) is the
        /// mapping's "Normal" (<see cref="FlagKey"/>).
        /// </summary>
        public static string Key(string? brand, string? mc, string? flag, string? mrp) =>
            string.Join("|", (brand ?? "").Trim().ToUpperInvariant(), StripDot0(mc), FlagKey(flag), StripDot0(mrp));

        /// <summary>The flag part of <see cref="Key"/>: upper-case, trimmed; NON-BOGO / NON BOGO / NONBOGO → NORMAL.</summary>
        public static string FlagKey(string? flag)
        {
            var f = (flag ?? "").Trim().ToUpperInvariant();
            return f is "NON-BOGO" or "NON BOGO" or "NONBOGO" or "NON_BOGO" ? "NORMAL" : f;
        }

        /// <summary>"NON-PL · MC 2203 · Flag BOGO · MRP 599" — how a key is named in reasons.</summary>
        public static string Describe(string? brand, string? mc, string? flag, string? mrp) =>
            (brand ?? "").Trim() + " · MC " + StripDot0(mc) + " · Flag " + ((flag ?? "").Trim().Length > 0 ? (flag ?? "").Trim() : "(blank)") + " · MRP " + StripDot0(mrp);

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
