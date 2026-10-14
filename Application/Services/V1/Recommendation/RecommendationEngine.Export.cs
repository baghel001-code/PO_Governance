using Application.DTOs.Recommendation;
using Application.Services.V1.Engines.Common;
using Domain.Entities.Common;
using Domain.Entities.Engine;
using Domain.Exceptions;
using static Application.Services.V1.Recommendation.RecommendationData;

namespace Application.Services.V1.Recommendation
{
    public sealed partial class RecommendationEngine
    {
        // ── _exportEngineARecs (line 10970) ───────────────────────────────────

        public RecommendationExportDto BuildExport(RecommendationContext ctx, RecAResultDto res, string fileDate)
        {
            if (res.Recs == null || res.Recs.Count == 0) throw new AppValidationException("No recommendation rows to export");
            var recs = res.Recs;
            var months = res.Months ?? new List<string>();
            var attrs = res.Attrs ?? new List<RecAttrDto>();
            var export = new RecommendationExportDto { FileName = "VMM_EngineA_FullDetail_" + fileDate + ".xlsx" };

            // Sheet 1 — Recommendations (every column regardless of on-screen toggles)
            var header = new List<object?> { "Attribute", "Colour", "Stock Mix% (within attr)", "Sale Mix% (within attr)", "Deviation%",
                "Gen Mat Recom", "GM Description", "Vendor Cd", "Vendor Name", "BSR Q", "PDSTR%/d", "Priority",
                "Option Stock Qty", "Colour Stock Qty", "Attribute Stock Qty" };
            for (var i = 0; i < months.Count; i++)
            {
                var m = months[i];
                header.Add(m + " Colour PO Qty"); header.Add(m + " Option PO Qty");
                if (i > 0) { header.Add(m + " Attr Op STR"); header.Add(m + " Col Op STR"); }
                header.Add(m + " Rec Qty");
            }
            var s1 = new ExportSheetDto { Name = "1-Recommendations", Rows = { header }, Widths = header.Select(_ => 16.0).ToList() };
            foreach (var r in recs)
            {
                var row = new List<object?> { r.Attr, r.Col.ToUpperInvariant(), r.ColStockMixInAttr, r.ColMixInAttr, r.ColDev,
                    r.Gm, r.GmDesc ?? "", r.VendorCd ?? "", r.VendorName ?? "", r.Q, r.Pdstr, r.Priority,
                    r.OptionStockQty, r.ColStockQty, r.AttrStockQty };
                for (var mi = 0; mi < r.MData.Count; mi++)
                {
                    var md = r.MData[mi];
                    row.Add(md.Pend); row.Add(md.OptPend);
                    if (mi > 0)
                    {
                        var attrMd = r.AttrAmdata != null && mi - 1 < r.AttrAmdata.Count ? r.AttrAmdata[mi - 1] : null;
                        var colMd = r.MData[mi - 1];
                        row.Add(attrMd != null && attrMd.Str != null ? attrMd.Str.Value : "");
                        row.Add(colMd != null && colMd.Str != null ? colMd.Str.Value : "");
                    }
                    row.Add(md.Rec > 0 ? md.Rec : 0.0);
                }
                s1.Rows.Add(row);
            }
            export.Sheets.Add(s1);

            // Sheet 2 — every colour under every attribute
            var recSet = new JsObjectMap<string>();
            foreach (var r in recs) recSet[r.Attr + "|" + r.Col] = r.Priority;
            var s2 = new ExportSheetDto
            {
                Name = "2-All Colours (full bucket)",
                Widths = new() { 12, 10, 14, 14, 16, 16, 12, 20, 14, 12, 14 },
                Rows = { new List<object?> { "Attribute", "Colour", "Colour Stock Qty", "Colour 45D Sales", "Stock Mix% (within attr)", "Sale Mix% (within attr)",
                    "Deviation%", "Colour STR (anchor " + (res.AnchorMonth ?? "") + ")", "# Generic Materials in this Colour", "Recommended?", "Priority if recommended" } }
            };
            foreach (var attr in attrs)
                foreach (var cd in attr.ColRanked ?? new List<ColRankDto>())
                {
                    var stockMix = attr.Stk > 0 ? Js.Round(cd.Stk / attr.Stk * 1000) / 10 : 0;
                    var saleMix = attr.S45 > 0 ? Js.Round(cd.S45 / attr.S45 * 1000) / 10 : 0;
                    var dev = Js.Round((saleMix - stockMix) * 100) / 100;
                    var key = attr.Av + "|" + cd.Col;
                    var pr = recSet.Get(key);
                    s2.Rows.Add(new List<object?> { attr.Av, cd.Col.ToUpperInvariant(), cd.Stk, cd.S45, stockMix, saleMix, dev,
                        cd.Str >= 9999 ? "No data" : cd.Str, (double)(cd.Gms?.Count ?? 0), !string.IsNullOrEmpty(pr) ? "Y" : "N", pr ?? "" });
                }
            export.Sheets.Add(s2);

            // Sheet 3 — every GM within every colour
            var opt = res.OptLookups ?? new OptionLookupsDto();
            var s3 = new ExportSheetDto
            {
                Name = "3-All Gen Materials",
                Widths = new() { 12, 10, 16, 26, 12, 22, 18 },
                Rows = { new List<object?> { "Attribute", "Colour", "Generic Material", "GM Description", "Vendor Cd", "Vendor Name", "Option Stock Qty (this exact GM+Colour)" } }
            };
            foreach (var attr in attrs)
                foreach (var cd in attr.ColRanked ?? new List<ColRankDto>())
                    foreach (var gm in cd.Gms ?? new List<string>())
                    {
                        opt.AmInfo.TryGetValue(Js.N(gm), out var info);
                        var stk = opt.StkByOpt.TryGetValue(Js.N(gm) + "|" + Js.N(cd.Col), out var sv) ? JsOps.Or(sv, 0) : 0;
                        s3.Rows.Add(new List<object?> { attr.Av, cd.Col.ToUpperInvariant(), gm, Or(info?.ArtDesc), Or(info?.Vendor), Or(info?.VendorName), stk });
                    }
            export.Sheets.Add(s3);

            // Sheet 4 — raw pending PO lines of the bucket
            var gmToAttr = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var attr in attrs)
                foreach (var cd in attr.ColRanked ?? new List<ColRankDto>())
                    foreach (var gm in cd.Gms ?? new List<string>())
                        gmToAttr[Js.N(gm)] = attr.Av;
            var s4 = new ExportSheetDto
            {
                Name = "4-Pending PO Detail (raw)",
                Widths = new() { 16, 12, 10, 16, 14, 14, 22 },
                Rows = { new List<object?> { "PO Number", "Attribute", "Colour", "Generic Material", "Delivery Month", "Pending Qty", "Vendor Name" } }
            };
            if (ctx.Data.F2 != null)
                foreach (var r in ctx.Data.F2)
                {
                    if (!Match(r, res.Mc, res.Mrp, res.Brand, res.Flag, null)) continue;
                    var gm = JsOps.RawOr(r, "", GmKeys);
                    var col = JsOps.RawOr(r, "", F23ColKeys);
                    var dm = JsOps.RawOr(r, "", DmKeys);
                    if (!Js.Truthy(dm)) continue;
                    s4.Rows.Add(new List<object?> { Cell(JsOps.RawOr(r, "", "PO Number", "PO No")),
                        gmToAttr.TryGetValue(Js.N(gm), out var av) && av.Length > 0 ? av : "(attribute unknown)",
                        Js.ToJsString(col).ToUpperInvariant(), Cell(gm), Cell(dm), Js.Sf(JsOps.RawOr(r, 0.0, PendKeys)),
                        Cell(JsOps.RawOr(r, "", "Vendor Name", "VENDOR NAME")) });
                }
            export.Sheets.Add(s4);

