using DecisionEngine.Common;
using DecisionEngine.Model;
using DecisionEngine.Results.Tool1;

namespace DecisionEngine.Tool1
{
    /// <summary>
    /// v1.1.6 S3 (legacy/v1.1.6/patches/24_step4_caution.py) — ports of <c>_laterPOCaution</c>, <c>_laterPOName</c>,
    /// <c>_laterPOText</c> and the data of <c>_laterPOCautionHtml</c> / <c>_laterPOStep6Html</c>. Single concept New PO
    /// request card only, <b>information only</b>: when the asked quantity is allowed (Auto Allow) but a LATER month's 1st
    /// is above the limit by 1,000 pcs or more, the PO landing just before that 1st (latest first) is named with the
    /// quantity that would be too much. Worked out from the request's own month-by-month roll (<c>chain</c>); the
    /// decision, Max Permissible, recommendation, PO Assessment and the saved request do not change.
    /// </summary>
    public static class Tool1LaterPoCaution
    {
        /// <summary><c>LATER_PO_CAUTION_MIN</c>: smaller excesses are not shown (agreed 29 Oct 2026).</summary>
        public const double MinExcess = 1000;

        /// <summary>Port of <c>_laterPOCaution(r)</c>: null when there is nothing to name.</summary>
        /// <param name="decision">The card's decision (<c>r.decision</c>).</param>
        public static Tool1LaterCautionDto? Compute(string engine, string decision, Tool1EngineBResult? b, EngineParams p)
        {
            if (b == null || engine != "B" || decision != Decisions.AutoAllow || b.Chain.Count < 3 || b.NoPlan == true) return null;
            var ch = b.Chain;
            var D = JsOps.Or(p.D, 75);
            var effT = JsOps.Or(p.T1, 75) * (1 + JsOps.Or(p.StrBuffer, 0) / 100);
            var d1 = b.D1Idx ?? 1;
            double RateAt(int i)
            {
                double dem = 0, left = D;
                for (var j = i; j < ch.Count && left > 0; j++)
                {
                    double md = MonthMath.DaysInMonth(ch[j].Y, ch[j].M);
                    var t = Math.Min(md, left);
                    dem += ch[j].ColPlan * (t / md);
                    left -= t;
                }
                return dem > 0 ? dem / D : 0;
            }
            for (var k = d1 + 1; k < ch.Count; k++)
            {
                if (ch[k].Status != "Breach" || ch[k].Str == null) continue;
                var rate = RateAt(k);
                var limit = Math.Floor(effT * rate);                  // rounded down as the Max Permissible
                var excess = Js.Round(ch[k].OpenStock) - limit;
                if (excess < MinExcess) return null;                  // the first later breach only (as the Heads-up)
                double left = excess;
                var c = new Tool1LaterCautionDto { MKey = ch[k].MKey, Y = ch[k].Y, M = ch[k].M, Stock = ch[k].OpenStock, Limit = limit, Str = ch[k].Str!.Value, EffT = effT, Excess = excess };
                for (var j = k - 1; j >= d1 && left > 0; j--)
                {
                    var q = JsOps.Or(ch[j].EPO, 0);
                    if (q <= 0) continue;
                    // r.approvedPO is not set by the v1.1.6 engine: the requests part is always 0 ("PO")
                    var cut = Math.Min(q, left);
                    c.Items.Add(new Tool1LaterCautionItemDto { MKey = ch[j].MKey, Y = ch[j].Y, M = ch[j].M, Qty = q, Requests = 0, Cut = cut, Keep = q - cut });
                    left -= cut;
                }
                if (c.Items.Count == 0) return null;                  // nothing later to name (the request itself is checked in step 5)
                for (var i = d1; i <= k; i++)
                {
                    var lm = Math.Floor(effT * RateAt(i));
                    var bad = i == k;
                    c.Rows.Add(new Tool1Step6RowDto
                    {
                        Label = "1 " + MonthMath.MonLabel(ch[i].Y, ch[i].M), WindowStart = i == d1, Bad = bad,
                        Background = bad ? "#FDECEC" : (i == d1 ? "#EAF0FB" : "#fff"),
                        Stock = Js.Fmt(Js.Round(ch[i].OpenStock)), Limit = Js.Fmt(Js.Round(lm)),
                        Str = ch[i].Str != null ? Js.NumberToString(Js.Round(ch[i].Str!.Value)) + "d" : "—",
                        StrColour = bad ? "#DC2626" : "#00873E", Excess = bad ? Js.Fmt(excess) : "—"
                    });
                }
                Texts(c);
                return c;
            }
            return null;
        }

