using Application.DTOs.Recommendation;
using Application.DTOs.Tool2;
using Domain.Entities.Common;

namespace POGovernance.Tests.Tool2
{
    /// <summary>
    /// Renders the DTOs to the text tokens the v1.1.4 HTML showed (the harness splits innerHTML at tags), in
    /// the same order — so the parity tests check that the server produces every text of the screen.
    /// </summary>
    public static class Tool2Tokens
    {
        private static IEnumerable<string> Segs(IEnumerable<TextSegDto> s) => s.Select(x => x.Text);

        public static List<string> Reason(Tool2ReasonDto d)
        {
            var t = new List<string>();
            foreach (var kv in d.Kvs) { t.Add(kv.Label); t.Add(kv.Value); }
            t.Add("Why this action:");
            t.AddRange(Segs(d.Why));
            if (d.Caution != null) t.AddRange(Caution(d.Caution));
            if (d.StrSteps != null)
            {
                t.Add("STR Computation Steps:");
                foreach (var l in d.StrSteps) t.AddRange(Segs(l));
            }
            if (d.Steps is { } s)
            {
                t.Add("Step 1 — Article & Peer Pool");
                foreach (var x in s.Step1) { t.Add(x.Value); t.Add(x.Label); }
                if (s.Step2 != null)
                {
                    t.Add("Step 2 — PDSTR vs Peer Average");
                    foreach (var x in s.Step2) { t.Add(x.Value); t.Add(x.Label); }
                }
                if (s.Step3Heading != null) { t.Add(s.Step3Heading); t.AddRange(new[] { "Q4 Slow", "Q3 Watch", "Q2 Healthy", "Q1 Top" }); }
                if (s.Step4 is { } b)
                {
                    t.Add("Step 4 — Border Zone Check (PDSTR Gap)");
                    t.Add(b.Threshold); t.Add(b.Gap); t.Add("Result" + new string(' ', 16) + ":"); t.Add(b.Result); t.Add(b.Message);
                }
                if (s.AbsBlockNote != null) t.Add(s.AbsBlockNote);
            }
            return t;
        }

        public static List<string> Caution(StrCautionDto c)
        {
            var t = new List<string> { "⚠ Caution — STR" };
            foreach (var it in c.Items)
            {
                t.Add(it.Text); t.Add("▾ Show calculation");
                if (it.Cs.Chain.Count > 0)
                {
                    t.AddRange(new[] { "Month", "Open", "+PO", "−Fcst", "=Close" });
                    var i = 0;
                    foreach (var ch in it.Cs.Chain.Take(4))
                    {
                        t.Add(ch.MKey + (i == 0 ? " (a)" : ""));
                        t.Add(Js.Fmt(ch.OpenStock)); t.Add(Js.Fmt(ch.PoIn)); t.Add(Js.Fmt(ch.ColPlan)); t.Add(Js.Fmt(ch.CloseStock));
                        i++;
                    }
                    var cut = it.Footer.LastIndexOf(" = ", StringComparison.Ordinal);
                    t.Add(it.Footer[..(cut + 2)]); t.Add(it.Footer[(cut + 3)..]);
                }
            }
            return t;
        }

        public static List<string> Kpis(List<Tool2KpiDto> k) => k.SelectMany(x => new[] { x.Value, x.Label }).ToList();

        public static List<string> DivChart(List<Tool2ChartBarDto> b) =>
            b.SelectMany(x => new[] { x.Label, x.Note }).Concat(new[] { "Retain", "Reduce", "Cancel", "Watch" }).ToList();

        public static List<string> MonthChart(List<Tool2ChartBarDto> b) => b.SelectMany(x => new[] { x.Label, x.Note }).ToList();

