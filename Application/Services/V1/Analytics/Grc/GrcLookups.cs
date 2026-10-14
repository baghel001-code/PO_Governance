using Domain.Entities.Common;
using Domain.Entities.Engine;

namespace Application.Services.V1.Analytics.Grc
{
    /// <summary>One entry of the GM resolve map (index.html <c>_buildResolveIndex</c>, line 1716).</summary>
    public sealed class GrcResolveInfo
    {
        public string Brand { get; init; } = "";
        public string Div { get; init; } = "";
        public string McCd { get; init; } = "";
        public string McDesc { get; init; } = "";
        public string Mrp { get; init; } = "";
        public string Flag { get; init; } = "";
        public string Season { get; init; } = "";
        public string ArtDesc { get; init; } = "";
        public string VendorName { get; init; } = "";
    }

    /// <summary>
    /// Private copies of the shared lookup builders the GRC back-test calls (built once per data
    /// snapshot through <see cref="EngineData.GetOrBuild{T}"/> under "grc|…" names).
    /// </summary>
    public static class GrcLookups
    {
        /// <summary>
        /// Port of <c>_buildResolveIndex</c>: GM → brand / division / MC / MRP / flag / season … from
        /// Article Master &gt; Universe Master &gt; F2 (higher priority overwrites).
        /// </summary>
        public static Dictionary<string, GrcResolveInfo> ResolveMap(EngineData data) =>
            data.GetOrBuild("grc|resolveMap", d =>
            {
                var map = new Dictionary<string, GrcResolveInfo>(StringComparer.Ordinal);
                foreach (var src in new[] { d.F2, d.Um, d.ArtMaster }) // reversed priority: artmaster wins
                {
                    if (src == null) continue;
                    foreach (var r in src)
                    {
                        var k = r.Col("Generic Material", "generic material", "GENERIC MATERIAL", "GenMat").Trim();
                        if (k.Length == 0) continue;
                        map[k] = new GrcResolveInfo
                        {
                            Brand = r.Col("Brand Type", "BRAND TYPE", "brand type"),
                            Div = r.Col("Division", "DIV", "division", "div"),
                            McCd = r.Col("MC CD", "MC_CD", "mc cd", "MC Code"),
                            McDesc = r.Col("MC DES", "MC DESC", "MC Desc", "mc desc", "MC Description", "MC Name"),
                            Mrp = r.Col("MRP", "mrp", "MRP value", "MRP Value"),
                            Flag = r.Col("Flag (BOGO/NON-BOGO)", "BOGO/NON-BOGO", "Flag", "flag", "Article Type", "Bogo/Non-Bogo"),
                            Season = r.Col("Season", "SEASON", "season"),
                            ArtDesc = r.Col("ART DESC", "Art Desc", "art desc", "Article Description"),
                            VendorName = r.Col("VENDOR NAME", "Vendor Name", "vendor name"),
                        };
                    }
                }
                return map;
            });

        /// <summary>Port of <c>_buildUMIndex</c>: GMs listed in Universe Master (Single Concept MRP).</summary>
        public static HashSet<string> UmSet(EngineData data) =>
            data.GetOrBuild("grc|umSet", d =>
            {
                var set = new HashSet<string>(StringComparer.Ordinal);
                if (d.Um != null)
                    foreach (var r in d.Um)
                    {
                        var k = r.Col("Generic Material", "generic material", "GENERIC MATERIAL").Trim();
                        if (k.Length > 0) set.Add(k);
                    }
                return set;
            });

        /// <summary>
        /// Port of <c>_buildCatHeadMap</c> (line 7214): Brand × MC CD × MRP → category head e-mail
        /// (first mapping wins; rows without an e-mail are skipped).
        /// </summary>
        public static Dictionary<string, string> CatHeadMap(EngineData data) =>
            data.GetOrBuild("grc|catHeadMap", d =>
            {
                var map = new Dictionary<string, string>(StringComparer.Ordinal);
                var rows = d.CatHeadMap;
                if (rows == null) return map;
                foreach (var r in rows)
                {
                    var brand = GrcJs.Or(r, "Brand Type", "Brand");
                    var mcCd = GrcJs.Or(r, "MC CD", "MC_CD", "MC Cd", "MC");
                    var mrp = GrcJs.Or(r, "MRP");
                    var email = Js.ToJsString(GrcJs.Or(r, "Category_Email_ID", "Category Email ID", "Category_Email", "Level 3")).Trim();
                    if (email.Length == 0) continue;
                    var key = Js.N(brand) + "|" + Js.N(mcCd) + "|" + Js.N(mrp);
                    map.TryAdd(key, email);
                }
                return map;
            });

        /// <summary>Port of <c>_catHeadEmailFor</c>.</summary>
        public static string? CatHeadEmailFor(Dictionary<string, string> map, object? brand, object? mcCd, object? mrp)
        {
            if (map.Count == 0) return null;
            var key = Js.N(brand) + "|" + Js.N(mcCd) + "|" + Js.N(mrp);
            return map.TryGetValue(key, out var e) && e.Length > 0 ? e : null;
        }

        /// <summary>
        /// Port of <c>_catHeadDisplayName</c>: "sukhdev.singh@x" → "Sukhdev Singh"; no e-mail → "Unassigned".
        /// </summary>
        public static string CatHeadDisplayName(string? email)
        {
            if (string.IsNullOrEmpty(email)) return "Unassigned";
            var local = email.Split('@')[0];
            var parts = System.Text.RegularExpressions.Regex.Split(local, @"[._0-9]+").Where(p => p.Length > 0).ToList();
            if (parts.Count == 0) return email;
            return string.Join(" ", parts.Select(w => w.Substring(0, 1).ToUpperInvariant() + w.Substring(1).ToLowerInvariant()));
        }
    }
}
