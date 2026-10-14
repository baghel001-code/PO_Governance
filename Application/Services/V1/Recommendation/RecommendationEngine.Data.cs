using System.Text.RegularExpressions;
using Application.Services.V1.Engines.Common;
using Domain.Entities.Common;
using Domain.Entities.Engine;

namespace Application.Services.V1.Recommendation
{
    /// <summary>Per-(attribute, colour) aggregate of <c>_f1f3ByAttr</c>: {s45, stk, gms}.</summary>
    public sealed class AttrColBucket
    {
        public double S45 { get; set; }
        public double Stk { get; set; }
        public List<string> Gms { get; } = new();
    }

    /// <summary>Per-attribute aggregate of <c>_f1f3ByAttr</c>: {s45, stk, cols}.</summary>
    public sealed class AttrBucket
    {
        public double S45 { get; set; }
        public double Stk { get; set; }
        public JsObjectMap<AttrColBucket> Cols { get; } = new();
    }

    /// <summary>Result of <c>_f3ByCol</c>: byCol (colour → 45D sales) and byGM (gm → {col, s45}).</summary>
    public sealed class F3ByColResult
    {
        public JsObjectMap<double> ByCol { get; } = new();
        public JsObjectMap<(string Col, double S45)> ByGM { get; } = new();
    }

    /// <summary>Result of <c>_optionLookups</c> with JS-ordered maps.</summary>
    public sealed class OptionLookups
    {
        public JsObjectMap<double> StkByOpt { get; } = new();
        public JsObjectMap<JsObjectMap<double>> F2ByOpt { get; } = new();
        public JsObjectMap<(object? ArtDesc, object? Vendor, object? VendorName)> AmInfo { get; } = new();
    }

    /// <summary>
    /// Data-access helpers of the recommendation engine (index.html lines 9378–9510, 10206–10320,
    /// 10565). Public so the attribute-analysis screen and Tool 1 / Tool 2 can reuse them.
    /// Rows are read with exact header names (<c>r['Color']</c>) exactly as the JS does.
    /// </summary>
    public static class RecommendationData
    {
        private static readonly Regex DayCol = new(@"^Day\s*\d+$", RegexOptions.ECMAScript | RegexOptions.IgnoreCase | RegexOptions.Compiled);

        internal static readonly string[] GmKeys = { "Generic Material", "GENERIC MATERIAL", "GenMat" };
        internal static readonly string[] F1ColKeys = { "Color", "Colour", "COLOR" };
        internal static readonly string[] F23ColKeys = { "Color", "Colour" };
        internal static readonly string[] StkKeys = { "OP STK", "Op Stk" };
        internal static readonly string[] PendKeys = { "Pending Qty", "PENDING QTY" };
        internal static readonly string[] DmKeys = { "Delivery Month", "delivery month" };

        /// <summary>Port of <c>_n(v)</c>.</summary>
        public static string N(object? v) => Js.N(v);

        /// <summary>Port of <c>_sf(v)</c>.</summary>
        public static double Sf(object? v) => Js.Sf(v);

        /// <summary>Port of <c>_fmt(n)</c>: <c>Math.round(n).toLocaleString()</c> — en-IN grouping (see docs/ui/recommendation.md).</summary>
        public static string Fmt(double n) => Js.Fmt(n);

        private static string G(SheetRow r, string[] keys)
        {
            foreach (var k in keys)
            {
                var v = r[k];
                if (v != null && Js.ToJsString(v).Trim() != "") return Js.ToJsString(v).Trim();
            }
            return "";
        }

        private static readonly string[] McK = { "MC CD", "MC_CD", "mc cd" };
        private static readonly string[] MrpK = { "MRP", "mrp" };
        private static readonly string[] BrK = { "Brand Type", "BRAND TYPE", "brand type" };
        private static readonly string[] FlK = { "BOGO/NON-BOGO", "Flag", "flag", "Article Type", "Flag (BOGO/NON-BOGO)" };
        private static readonly string[] ColK = { "Color", "Colour", "COLOR", "COLOUR", "colour", "color" };