        public static List<string> Detail(Tool2DetailViewDto v, Dictionary<int, Tool2LineDto> lines)
        {
            var t = new List<string>();
            if (v.Empty) return t;
            if (v.Tab == "B")
            {
                foreach (var m in v.Months!)
                {
                    t.Add("📅 " + m.Month); t.Add(m.GroupsText); t.Add(m.ExcessText); t.Add(m.TotalQtyText);
                    foreach (var g in m.Groups)
                    {
                        t.AddRange(new[] { g.Colour, g.Brand, g.McDesc, "₹" + g.Mrp, g.Flag, g.Dm, g.PoCount.ToString(), g.TotalQtyText, g.MixText, g.StrText, g.MaxPermText, g.ColourDecision, "▼", g.PanelTitle });
                        if (g.WhyNoStr != null) { t.Add("Why no STR was computed:"); t.Add(g.WhyNoStr); }
                        foreach (var kv in g.Kvs) { t.Add(kv.Label); t.Add(kv.Value); }
                        if (g.Formula != null) foreach (var l in g.Formula) t.AddRange(Segs(l));
                        t.Add("PO-wise Allocation (FIFO: largest PO cut first)");
                        t.AddRange(new[] { "Colour", "Generic Material", "PO Number", "Released Qty", "Recommended Action", "Max Permissible Qty" });
                        foreach (var p in g.PoActions) t.AddRange(new[] { g.Colour, p.Gm, p.PoNum, Js.Fmt(p.RelQty), p.Action, p.RecQty > 0 ? Js.Fmt(p.RecQty) : "0" });
                        t.AddRange(new[] { "TOTAL", Js.Fmt(g.TotalQty), Js.Fmt(g.TotalRetain) });
                    }
                }
                return t;
            }
            foreach (var idx in v.Rows!)
            {
                var r = lines[idx];
                t.AddRange(new[] { r.PoNum, r.Gm, r.Colour, r.Brand, r.Div, r.McDesc, "₹" + r.Mrp, r.Dm, Js.Fmt(r.RelQty),
                    string.IsNullOrEmpty(r.OptionType) ? "—" : r.OptionType, string.IsNullOrEmpty(r.Quartile) ? "—" : r.Quartile,
                    r.Percentile != null ? Js.NumberToString(r.Percentile.Value) + "th" : "—", r.Decision, "▼", r.Decision + " — Detailed Computation" });
                t.AddRange(Reason(r.Reason));
            }
            return t;
        }

        private static IEnumerable<string> RollupLines(List<int> rows, Tool2RollupDto d)
        {
            if (rows.Count == 0) return new[] { "No action-required lines" };
            var t = new List<string> { "GM", "Colour", "MRP", "PO Number", "Released", "Cut Qty", "Decision" };
            foreach (var i in rows) { var l = d.Lines[i]; t.AddRange(new[] { l.Gm, l.Colour, l.MrpText, l.PoNum, l.Released, l.CutQty, l.Decision }); }
            return t;
        }

        public static List<string> Vendors(Tool2RollupDto d)
        {
            var t = new List<string>();
            if (d.Vendors.Count == 0) return new List<string> { "No rows match the selected filters" };
            foreach (var v in d.Vendors)
            {
                if (v.Rows.Count > 0) t.Add("▸");
                t.AddRange(new[] { v.Vendor, v.Total.ToString(), v.Retain.ToString(), v.Reduce.ToString(), v.Cancel.ToString(), v.Watch.ToString(), v.AtRiskText, v.ValueText });
                t.AddRange(RollupLines(v.Rows, d));
            }
            return t;
        }

        public static List<string> DivMc(Tool2RollupDto d)
        {
            var t = new List<string>();
            if (d.DivMc.Count == 0) return new List<string> { "No rows match the selected filters" };
            foreach (var v in d.DivMc)
            {
                if (v.Rows.Count > 0) t.Add("▸");
                t.AddRange(new[] { v.Brand, v.Div, v.Mc, v.Lines.ToString(), v.Reduce.ToString(), v.Cancel.ToString(), v.Watch.ToString(), v.AtRiskText, v.ValueText, v.PctText });
                t.AddRange(RollupLines(v.Rows, d));
            }
            return t;
        }
    }
}
