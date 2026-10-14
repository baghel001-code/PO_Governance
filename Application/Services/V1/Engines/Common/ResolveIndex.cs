using Domain.Entities.Common;
using Domain.Entities.Engine;

namespace Application.Services.V1.Engines.Common
{
    /// <summary>One entry of the GM resolve map (index.html <c>_idx.resolveMap</c> values).</summary>
    public sealed record GmInfo(string Brand, string Div, string McCd, string McDesc, string Mrp, string Flag,
                                string Season, string ArtDesc, string VendorName);

    /// <summary>
    /// Ports of index.html <c>_buildResolveIndex</c> (line 1695), <c>resolveGM</c> (line 3740),
    /// <c>_buildUMIndex</c> / <c>_buildBSRIndex</c> (lines 1670 / 1679) and the engine choice of
    /// <c>routeRequest</c> (line 3825). Maps are built once per <see cref="EngineData"/> snapshot.
    /// </summary>
    public static class ResolveIndex
    {
        private const string ResolveKey = "common.resolveMap";
        private const string UmKey = "common.umSet";
        private const string BsrGmKey = "common.bsrGM";

        /// <summary>GM (trimmed, case-sensitive) → info; Article Master overrides UM overrides F2.</summary>
        public static Dictionary<string, GmInfo> ResolveMap(EngineData data) => data.GetOrBuild(ResolveKey, d =>
        {
            var map = new Dictionary<string, GmInfo>(StringComparer.Ordinal);
            var srcs = new[] { d.ArtMaster, d.Um, d.F2 };
            foreach (var src in srcs.Reverse())
            {
                if (src == null) continue;
                foreach (var r in src)
                {
                    var k = r.Col("Generic Material", "generic material", "GENERIC MATERIAL", "GenMat").Trim();
                    if (k.Length == 0) continue;
                    map[k] = new GmInfo(
                        r.Col("Brand Type", "BRAND TYPE", "brand type"),
                        r.Col("Division", "DIV", "division", "div"),
                        r.Col("MC CD", "MC_CD", "mc cd", "MC Code"),
                        r.Col("MC DES", "MC DESC", "MC Desc", "mc desc", "MC Description", "MC Name"),
                        r.Col("MRP", "mrp", "MRP value", "MRP Value"),
                        r.Col("Flag (BOGO/NON-BOGO)", "BOGO/NON-BOGO", "Flag", "flag", "Article Type", "Bogo/Non-Bogo"),
                        r.Col("Season", "SEASON", "season"),
                        r.Col("ART DESC", "Art Desc", "art desc", "Article Description"),
                        r.Col("VENDOR NAME", "Vendor Name", "vendor name"));
                }
            }
            return map;
        });

        /// <summary>Port of <c>resolveGM(code)</c>: null when unknown.</summary>
        public static GmInfo? ResolveGm(EngineData data, string? code)
        {
            if (string.IsNullOrEmpty(code)) return null;
            return ResolveMap(data).TryGetValue(code.Trim(), out var i) ? i : null;
        }

        private static HashSet<string> UmSet(EngineData data) => data.GetOrBuild(UmKey, d =>
        {
            var s = new HashSet<string>(StringComparer.Ordinal);
            if (d.Um != null)
                foreach (var r in d.Um)
                {
                    var k = r.Col("Generic Material", "generic material", "GENERIC MATERIAL").Trim();
                    if (k.Length > 0) s.Add(k);
                }
            return s;
        });

        private static Dictionary<string, HashSet<string>> BsrGm(EngineData data) => data.GetOrBuild(BsrGmKey, d =>
        {
            var m = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
            if (d.Bsr != null)
                foreach (var r in d.Bsr)
                {
                    var gm = r.Col("Generic Material", "generic material", "GENERIC MATERIAL").Trim();
                    var c = r.Col("Color", "Colour", "COLOR", "COLOUR", "colour", "color").Trim();
                    if (gm.Length == 0) continue;
                    if (!m.TryGetValue(gm, out var set)) m[gm] = set = new HashSet<string>(StringComparer.Ordinal);
                    if (c.Length > 0) set.Add(c);
                }
            return m;
        });

        /// <summary>
        /// Port of <c>routeRequest(gm, colour)</c>: ("B", null) when the GM is in Universe Master;
        /// ("A", "True Repeat") when BSR has this GM in this colour; ("A", "BOBs") when BSR has the GM in
        /// other colours; else ("NEW", "New Option").
        /// </summary>
        public static (string Engine, string? OptionType) RouteRequest(EngineData data, string gm, string? colour)
        {
            var gmStr = gm.Trim();
            var colStr = (colour ?? "").Trim();
            if (UmSet(data).Contains(gmStr)) return ("B", null);
            if (BsrGm(data).TryGetValue(gmStr, out var cols))
            {
                if (cols.Contains(colStr)) return ("A", "True Repeat");
                if (cols.Count > 0) return ("A", "BOBs");
            }
            return ("NEW", "New Option");
        }
    }

    /// <summary>
    /// BSR peer-rank helpers shared by Tool 1's Engine A and the recommendation engine — ports of
    /// index.html <c>getWOF</c> (line 4213), <c>getPDSTR</c> (line 4216), <c>quartile</c> (line 4229)
    /// and <c>pctRank</c> (line 4242).
    /// </summary>
    public static class BsrPeerRank
    {
        /// <summary>Port of <c>getWOF(r)</c>.</summary>
        public static double GetWof(SheetRow r) => Js.Num(r.Col("WEEK ON FLOOR", "WEEK\n ON\nFLOOR", "WOF", "wof"));

        /// <summary>Port of <c>getPDSTR(r)</c>: a "1.72%" cell is rescaled to 0.0172.</summary>
        public static double GetPdstr(SheetRow r)
        {
            var raw = r.Col("AVG PERDAY SELL THRU%", "AVG PERDAY SELL THRU %", "avg perday sell thru%");
            var v = Js.Num(raw);
            if (raw.Contains('%')) v /= 100;
            return v;
        }

        /// <summary>
        /// Port of <c>quartile(val, pool)</c>: percentile rank of <paramref name="val"/> in the pool's PDSTRs;
        /// Q1 = top 25 % (pct &gt; 75) … Q4 = bottom 25 %. Empty pool → "Q3".
        /// </summary>
        public static string Quartile(double val, IReadOnlyList<SheetRow>? pool)
        {
            if (pool == null || pool.Count == 0) return "Q3";
            var cnt = pool.Count(r => GetPdstr(r) <= val);
            var pct = (double)cnt / pool.Count * 100;
            if (pct <= 25) return "Q4";
            if (pct <= 50) return "Q3";
            if (pct <= 75) return "Q2";
            return "Q1";
        }

        /// <summary>Port of <c>pctRank(val, pool)</c>.</summary>
        public static double PctRank(double val, IReadOnlyList<SheetRow>? pool)
        {
            if (pool == null || pool.Count == 0) return 50;
            var cnt = pool.Count(r => GetPdstr(r) <= val);
            return Js.Round((double)cnt / pool.Count * 100);
        }
    }
}