        /// <summary>Port of <c>_match(r, mc, mrp, brand, flag, colour)</c> (line 9383). Empty filter = any; a row with no flag matches every flag.</summary>
        public static bool Match(SheetRow r, string? mc, string? mrp, string? brand, string? flag, string? colour)
        {
            if (!string.IsNullOrEmpty(mc) && Js.N(G(r, McK)) != Js.N(mc)) return false;
            if (!string.IsNullOrEmpty(mrp) && Js.N(G(r, MrpK)) != Js.N(mrp)) return false;
            if (!string.IsNullOrEmpty(brand) && Js.N(G(r, BrK)) != Js.N(brand)) return false;
            if (!string.IsNullOrEmpty(flag))
            {
                var rfl = Js.N(G(r, FlK));
                if (rfl != Js.N(flag) && rfl != "") return false;
            }
            if (!string.IsNullOrEmpty(colour) && Js.N(G(r, ColK)) != Js.N(colour)) return false;
            return true;
        }

        /// <summary>Sum of the "Day N" columns of an F3 row, in header order.</summary>
        internal static double DaySum(SheetRow r)
        {
            double s = 0;
            foreach (var h in r.Keys) if (DayCol.IsMatch(h)) s += Js.Sf(r[h]);
            return s;
        }

        /// <summary>Port of <c>_f1ByCol</c>: colour → opening stock.</summary>
        public static JsObjectMap<double> F1ByCol(EngineData data, string mc, string mrp, string brand, string flag)
        {
            var o = new JsObjectMap<double>();
            if (data.F1 == null) return o;
            foreach (var r in data.F1)
            {
                if (!Match(r, mc, mrp, brand, flag, null)) continue;
                var c = Js.N(JsOps.RawOr(r, "", F1ColKeys));
                o[c] = o.Get(c) + Js.Sf(JsOps.RawOr(r, 0.0, StkKeys));
            }
            return o;
        }

        /// <summary>Port of <c>_f3ByCol</c>: 45-day sales by colour and by GM.</summary>
        public static F3ByColResult F3ByCol(EngineData data, string mc, string mrp, string brand, string flag)
        {
            var res = new F3ByColResult();
            if (data.F3 == null) return res;
            foreach (var r in data.F3)
            {
                if (!Match(r, mc, mrp, brand, flag, null)) continue;
                var c = Js.N(JsOps.RawOr(r, "", F23ColKeys));
                var gm = Js.N(JsOps.RawOr(r, "", GmKeys));
                var s = DaySum(r);
                if (c.Length > 0) res.ByCol[c] = res.ByCol.Get(c) + s;
                if (gm.Length > 0)
                {
                    if (!res.ByGM.ContainsKey(gm)) res.ByGM[gm] = (c, 0);
                    var e = res.ByGM[gm];
                    res.ByGM[gm] = (e.Col, e.S45 + s);
                }
            }
            return res;
        }

        /// <summary>Port of <c>_f2ByCol</c>: colour → pending PO with delivery month ≤ <paramref name="uptoMon"/>.</summary>
        public static JsObjectMap<double> F2ByCol(EngineData data, string mc, string mrp, string brand, string flag, string uptoMon)
        {
            var o = new JsObjectMap<double>();
            if (data.F2 == null) return o;
            var lim = MonthMath.MonthOrdinal(uptoMon);
            foreach (var r in data.F2)
            {
                if (!Match(r, mc, mrp, brand, flag, null)) continue;
                var dm = JsOps.StrOr(r, DmKeys).Trim();
                if (dm.Length == 0 || MonthMath.MonthOrdinal(dm) > lim) continue;
                var c = Js.N(JsOps.RawOr(r, "", F23ColKeys));
                o[c] = o.Get(c) + Js.Sf(JsOps.RawOr(r, 0.0, PendKeys));
            }
            return o;
        }

        /// <summary>Port of <c>_f4(mc,mrp,brand,flag,mon)</c>: bucket forecast of one month.</summary>
        public static double F4(EngineData data, string mc, string mrp, string brand, string flag, string mon)
        {
            double t = 0;
            if (data.F4 == null) return t;
            foreach (var r in data.F4)
                if (Match(r, mc, mrp, brand, flag, null)) t += Js.Sf(JsOps.RawOr(r, 0.0, mon));
            return t;
        }