            // Sheet 5 — Q1/Q2 selection trace per recommended colour
            var s5 = new ExportSheetDto
            {
                Name = "5-Q1-Q2 Selection Trace",
                Widths = new() { 12, 10, 26, 16, 12, 12, 10, 24, 38 },
                Rows = { new List<object?> { "Attribute", "Colour", "Pass checked", "Candidate GM", "WOF (weeks)", "PDSTR%/day", "Quartile", "Selected?", "Why" } }
            };
            foreach (var r in recs)
            {
                var attr = attrs.FirstOrDefault(a => a.Av == r.Attr);
                var cd = attr?.ColRanked?.FirstOrDefault(c => c.Col == r.Col);
                var candidates = cd != null && cd.Gms != null && cd.Gms.Count > 0
                    ? cd.Gms : GetGMsForColour(ctx, res.Mc ?? "", res.Mrp ?? "", res.Brand ?? "", res.Flag ?? "", r.Col);
                foreach (var passQ in new[] { "Q1", "Q2" })
                {
                    if (passQ != r.Q) continue;
                    var rows5 = new List<(string Gm, double Wof, double Pdstr, string Q)>();
                    foreach (var gm in candidates)
                    {
                        var bsr = BsrQ(ctx, gm, r.Col, res.Mc ?? "", res.Mrp ?? "", res.Flag ?? "");
                        if (bsr == null || bsr.Q.ToUpperInvariant() != passQ) continue;
                        rows5.Add((gm, bsr.Wof, bsr.Pdstr, bsr.Q));
                    }
                    JsSort.StableSort(rows5, (a, b) => b.Pdstr - a.Pdstr);
                    for (var i = 0; i < rows5.Count; i++)
                    {
                        var c = rows5[i];
                        var isWinner = c.Gm == r.Gm;
                        s5.Rows.Add(new List<object?> { r.Attr, r.Col.ToUpperInvariant(), passQ + " pool (" + rows5.Count + " qualifying GMs)", c.Gm, c.Wof,
                            Js.Round(c.Pdstr * 1000) / 10, c.Q, isWinner ? "✓ SELECTED" : "",
                            isWinner ? "Highest PDSTR%/day among " + passQ + " candidates" : (i == 0 ? "" : "Lower PDSTR%/day than the selected GM") });
                    }
                    if (rows5.Count == 0)
                        s5.Rows.Add(new List<object?> { r.Attr, r.Col.ToUpperInvariant(), passQ + " pool", r.Gm, "", "", r.Q, "✓ SELECTED (only candidate found)", "" });
                }
            }
            export.Sheets.Add(s5);
            return export;
        }