        /// <summary>Port of <c>_laterPOName(it)</c>.</summary>
        public static string Name(Tool1LaterCautionItemDto it) =>
            MonthMath.MonLabel(it.Y, it.M) + (it.Requests > 0 ? (it.Requests < it.Qty ? " POs and requests" : " requests") : " PO");

        /// <summary>The card texts (<c>_laterPOText</c>, plain and with the bold parts) and the Step 6 working.</summary>
        private static void Texts(Tool1LaterCautionDto c)
        {
            var multi = c.Items.Count > 1;
            var seg = new List<Tool1TextSegmentDto> { new("With this request, ") };
            for (var i = 0; i < c.Items.Count; i++)
            {
                var it = c.Items[i];
                if (i > 0) seg.Add(new(" and "));
                seg.Add(new("the "));
                seg.Add(new(Name(it) + " " + Js.Fmt(Js.Round(it.Qty)), true));
                if (multi) seg.Add(new(" (reduce to " + Js.Fmt(Js.Round(it.Keep)) + ")"));
            }
            seg.Add(new(" will be "));
            seg.Add(new(Js.Fmt(c.Excess) + " too much", true));
            seg.Add(new(" on 1 " + MonthMath.MonLabel(c.Y, c.M) + " (" + Js.NumberToString(Js.Round(c.Str)) + "d vs " + Js.NumberToString(Js.Round(c.EffT)) + "d limit)."));
            if (!multi)
            {
                seg.Add(new(" Consider reducing it to "));
                seg.Add(new(Js.Fmt(Js.Round(c.Items[0].Keep)), true));
                seg.Add(new("."));
            }
            c.Segments = seg;
            c.Text = string.Concat(seg.Select(s => s.Text));
            c.LimitHeader = "Limit (" + Js.NumberToString(Js.Round(c.EffT * 100) / 100) + "d × daily rate)";
            c.Working.Add("Too much on 1 " + MonthMath.MonLabel(c.Y, c.M) + " = Stock on the 1st − Limit = " + Js.Fmt(Js.Round(c.Stock)) + " − " + Js.Fmt(Js.Round(c.Limit)) + " = " + Js.Fmt(c.Excess));
            c.Working.Add("Named to the POs / requests landing just before that 1st (latest first):");
            foreach (var it in c.Items)
                c.Working.Add(Name(it) + " " + Js.Fmt(Js.Round(it.Qty)) + " − " + Js.Fmt(Js.Round(it.Cut)) + " = " + Js.Fmt(Js.Round(it.Keep)));
            c.Footer = "Information only: this request's decision and Max Permissible (Step 5) do not change. Smaller excesses (under " + Js.Fmt(MinExcess) + " pcs) are not shown.";
        }

        private static readonly System.Text.RegularExpressions.Regex HeadsUp = new(@" \[Heads-up:[^\]]*\]", System.Text.RegularExpressions.RegexOptions.Compiled);

        /// <summary>The card reason when the caution names the later PO: the Heads-up sentence is not repeated (first match only).</summary>
        public static string ReasonWithoutHeadsUp(string reason) => HeadsUp.Replace(reason ?? "", "", 1);
    }
}