        /// <summary>Port of <c>_amAttrMap(dim)</c>: normalised GM → Article Master value of <paramref name="dim"/>.</summary>
        public static JsObjectMap<string> AmAttrMap(EngineData data, string dim) =>
            data.GetOrBuild("rec.amAttrMap|" + dim, d =>
            {
                var map = new JsObjectMap<string>();
                if (d.ArtMaster != null)
                    foreach (var r in d.ArtMaster)
                    {
                        var gm = Js.N(JsOps.RawOr(r, "", GmKeys));
                        if (gm.Length == 0) continue;
                        var v = JsOps.StrOr(r, dim, dim.ToLowerInvariant(), dim.ToUpperInvariant()).Trim();
                        if (v.Length > 0) map[gm] = v;
                    }
                return map;
            });

        /// <summary>Port of <c>_f1f3ByAttr</c>: F1 stock + F3 sales by (attribute value, colour) via Article Master.</summary>
        public static JsObjectMap<AttrBucket> F1F3ByAttr(EngineData data, string mc, string mrp, string brand, string flag, string dim)
        {
            var attrMap = new JsObjectMap<AttrBucket>();
            var amMap = AmAttrMap(data, dim);
            void Bump(string av, string col, double stk, double s45, string gm)
            {
                if (!attrMap.TryGetValue(av, out var a)) attrMap[av] = a = new AttrBucket();
                a.Stk += stk; a.S45 += s45;
                if (!a.Cols.TryGetValue(col, out var c)) a.Cols[col] = c = new AttrColBucket();
                c.Stk += stk; c.S45 += s45;
                if (gm.Length > 0 && !c.Gms.Contains(gm)) c.Gms.Add(gm);
            }
            if (data.F1 != null)
                foreach (var r in data.F1)
                {
                    if (!Match(r, mc, mrp, brand, flag, null)) continue;
                    var gm = Js.N(JsOps.RawOr(r, "", GmKeys));
                    var av = amMap.Get(gm);
                    if (string.IsNullOrEmpty(av)) continue;
                    var col = Js.N(JsOps.RawOr(r, "", F1ColKeys));
                    Bump(av, col, Js.Sf(JsOps.RawOr(r, 0.0, StkKeys)), 0, gm);
                }
            if (data.F3 != null)
                foreach (var r in data.F3)
                {
                    if (!Match(r, mc, mrp, brand, flag, null)) continue;
                    var gm = Js.N(JsOps.RawOr(r, "", GmKeys));
                    var av = amMap.Get(gm);
                    if (string.IsNullOrEmpty(av)) continue;
                    var col = Js.N(JsOps.RawOr(r, "", F23ColKeys));
                    Bump(av, col, 0, DaySum(r), gm);
                }
            return attrMap;
        }

        /// <summary>Port of <c>_assessDim</c>.</summary>
        public static string? AssessDim(EngineData data, string mc, string mrp, string brand, string flag)
        {
            if (data.AttrRel == null) return null;
            foreach (var r in data.AttrRel)
            {
                if (!Match(r, mc, mrp, brand, flag, null)) continue;
                var d = JsOps.StrOr(r, "Assessment Dimension", "Assessment\nDimension", "assessment dimension").Trim();
                return d.Length > 0 ? d : null;
            }
            return null;
        }

        /// <summary>Port of <c>_buildF4Map</c>: bucket forecast by "MM.YYYY".</summary>
        public static JsObjectMap<double> BuildF4Map(EngineData data, string mc, string mrp, string brand, string flag)
        {
            var o = new JsObjectMap<double>();
            if (data.F4 == null) return o;
            foreach (var r in data.F4)
            {
                if (!Match(r, mc, mrp, brand, flag, null)) continue;
                foreach (var h in r.Keys)
                {
                    var hs = h.Trim();
                    if (!MonthMath.IsMMYYYY(hs)) continue;
                    var v = Js.Num(r[h]);
                    if (v != 0 && !double.IsNaN(v)) o[hs] = o.Get(hs) + v;
                }
            }
            return o;
        }