        private static string Or(string? v) => string.IsNullOrEmpty(v) ? "" : v;

        /// <summary>A raw cell for the workbook: numbers stay numbers, everything else its JS string.</summary>
        private static object? Cell(object? v) => v switch
        {
            null => "",
            double d => d,
            int i => (double)i,
            long l => (double)l,
            string s => s,
            _ => Js.ToJsString(v)
        };

        // ── _paintRec (line 11158) ────────────────────────────────────────────

        private static readonly Dictionary<string, string> QLabel = new()
        {
            ["Q1"] = "Q1 — Priority 1 (best-quartile peers)",
            ["Q2"] = "Q2 — Priority 2 (second-quartile peers)"
        };

        public RecommendationPaintDto Paint(EngineParams p, RecBResultDto? b, RecAResultDto? a)
        {
            var T = JsOps.Or(p.T1, 75); var buf = JsOps.Or(p.StrBuffer, 0); var eff = T * (1 + buf / 100);
            var paint = new RecommendationPaintDto { T = T, Buf = buf, Eff = eff, Wof = p.Wof };
            if (b != null)
            {
                if (b.Rows.Count == 0) { paint.EmptyMessage = "No colour data found for this MC×MRP in F1/F3."; return paint; }
                paint.Heading = "Colour-wise Stock & PO Recommendation — " + b.Rows.Count + " colours · ranked Stock High to Low · EffT=" + Js.NumberToString(b.Eff) + "d";
                double totalRec = 0;
                foreach (var r in b.Rows)
                    foreach (var md in r.MData)
                    {
                        totalRec += md.Rec;
                        md.Tip = md.Action == "Watch List" ? null
                            : "Based on threshold STR of " + Js.NumberToString(md.Eff ?? eff) + "d, the max permissible qty is " + Fmt(md.Rec);
                    }
                paint.TotalRec = totalRec;
                paint.RaiseCount = b.Rows.Count(r => r.MData.Any(m => m.Action == "Raise"));
                paint.Summary = paint.RaiseCount + " colours have headroom across selected months. Total recommended units: " + Fmt(totalRec) +
                                ". EffT=" + Js.NumberToString(b.Eff) + "d (T=" + Js.NumberToString(T) + "d + " + Js.NumberToString(buf) + "% buffer).";
                return paint;
            }
            if (a == null) return paint;
            if (a.Err != null) { paint.EmptyMessage = a.Err; paint.EmptyIsError = true; return paint; }
            if (a.DimMode == "colour-only") return Paint(p, a.Data, null);

            paint.Heading = "Step 1 — Attribute STR Ranking · Dimension: " + a.Dim;
            paint.Step2Heading = "Step 2+3 — Colour × GM Recommendations (Q1 → Q2, BSR-validated only)";
            var recs = a.Recs ?? new List<RecARecDto>();
            if (recs.Count == 0)
            {
                paint.NoRecsMessage = "No eligible Q1/Q2 GM found for any low-STR colour combination. \nCheck: (1) BSR file uploaded, (2) Article Master has " + a.Dim +
                                      " values filled in for GMs in this MC×MRP, (3) GMs have WOF ≥ " + Js.NumberToString(p.Wof) + "w in BSR.";
                return paint;
            }
            var attrIndex = new Dictionary<string, int>(StringComparer.Ordinal);
            var attrs = a.Attrs ?? new List<RecAttrDto>();
            for (var i = 0; i < attrs.Count; i++) attrIndex[attrs[i].Av] = i;

            var spans = recs.Select(_ => (IsFirst: true, Span: 1)).ToList();
            var runStart = 0;
            for (var si = 0; si < recs.Count; si++)
            {
                if (si > 0 && recs[si].Attr == recs[si - 1].Attr && recs[si].Q == recs[si - 1].Q)
                {
                    spans[si] = (false, spans[si].Span);
                    spans[runStart] = (spans[runStart].IsFirst, spans[runStart].Span + 1);
                }
                else runStart = si;
            }
            string? lastQ = null;
            double tot2 = 0;
            for (var i = 0; i < recs.Count; i++)
            {
                var r = recs[i];
                string? section = null;
                if (r.Q != lastQ) { lastQ = r.Q; section = QLabel.TryGetValue(r.Q, out var ql) ? ql : r.Q; }
                var isLast = i == recs.Count - 1 || recs[i + 1].Attr != r.Attr || recs[i + 1].Q != r.Q;
                var col = r.Col.ToUpperInvariant();
                paint.RecLayout.Add(new RecRowLayoutDto
                {
                    SectionHeader = section, IsFirstInRun = spans[i].IsFirst, Span = spans[i].Span, IsLastInRun = isLast,
                    AttrIndex = attrIndex.TryGetValue(r.Attr, out var ai) ? ai : 0,
                    TraceHeader = col + " — Option Stock Qty " + Fmt(r.OptionStockQty) + " units (this GM+Colour) · Colour Stock Qty " + Fmt(r.ColStockQty) +
                                  " units · Sale Mix% (of " + r.Attr + "'s 45D sales) = " + Js.NumberToString(r.ColMixInAttr) + "% · Stock Mix% (of " + r.Attr +
                                  "'s stock) = " + Js.NumberToString(r.ColStockMixInAttr) + "%",
                    QtyHeader = r.Gm + " recommended for " + col + " (" + r.Attr + ") — Recommended Qty, month by month:"
                });
                foreach (var md in r.MData)
                {
                    tot2 += md.Rec;
                    var noData = md.Str == null || md.EffExact == null;
                    md.Formula = noData ? "—"
                        : Js.NumberToString(Js.Round(md.EffExact!.Value * 100) / 100) + " × " + Js.NumberToString(md.Dr) + " − " + Fmt(md.WinStockStart ?? double.NaN) + " = " + Fmt(md.Rec);
                }
            }
            paint.TotalRec = tot2;
            paint.P1 = recs.Count(r => r.Priority == "P1");
            paint.P2 = recs.Count(r => r.Priority == "P2");
            paint.RaiseCount = paint.P1;
            paint.Summary = recs.Count + " Colour×GM combinations recommended. P1 (Q1): " + paint.P1 + " · P2 (Q2): " + paint.P2 +
                            ". Total units: " + Fmt(tot2) + ". EffT=" + Js.NumberToString(a.Eff ?? Js.Round(eff)) + "d.";
            return paint;
        }
    }
}
