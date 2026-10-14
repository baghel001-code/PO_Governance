using Application.Services.V1.Engines.Common;
using Application.Services.V1.Recommendation;
using Domain.Entities.Common;
using Domain.Entities.Engine;

namespace Application.Services.V1.Analytics.AttrAnalysis
{
    /// <summary>A Brand → MC node of <c>_aaBuildTree</c>.</summary>
    public sealed class AaMcNode
    {
        public string McCd { get; init; } = "";
        public string McDesc { get; init; } = "";
        public double Stk { get; set; }
        public double S45 { get; set; }
        public JsObjectMap<AaBucketNode> Buckets { get; } = new();
    }

    /// <summary>An MRP × Flag node: totals and the distinct (raw, trimmed) colours seen in F1 / F3.</summary>
    public sealed class AaBucketNode
    {
        public string Mrp { get; init; } = "";
        public string Flag { get; init; } = "";
        public double Stk { get; set; }
        public double S45 { get; set; }
        public HashSet<string> Cols { get; } = new(StringComparer.Ordinal);
    }

    /// <summary>
    /// Port of index.html <c>_aaBuildTree</c> (line 9551) and <c>_aaDiscoverAttrCols</c> (line 9531).
    /// Built once per data version (EngineData cache).
    /// </summary>
    public static class AttrAnalysisTree
    {
        /// <summary><c>AA_EXCLUDE_COLS</c> — identifier / descriptor columns of Article Master.</summary>
        public static readonly string[] ExcludeCols =
        {
            "Generic Material", "GENERIC MATERIAL", "GenMat", "Brand Type", "BRAND TYPE",
            "Division", "DIV", "DIVISION", "MC CD", "MC CODE", "MC DESC", "MC DESCRIPTION",
            "MRP", "Flag (BOGO/NON-BOGO)", "Flag", "BOGO/NON-BOGO", "ART DESC", "ARTICLE DESC",
            "Vendor", "VENDOR CODE", "VENDOR NAME"
        };

        public static JsObjectMap<JsObjectMap<AaMcNode>> Build(EngineData data) =>
            data.GetOrBuild("aa.tree", BuildTree);

        private static JsObjectMap<JsObjectMap<AaMcNode>> BuildTree(EngineData data)
        {
            var tree = new JsObjectMap<JsObjectMap<AaMcNode>>();
            (AaMcNode Mc, AaBucketNode B)? Node(SheetRow r)
            {
                var brand = JsOps.StrOr(r, "Brand Type", "BRAND TYPE").Trim();
                if (brand.Length == 0) brand = "NON-PL";
                var mc = JsOps.StrOr(r, "MC CD", "MC CODE").Trim();
                var mcDesc = JsOps.StrOr(r, "MC DESC", "MC DESCRIPTION").Trim();
                var mrp = JsOps.StrOr(r, "MRP").Trim();
                var flag = JsOps.StrOr(r, "BOGO/NON-BOGO", "Flag").Trim();
                if (flag.Length == 0) flag = "Normal";
                if (mc.Length == 0) return null;
                if (!tree.TryGetValue(brand, out var mcs)) tree[brand] = mcs = new JsObjectMap<AaMcNode>();
                var mcKey = mc + "|||" + mcDesc;
                if (!mcs.TryGetValue(mcKey, out var mcNode)) mcs[mcKey] = mcNode = new AaMcNode { McCd = mc, McDesc = mcDesc };
                var bKey = mrp + "|||" + flag;
                if (!mcNode.Buckets.TryGetValue(bKey, out var b)) mcNode.Buckets[bKey] = b = new AaBucketNode { Mrp = mrp, Flag = flag };
                return (mcNode, b);
            }
            if (data.F1 != null)
                foreach (var r in data.F1)
                {
                    var n = Node(r);
                    if (n == null) continue;
                    var stk = Js.Sf(JsOps.RawOr(r, 0.0, "OP STK", "Op Stk"));
                    n.Value.Mc.Stk += stk; n.Value.B.Stk += stk;
                    var col = JsOps.StrOr(r, "Color", "Colour").Trim();
                    if (col.Length > 0) n.Value.B.Cols.Add(col);
                }
            if (data.F3 != null)
                foreach (var r in data.F3)
                {
                    var n = Node(r);
                    if (n == null) continue;
                    var s = RecommendationData.DaySum(r);
                    n.Value.Mc.S45 += s; n.Value.B.S45 += s;
                    var col = JsOps.StrOr(r, "Color", "Colour").Trim();
                    if (col.Length > 0) n.Value.B.Cols.Add(col);
                }
            return tree;
        }

        /// <summary>Port of <c>_aaDiscoverAttrCols</c>: header names of the first 50 Article Master rows that are not identifiers.</summary>
        public static List<string> DiscoverAttrCols(EngineData data)
        {
            var am = data.ArtMaster;
            if (am == null || am.Count == 0) return new List<string>();
            var seen = new JsObjectMap<bool>();
            foreach (var row in am.Take(50))
                foreach (var h in row.Keys)
                {
                    var norm = h.Trim();
                    if (norm.Length == 0 || Array.IndexOf(ExcludeCols, norm) != -1) continue;
                    seen[norm] = true;
                }
            return seen.Keys.ToList();
        }
    }
}