        /// <summary>Port of <c>_buildF2RawMap</c>: colour → delivery month → pending PO.</summary>
        public static JsObjectMap<JsObjectMap<double>> BuildF2RawMap(EngineData data, string mc, string mrp, string brand, string flag)
        {
            var o = new JsObjectMap<JsObjectMap<double>>();
            if (data.F2 == null) return o;
            foreach (var r in data.F2)
            {
                if (!Match(r, mc, mrp, brand, flag, null)) continue;
                var dm = JsOps.StrOr(r, DmKeys).Trim();
                if (dm.Length == 0) continue;
                var c = Js.N(JsOps.RawOr(r, "", F23ColKeys));
                if (!o.TryGetValue(c, out var m)) o[c] = m = new JsObjectMap<double>();
                m[dm] = m.Get(dm) + Js.Sf(JsOps.RawOr(r, 0.0, PendKeys));
            }
            return o;
        }

        /// <summary>Port of <c>_mergeSessionLedgerIntoF2Map</c>: adds today's approved Single Concept quantities (mutates and returns the map).</summary>
        public static JsObjectMap<JsObjectMap<double>> MergeSessionLedgerIntoF2Map(RecommendationContext ctx, JsObjectMap<JsObjectMap<double>> f2AllCols,
                                                                                  string mc, string mrp, string brand, string flag)
        {
            if (ctx.Requests.Count == 0) return f2AllCols;
            foreach (var req in ctx.Requests)
            {
                if (!LedgerHelpers.IsToday(req, ctx.Today)) continue;
                var info = LedgerHelpers.Info(req);
                if (info == null) continue;
                if (!LedgerHelpers.SameBucket(info, mc, mrp, brand, flag)) continue;
                var approved = LedgerHelpers.ApprovedQtyFor(req);
                var month = LedgerHelpers.Get(req, "month");
                var colour = LedgerHelpers.Get(req, "colour");
                if (approved > 0 && Js.Truthy(month) && Js.Truthy(colour))
                {
                    var c = Js.N(colour);
                    var mk = Js.ToJsString(month);
                    if (!f2AllCols.TryGetValue(c, out var m)) f2AllCols[c] = m = new JsObjectMap<double>();
                    m[mk] = m.Get(mk) + approved.Value;
                }
            }
            return f2AllCols;
        }

        /// <summary>Port of <c>_buildF2RawMapByAttr</c>: attribute → colour → month → pending PO (via each F2 row's GM).</summary>
        public static JsObjectMap<JsObjectMap<JsObjectMap<double>>> BuildF2RawMapByAttr(EngineData data, string mc, string mrp, string brand, string flag, string dim)
        {
            var o = new JsObjectMap<JsObjectMap<JsObjectMap<double>>>();
            var amMap = AmAttrMap(data, dim);
            if (data.F2 == null) return o;
            foreach (var r in data.F2)
            {
                if (!Match(r, mc, mrp, brand, flag, null)) continue;
                var gm = Js.N(JsOps.RawOr(r, "", GmKeys));
                var av = amMap.Get(gm);
                if (string.IsNullOrEmpty(av)) continue;
                var dm = JsOps.StrOr(r, DmKeys).Trim();
                if (dm.Length == 0) continue;
                var c = Js.N(JsOps.RawOr(r, "", F23ColKeys));
                if (!o.TryGetValue(av, out var a)) o[av] = a = new JsObjectMap<JsObjectMap<double>>();
                if (!a.TryGetValue(c, out var m)) a[c] = m = new JsObjectMap<double>();
                m[dm] = m.Get(dm) + Js.Sf(JsOps.RawOr(r, 0.0, PendKeys));
            }
            return o;
        }

        /// <summary>Port of <c>_mergeSessionLedgerIntoF2ByAttrMap</c> (needs the request's GM to find its attribute).</summary>
        public static JsObjectMap<JsObjectMap<JsObjectMap<double>>> MergeSessionLedgerIntoF2ByAttrMap(RecommendationContext ctx,
            JsObjectMap<JsObjectMap<JsObjectMap<double>>> f2ByAttr, string mc, string mrp, string brand, string flag, string dim)
        {
            if (ctx.Requests.Count == 0) return f2ByAttr;
            var amMap = AmAttrMap(ctx.Data, dim);
            foreach (var req in ctx.Requests)
            {
                if (!LedgerHelpers.IsToday(req, ctx.Today)) continue;
                var info = LedgerHelpers.Info(req);
                if (info == null) continue;
                if (!LedgerHelpers.SameBucket(info, mc, mrp, brand, flag)) continue;
                var approved = LedgerHelpers.ApprovedQtyFor(req);
                var month = LedgerHelpers.Get(req, "month");
                var colour = LedgerHelpers.Get(req, "colour");
                var gm = LedgerHelpers.Get(req, "gm");
                if (approved > 0 && Js.Truthy(month) && Js.Truthy(colour) && Js.Truthy(gm))
                {
                    var av = amMap.Get(Js.N(gm));
                    if (string.IsNullOrEmpty(av)) continue;
                    var c = Js.N(colour);
                    var mk = Js.ToJsString(month);
                    if (!f2ByAttr.TryGetValue(av, out var a)) f2ByAttr[av] = a = new JsObjectMap<JsObjectMap<double>>();
                    if (!a.TryGetValue(c, out var m)) a[c] = m = new JsObjectMap<double>();
                    m[mk] = m.Get(mk) + approved.Value;
                }
            }
            return f2ByAttr;
        }

        /// <summary>Port of <c>_cumPO</c>: sum of the months ≤ <paramref name="uptoMon"/>.</summary>
        public static double CumPO(JsObjectMap<double>? rawMonthMap, string uptoMon)
        {
            if (rawMonthMap == null) return 0;
            var lim = MonthMath.MonthOrdinal(uptoMon);
            double tot = 0;
            foreach (var dm in rawMonthMap.Keys) if (MonthMath.MonthOrdinal(dm) <= lim) tot += rawMonthMap[dm];
            return tot;
        }

        /// <summary>Port of <c>_optionLookups</c>: stock / pending PO per "gm|colour" option and Article Master info per GM.</summary>
        public static OptionLookups BuildOptionLookups(EngineData data, string mc, string mrp, string brand, string flag)
        {
            var o = new OptionLookups();
            if (data.F1 != null)
                foreach (var r in data.F1)
                {
                    if (!Match(r, mc, mrp, brand, flag, null)) continue;
                    var key = Js.N(JsOps.RawOr(r, "", GmKeys)) + "|" + Js.N(JsOps.RawOr(r, "", F1ColKeys));
                    o.StkByOpt[key] = o.StkByOpt.Get(key) + Js.Sf(JsOps.RawOr(r, 0.0, StkKeys));
                }
            if (data.F2 != null)
                foreach (var r in data.F2)
                {
                    if (!Match(r, mc, mrp, brand, flag, null)) continue;
                    var gm = Js.N(JsOps.RawOr(r, "", GmKeys));
                    var col = Js.N(JsOps.RawOr(r, "", F23ColKeys));
                    var dm = JsOps.StrOr(r, DmKeys).Trim();
                    if (dm.Length == 0) continue;
                    var key = gm + "|" + col;
                    if (!o.F2ByOpt.TryGetValue(key, out var m)) o.F2ByOpt[key] = m = new JsObjectMap<double>();
                    m[dm] = m.Get(dm) + Js.Sf(JsOps.RawOr(r, 0.0, PendKeys));
                }
            if (data.ArtMaster != null)
                foreach (var r in data.ArtMaster)
                {
                    if (!Match(r, mc, mrp, brand, flag, null)) continue;
                    var gm = Js.N(JsOps.RawOr(r, "", GmKeys));
                    if (gm.Length == 0) continue;
                    o.AmInfo[gm] = (JsOps.RawOr(r, "", "ART DESC", "Art Desc"), JsOps.RawOr(r, "", "Vendor", "VENDOR"),
                                    JsOps.RawOr(r, "", "VENDOR NAME", "Vendor Name"));
                }
            return o;
        }

        /// <summary>Port of <c>_buildMonths</c>.</summary>
        public static List<string> BuildMonths(EngineData data, string mc, string mrp, string brand, string flag)
        {
            var ms = new List<string>();
            if (data.F4 != null)
                foreach (var r in data.F4)
                {
                    if (!Match(r, mc, mrp, brand, flag, null)) continue;
                    foreach (var h in r.Keys) if (MonthMath.IsMMYYYYExact(h) && !ms.Contains(h)) ms.Add(h);
                }
            return MonthMath.SortByOrdinal(ms);
        }
    }
}
