using System.Globalization;
using System.Text.RegularExpressions;
using DecisionEngine.Common;
using DecisionEngine.Model;
using DecisionEngine.Results.Explain;
using DecisionEngine.Results.Recommendation;
using DecisionEngine.Results.Tool1;
using DecisionEngine.Tool1;

namespace DecisionEngine.Explain
{
    /// <summary>
    /// v1.1.6 S8 — port of the single concept part of legacy/v1.1.6/app/explain.js: <c>singleNumbers</c>,
    /// <c>Explain.single</c> (the side panel), <c>singleBook</c> ("this calculation" workbook), <c>singleAllBook</c>
    /// ("all lines"), <c>recoCtx</c> / <c>requestCtx</c> / <c>openForRequest</c>. Display only: every number comes from the
    /// engines' own month-by-month roll (<c>preRoll</c> / <c>pre</c> and <c>chain</c>); nothing here changes a result.
    /// The v1.1.7 MRA wording of explain.js is left out (v1.1.6 has no MRAs).
    /// </summary>
    public static class SingleExplainBuilder
    {
        private static readonly string[] Mn = { "Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec" };

        /// <summary>One chain month as explain.js reads it (Tool 1 <c>ePO</c> or recommendation <c>poIn</c>).</summary>
        public sealed record ChainMonth(int Y, int M, string MKey, double OpenStock, double Po, double ColPlan, double CloseStock, double Fc, double DemandDays, double TotalDays);

        /// <summary>The explain.js context <c>c</c> of one number.</summary>
        public sealed class Ctx
        {
            public string Colour = "";
            public string Month = "";
            public double Stock;
            public double Mix;
            public double? S45Share;
            public string MixNote = "";
            public double T1, Buf, D, MixT;
            public bool Watch;
            public double? Rec;
            public List<PreRollMonthDto> Pre = new();
            public List<ChainMonth> Chain = new();
            /// <summary>The asked quantity of a New PO request; null for the Recommendation.</summary>
            public double? RequestQty;
            public string Group = "";
            /// <summary>Server date "yyyy-MM-dd" (explain.js used the PC clock).</summary>
            public string Today = "";
        }

        // ── formatting (explain.js helpers) ────────────────────────────────

        private static string N(double? v) => v == null || double.IsNaN(v.Value) ? "—" : Js.Fmt(v.Value);
        private static string D1(double v)
        {
            if (double.IsNaN(v)) return "—";
            var r = Js.Round(v * 10) / 10;
            var s = Math.Abs(r).ToString("0.0", CultureInfo.InvariantCulture);
            var dot = s.IndexOf('.');
            return (r < 0 ? "-" : "") + Js.GroupIN(double.Parse(s[..dot], CultureInfo.InvariantCulture)) + s[dot..];
        }
        private static string D2(double v) => double.IsNaN(v) ? "—" : Js.ToFixed(v, 2);
        private static string Pc(double v, int dp = 1) => double.IsNaN(v) ? "—" : Js.ToFixed(v * 100, dp) + "%";
        private static string Num(double v) => Js.NumberToString(v);
        private static (int Y, int M) Ym(string mk)
        {
            var p = (mk ?? "").Split('.');
            return p.Length == 2 ? ((int)MonthMath.ParseInt(p[1]), (int)MonthMath.ParseInt(p[0])) : (0, 0);
        }
        private static string MonL(string mk) { var (y, m) = Ym(mk); return m is >= 1 and <= 12 ? Mn[m - 1] + " " + y.ToString(CultureInfo.InvariantCulture) : mk ?? ""; }
        private static string MonShort(string mk) { var (_, m) = Ym(mk); return m is >= 1 and <= 12 ? Mn[m - 1] : ""; }
        private static string LastDay(string mk) { var (y, m) = Ym(mk); return MonthMath.DaysInMonth(y, m).ToString(CultureInfo.InvariantCulture) + " " + Mn[m - 1]; }

        /// <summary>explain.js <c>groupText(info)</c>.</summary>
        public static string GroupText(string? brand, string? mcDesc, string? mcCd, string? mrp, string? flag) =>
            string.Join(" · ", new[] { brand, !string.IsNullOrEmpty(mcDesc) ? mcDesc : mcCd, !string.IsNullOrEmpty(mrp) ? "₹" + mrp : "", flag }.Where(s => !string.IsNullOrEmpty(s)));

        // ── singleNumbers ──────────────────────────────────────────────────

        public sealed record WindowRow(int K, string MKey, double Take, double Days, double Sale);

        public sealed record Numbers(double WStart, double WDem, double Rate, double Eff, double Need, double StartNoProp, double Can, List<WindowRow> WRows, double Po0);

        /// <summary>Port of <c>singleNumbers(c)</c>.</summary>
        public static Numbers SingleNumbers(Ctx c)
        {
            var ch = c.Chain;
            var D = c.D;
            const int d1 = 1;
            var wstart = ch.Count > d1 ? ch[d1].OpenStock : 0;
            double budget = D, wdem = 0;
            var wrows = new List<WindowRow>();
            for (var k = d1; k < ch.Count && budget > 0; k++)
            {
                double days = MonthMath.DaysInMonth(ch[k].Y, ch[k].M);
                var take = Math.Min(days, budget);
                wdem += ch[k].ColPlan * (take / days);
                budget -= take;
                wrows.Add(new WindowRow(k, ch[k].MKey, take, days, ch[k].ColPlan * (take / days)));
            }
            var rate = D > 0 && wdem > 0 ? wdem / D : 0;
            var eff = c.T1 * (1 + c.Buf / 100);
            var ch0 = ch.Count > 0 ? ch[0] : null;
            var po0 = ch0?.Po ?? 0;
            var startNoProp = c.RequestQty != null ? (ch0?.OpenStock ?? 0) + po0 - (ch0?.ColPlan ?? 0) : wstart;   // Tool 1: unclamped, without the asked qty
            var can = Math.Max(0, Math.Floor(eff * rate - startNoProp));
            return new Numbers(wstart, wdem, rate, eff, eff * rate, startNoProp, can, wrows, po0);
        }

        private static ExplainStepDto Step(int no, params ExplainSegDto[] segs) => new() { No = no.ToString(CultureInfo.InvariantCulture), Segments = segs.ToList() };
        private static ExplainSegDto T(string s) => new(s);
        private static ExplainSegDto B(string s) => new(s, true);

        // ── Explain.single ─────────────────────────────────────────────────

        /// <summary>Port of <c>Explain.single(c)</c>: the panel of one number (Watch List, a request's Max Permissible or a "can raise").</summary>
        public static SingleExplainDto Single(Ctx c)
        {
            var (ty, tm, _) = MonthMath.ParseIsoDate(c.Today);
            var curKey = MonthMath.Mmyyyy(ty, tm);
            var ch = c.Chain;
            var ch0 = ch.Count > 0 ? ch[0] : null;
            var ch1 = ch.Count > 1 ? ch[1] : null;
            var dm = c.Month;
            var nextKey = ch1?.MKey ?? "";
            var x = new SingleExplainDto { Sub = c.Group };
            if (c.Watch)
            {
                x.Steps.Add(Step(1, T("This colour sold "), B(Pc(c.Mix, 1)), T(" of the group in the last 45 days" + (c.MixNote.Length > 0 ? " (" + c.MixNote + ")" : "") + ".")));
                x.Steps.Add(Step(2, T("From step 1: that is under the colour minimum of "), B(Num(c.MixT) + "%"), T(", so the tool does not work out a quantity — sizes are probably broken or cut.")));
                x.Result = WatchResult();
                x.Title = c.Colour + " · delivery " + MonL(dm) + " · Watch List";
                return x;
            }
            var n = SingleNumbers(c);
            var s = 1;
            var pre = c.Pre;
            x.Steps.Add(Step(s++, T("Stock today "), B(N(c.Stock)), T(" — all stores, F1 file.")));
            if (dm != curKey)
            {
                var preTxt = string.Join("; ", pre.Select(p => MonL(p.MKey) + ": sale " + N(p.Sale) + (p.Counted != p.Days ? " (" + Num(p.Counted) + " days left)" : "") + (JsOps.Truthy(p.Po) ? ", POs +" + N(p.Po) : "")));
                x.Steps.Add(Step(s++, T("From step 1, take off the plan sale until " + LastDay(pre.Count > 0 ? pre[^1].MKey : curKey) + " (F4 plan × colour mix " + Pc(c.Mix, 1) +
                    ", days left only this month) and add POs landing by then" + (preTxt.Length > 0 ? " — " + preTxt : "") + ": stock on 1 " + MonL(dm) + " "), B(N(ch0?.OpenStock ?? double.NaN)), T(".")));
            }
            var poTxt = "add " + MonL(dm) + " POs (F2) and quantities approved today (" + N(n.Po0) + ")";
            var left = ch0 != null && JsOps.Truthy(ch0.DemandDays) && JsOps.Truthy(ch0.TotalDays) && ch0.DemandDays != ch0.TotalDays ? " (" + Num(ch0.DemandDays) + " days left)" : "";
            if (c.RequestQty != null)
            {
                x.Steps.Add(Step(s, T("From step " + (s - 1) + ", take off " + MonL(dm) + "'s sale " + N(ch0?.ColPlan ?? double.NaN) + left + " (plan × " + Pc(c.Mix, 1) + ") and " + poTxt +
                    ": stock on 1 " + MonL(nextKey) + " without this request "), B(N(n.StartNoProp)), T((n.StartNoProp < 0 ? " (below 0: already short by this much)" : "") + " — the window starts here.")));
                s++;
            }
            else
            {
                x.Steps.Add(Step(s, T("From step " + (s - 1) + ", take off " + MonL(dm) + "'s sale " + N(ch0?.ColPlan ?? double.NaN) + left + " (plan × " + Pc(c.Mix, 1) + ") and " + poTxt +
                    ": stock on 1 " + MonL(nextKey) + " "), B(N(n.WStart)), T(" — the window starts here (never below 0).")));
                s++;
            }
            var wEnd = n.WRows.Count > 0 ? n.WRows[^1] : null;
            x.Steps.Add(Step(s++, T("Window 1 " + MonL(nextKey) + " – " + (wEnd != null ? Num(wEnd.Take) + " " + MonL(wEnd.MKey) : "") + " (" + Num(c.D) + " days): " +
                string.Join(" + ", n.WRows.Select(w => w.Take == w.Days ? MonShort(w.MKey) : Num(w.Take) + "/" + Num(w.Days) + " of " + MonShort(w.MKey))) +
                " plan × " + Pc(c.Mix, 1) + " = "), B(N(n.WDem) + " pcs"), T(" → "), B(D1(n.Rate) + " a day"), T(".")));
            var sN = s;
            x.Steps.Add(Step(s++, T("Limit " + Num(c.T1) + " days + " + Num(c.Buf) + "% buffer = "), B(D2(n.Eff) + " days"), T(". Stock needed on 1 " + MonL(nextKey) + " = " + D2(n.Eff) + " × " + D1(n.Rate) + " = "), B(N(n.Need)), T(".")));
            if (c.RequestQty != null)
            {
                var q = c.RequestQty.Value;
                double? cover = n.Rate > 0 ? Math.Max(0, ch1?.OpenStock ?? 0) / n.WDem * c.D : null;
                x.Steps.Add(Step(s++, T("Max Permissible = step " + sN + " − step " + (sN - 2) + " = " + N(n.Need) + " − " + N(n.StartNoProp) + " = "), B(N(n.Can)), T(" (rounded down, never below 0).")));
                x.Result = new List<ExplainSegDto> { T("Asking "), B(N(q)), T(" gives about "), B((cover != null ? Num(Js.Round(cover.Value)) : "—") + " days"),
                    T(" of cover in the window (with the asked quantity added on " + MonL(dm) + "). ") };
                if (q <= n.Can) x.Result.Add(T("That is within the limit, so it is allowed."));
                else { x.Result.Add(T("That is above the " + D2(n.Eff) + "-day limit, so the tool allows up to ")); x.Result.Add(B(N(n.Can))); x.Result.Add(T(" (Confirm Final Quantity).")); }
            }
            else
            {
                x.Steps.Add(Step(s++, T("Can raise = step " + sN + " − step " + (sN - 2) + " = " + N(n.Need) + " − " + N(n.WStart) + " = "), B(N(n.Can)), T(" (rounded down, never below 0).")));
                x.Result = n.Can > 0
                    ? new List<ExplainSegDto> { B("Max Permissible " + N(n.Can) + "."), T(" A New PO request up to this is allowed.") }
                    : new List<ExplainSegDto> { B("Hold."), T(" Stock and POs already cover " + D2(n.Eff) + " days; nothing more can be raised for " + MonL(dm) + ".") };
            }
            if (c.Rec != null && Math.Abs(c.Rec.Value - n.Can) > 0)
                x.Warn = "The table shows " + N(c.Rec) + "; the steps give " + N(n.Can) + ". The data changed since the table was worked out — reopen the screen.";
            x.MixTitle = "Where the " + Pc(c.Mix, 1) + " comes from";
            x.MixText = c.Colour + " sold " + Pc(c.S45Share ?? c.Mix, 1) + " of the group's last-45-day sale (F3). " + (c.MixNote.Length > 0 ? c.MixNote : "The last 45 days are used for every month.");
            x.TableHead = new List<string> { "Month", "Open", "POs in", "Plan sale", "Close" };
            var lastK = n.WRows.Count > 0 ? n.WRows[^1].K : 1;
            x.TableRows = pre.Select(p => new List<string> { MonL(p.MKey), N(p.Open), N(p.Po), N(p.Sale), N(p.Close) })
                .Concat(ch.Take(lastK + 1).Select(m => new List<string> { MonL(m.MKey), N(m.OpenStock), N(m.Po), N(m.ColPlan), N(m.CloseStock) })).ToList();
            x.Title = c.Colour + " · delivery " + MonL(dm) + " · " + (c.RequestQty != null ? "Max Permissible " + N(n.Can) : (n.Can > 0 ? "can raise " + N(n.Can) : "Hold"));
            x.Book = SingleBook(c);
            return x;
        }

        private static List<ExplainSegDto> WatchResult() => new()
        {
            B("Watch List."), T(" A New PO request for this colour goes to the Category Head for approval, with the buyer's reason (“Why introduce this colour?”).")
        };

        // ── New PO request (requestCtx / openForRequest / addLink) ─────────

        /// <summary>
        /// The panel of a single concept New PO request card, or null when the card has no "How was this number worked
        /// out?" link (explain.js <c>addLink</c>: engine B with a month-by-month roll or a failed colour mix).
        /// </summary>
        public static SingleExplainDto? ForRequest(Tool1Outcome r, EngineParams p, string today)
        {
            var b = r.B;
            if (r.Engine != "B" || b == null || !(b.Chain.Count > 0 || b.MixFailed)) return null;
            var c = RequestCtx(r, b, p, today);
            if (b.MixFailed) { c.Watch = true; return Single(c); }
            if (r.Decision == Decisions.WatchList)   // v1.1.6 D3: no sale → no quantity steps
                return new SingleExplainDto { Title = r.Gm + " " + r.Colour + " · Watch List", WarnFirst = r.DecisionReason, Result = WatchResult() };
            return Single(c);
        }

        private static Ctx RequestCtx(Tool1Outcome r, Tool1EngineBResult b, EngineParams p, string today)
        {
            var i = r.Info;
            return new Ctx
            {
                Colour = (r.Colour ?? "").ToUpperInvariant(), Month = r.Month, Stock = b.OpStock,
                Mix = b.EffMix ?? b.MixPct, S45Share = b.MixPct, MixNote = "",
                T1 = JsOps.Or(p.T1, 75), Buf = JsOps.Or(p.StrBuffer, 0), D = JsOps.Or(p.D, 75), MixT = JsOps.Or(p.Mix, 2),
                Rec = b.MaxPermQty, Pre = b.PreRoll ?? new(),
                Chain = b.Chain.Select(x => new ChainMonth(x.Y, x.M, x.MKey, x.OpenStock, x.EPO, x.ColPlan, x.CloseStock, x.Fc, x.DemandDays, x.TotalDays)).ToList(),
                RequestQty = r.Qty, Group = GroupText(i?.Brand, i?.McDesc, i?.McCd, i?.Mrp, i?.Flag), Today = today
            };
        }

        // ── single concept Recommendation (recoCtx / hookPaint / recoAll) ──

        /// <summary>
        /// explain.js <c>recoCtx(r, md, info)</c>: the context of one colour × month of the single concept Recommendation.
        /// <paramref name="group"/> = <see cref="GroupText"/> of the bucket.
        /// </summary>
        public static Ctx RecoCtx(RecBRowDto row, RecBMonthDto md, EngineParams p, string group, string today)
        {
            var cs = md.Cs;
            return new Ctx
            {
                Colour = (row.Col ?? "").ToUpperInvariant(), Month = md.Mon, Stock = row.Stk, Mix = md.MixUsed ?? row.MixRatio, S45Share = row.MixRatio, MixNote = "",
                T1 = JsOps.Or(p.T1, 75), Buf = JsOps.Or(p.StrBuffer, 0), D = cs != null ? JsOps.Or(cs.WinDays, JsOps.Or(p.D, 75)) : JsOps.Or(p.D, 75), MixT = JsOps.Or(p.Mix, 2),
                Watch = md.Action == "Watch List", Rec = md.Rec, Pre = cs?.Pre ?? new(),
                Chain = (cs?.Chain ?? new()).Select(x => new ChainMonth(x.Y, x.M, x.MKey, x.OpenStock, x.PoIn, x.ColPlan, x.CloseStock, x.Fc, x.DemandDays, x.TotalDays)).ToList(),
                RequestQty = null, Group = group, Today = today
            };
        }

        /// <summary>The panel of one quantity / Hold / Watch List cell of the single concept Recommendation (explain.js <c>hookPaint</c> click).</summary>
        public static SingleExplainDto ForRecommendation(RecBRowDto row, RecBMonthDto md, EngineParams p, string group, string today)
        {
            if (md.Action == "No data")
                return new SingleExplainDto { Title = (row.Col ?? "").ToUpperInvariant() + " · " + MonL(md.Mon), Sub = group, Warn = "No F4 plan for this group from " + MonL(md.Mon) + ", so no quantity can be worked out." };
            return Single(RecoCtx(row, md, p, group, today));
        }

        /// <summary>explain.js <c>recoAll</c>: "Download Excel (all lines)" — every colour × month except "No data".</summary>
        public static ExplainBookDto RecommendationAll(RecBResultDto res, EngineParams p, string group, string mcCd, string today)
        {
            var lines = new List<Ctx>();
            foreach (var r in res.Rows)
                foreach (var md in r.MData)
                    if (md.Action != "No data") lines.Add(RecoCtx(r, md, p, group, today));
            var meta = (Group: group, D: JsOps.Or(p.D, 75), T1: JsOps.Or(p.T1, 75), Buf: JsOps.Or(p.StrBuffer, 0), MixT: JsOps.Or(p.Mix, 2));
            var b = new BookBuilder();
            var sh = b.Sheet("All lines", "Colour Recommendation — Single Concept · " + meta.Group,
                "Window " + Num(meta.D) + " days from the 1st of the month after delivery · limit " + Num(meta.T1) + " days + " + Num(meta.Buf) + "%",
                "Stock on window start and window sale come from the month-by-month roll (download \"this calculation\" from a colour for that roll with formulas).",
                ("c", "Colour", 9, null), ("m", "Delivery month", 10, null), ("stk", "Stock today", 11, "int"), ("mix", "Colour mix used", 10, "pct"),
                ("ws", "Stock on window start", 13, "d1"), ("wd", "Window sale (D days)", 13, "d1"), ("D", "D (days)", 7, null), ("rate", "Daily rate", 10, "d2"),
                ("lim", "Limit days (T1 + buffer)", 11, "d2"), ("need", "Stock needed", 12, "d1"), ("mp", "Max Permissible", 12, "int"), ("res", "Result", 12, null));
            for (var i = 0; i < lines.Count; i++)
            {
                var L = lines[i];
                var id = "r" + i;
                if (L.Watch)
                {
                    sh.Row(id, null, ("c", BookBuilder.V(L.Colour)), ("m", BookBuilder.V(MonL(L.Month))), ("stk", BookBuilder.In(L.Stock, "int")), ("mix", BookBuilder.In(L.Mix, "pct")),
                        ("res", BookBuilder.V("Watch List (mix under " + Num(meta.MixT) + "%)")));
                    continue;
                }
                var n = SingleNumbers(L);
                sh.Row(id, null, ("c", BookBuilder.V(L.Colour)), ("m", BookBuilder.V(MonL(L.Month))), ("stk", BookBuilder.In(L.Stock, "int")), ("mix", BookBuilder.In(L.Mix, "pct")),
                    ("ws", BookBuilder.In(n.WStart, "d1")), ("wd", BookBuilder.In(n.WDem, "d1")), ("D", BookBuilder.In(L.D)),
                    ("rate", BookBuilder.F("IF({" + id + ".D}>0,{" + id + ".wd}/{" + id + ".D},0)", n.Rate)), ("lim", BookBuilder.In(n.Eff, "d2")),
                    ("need", BookBuilder.F("{" + id + ".lim}*{" + id + ".rate}", n.Need)),
                    ("mp", BookBuilder.F("MAX(0,ROUNDDOWN({" + id + ".need}-{" + id + ".ws},0))", n.Can)),
                    ("res", BookBuilder.F("IF({" + id + ".mp}>0,\"Raise\",\"Hold\")", n.Can > 0 ? "Raise" : "Hold")));
            }
            return b.Build("Recommendation_single_" + mcCd + "_all_lines.xlsx");
        }

        // ── singleBook ("this calculation") ─────────────────────────────────

        /// <summary>Port of <c>Explain.singleBook(c)</c> → Steps, Inputs, Month by month (formulas restate the tool's arithmetic).</summary>
        public static ExplainBookDto SingleBook(Ctx c)
        {
            var n = SingleNumbers(c);
            var pre = c.Pre;
            var ch = c.Chain;
            var lastK = n.WRows.Count > 0 ? n.WRows[^1].K : 1;
            var b = new BookBuilder();
            var st = b.Sheet("Steps", c.Colour + " · delivery " + MonL(c.Month) + " · " + (c.RequestQty != null ? "New PO request" : "Recommendation"), c.Group, null,
                ("no", "Step", 6, null), ("what", "What", 46, null), ("how", "How (in words)", 60, null), ("v", "Value", 14, "int"));
            var inputs = b.Sheet("Inputs", c.Colour + " · " + c.Group + " · inputs", null, null,
                ("a", "Input", 40, null), ("v", "Value", 14, "d2"), ("w", "Where it comes from", 60, null));
            inputs.Row("stk", null, ("a", BookBuilder.V("Stock today (all stores)")), ("v", BookBuilder.In(c.Stock, "int")), ("w", BookBuilder.V("F1 file")));
            inputs.Row("mix", null, ("a", BookBuilder.V("Colour mix used")), ("v", BookBuilder.In(c.Mix, "pct")), ("w", BookBuilder.V(c.MixNote.Length > 0 ? c.MixNote : "Share of the last-45-day sale (F3)")));
            inputs.Row("t1", null, ("a", BookBuilder.V("Limit T1 (days)")), ("v", BookBuilder.In(c.T1)), ("w", BookBuilder.V("Settings")));
            inputs.Row("buf", null, ("a", BookBuilder.V("Buffer %")), ("v", BookBuilder.In(c.Buf)), ("w", BookBuilder.V("Settings")));
            inputs.Row("D", null, ("a", BookBuilder.V("Window D (days)")), ("v", BookBuilder.In(c.D)), ("w", BookBuilder.V("Settings: days from the 1st of the month after delivery")));
            if (c.RequestQty != null)
                inputs.Row("q", null, ("a", BookBuilder.V("Asked quantity")), ("v", BookBuilder.In(c.RequestQty.Value, "int")), ("w", BookBuilder.V("New PO request")));

            var mm = b.Sheet("Month by month", "Stock rolled month by month (plan sale = F4 × mix × days counted ÷ days in month)", null, null,
                ("m", "Month", 10, null), ("fc", "F4 plan (group)", 14, "int"), ("days", "Days in month", 9, null), ("cnt", "Days counted", 9, null),
                ("sale", "Plan sale", 12, "d1"), ("po", "POs in (F2 + approved today)", 14, "int"), ("ask", "Asked qty", 10, "int"),
                ("open", "Open stock", 12, "d1"), ("close", "Close stock", 12, "d1"), ("take", "Days in window", 9, null), ("win", "Window sale", 12, "d1"));
            string? prevId = null;
            for (var i = 0; i < pre.Count; i++)
            {
                var p = pre[i];
                var id = "p" + i;
                mm.Row(id, null, ("m", BookBuilder.V(MonL(p.MKey))), ("fc", BookBuilder.In(p.Fc, "int")), ("days", BookBuilder.In(p.Days)), ("cnt", BookBuilder.In(p.Counted)),
                    ("sale", BookBuilder.F("{" + id + ".fc}*{Inputs!mix.v}*{" + id + ".cnt}/{" + id + ".days}", p.Sale)), ("po", BookBuilder.In(p.Po, "int")),
                    ("open", prevId != null ? BookBuilder.F("{" + prevId + ".close}", p.Open) : BookBuilder.L("{Inputs!stk.v}", p.Open)),
                    ("close", BookBuilder.F("MAX(0,{" + id + ".open}+{" + id + ".po}-{" + id + ".sale})", p.Close)));
                prevId = id;
            }
            for (var k = 0; k <= Math.Min(lastK, ch.Count - 1); k++)
            {
                var x = ch[k];
                var id = "c" + k;
                var w = n.WRows.FirstOrDefault(r => r.K == k);
                var ask = k == 0 && c.RequestQty != null ? c.RequestQty.Value : 0;
                var cells = new List<(string, ExplainCellDto?)>
                {
                    ("m", BookBuilder.V(MonL(x.MKey))), ("fc", BookBuilder.In(x.Fc, "int")),
                    ("days", BookBuilder.In(JsOps.Or(x.TotalDays, MonthMath.DaysInMonth(x.Y, x.M)))), ("cnt", BookBuilder.In(JsOps.Or(x.DemandDays, MonthMath.DaysInMonth(x.Y, x.M)))),
                    ("sale", BookBuilder.F("{" + id + ".fc}*{Inputs!mix.v}*{" + id + ".cnt}/{" + id + ".days}", x.ColPlan)), ("po", BookBuilder.In(x.Po, "int")),
                    ("open", prevId != null ? BookBuilder.F("{" + prevId + ".close}", x.OpenStock) : BookBuilder.L("{Inputs!stk.v}", x.OpenStock)),
                    ("close", BookBuilder.F("MAX(0,{" + id + ".open}+{" + id + ".po}" + (ask != 0 ? "+{" + id + ".ask}" : "") + "-{" + id + ".sale})", x.CloseStock))
                };
                if (ask != 0) cells.Add(("ask", BookBuilder.L("{Inputs!q.v}", ask)));
                if (w != null) { cells.Add(("take", BookBuilder.In(w.Take))); cells.Add(("win", BookBuilder.F("{" + id + ".sale}*{" + id + ".take}/{" + id + ".days}", w.Sale))); }
                mm.Row(id, null, cells.ToArray());
                prevId = id;
            }
            const string w0 = "c1";
            var wl = "c" + Math.Min(lastK, ch.Count - 1);
            st.Row("s1", null, ("no", BookBuilder.V(1)), ("what", BookBuilder.V("Stock today (F1)")), ("how", BookBuilder.V("From the Inputs sheet")), ("v", BookBuilder.L("{Inputs!stk.v}", c.Stock)));
            if (c.RequestQty != null)
                st.Row("s3", null, ("no", BookBuilder.V(2)), ("what", BookBuilder.V("Stock on the window start without this request")),
                    ("how", BookBuilder.V(MonL(c.Month) + ": open + POs − plan sale (can be below 0)")),
                    ("v", BookBuilder.F("{Month by month!c0.open}+{Month by month!c0.po}-{Month by month!c0.sale}", n.StartNoProp, "d1")));
            else
                st.Row("s3", null, ("no", BookBuilder.V(2)), ("what", BookBuilder.V("Stock on the window start (1 " + MonL(ch.Count > 1 ? ch[1].MKey : "") + ")")),
                    ("how", BookBuilder.V("Close stock of " + MonL(c.Month) + " (Month by month sheet)")), ("v", BookBuilder.L("{Month by month!c0.close}", n.WStart, "d1")));
            st.Row("s4", null, ("no", BookBuilder.V(3)), ("what", BookBuilder.V("Window sale (" + Num(c.D) + " days)")), ("how", BookBuilder.V("Sum of the window sale column")),
                ("v", BookBuilder.F("SUM({Month by month!" + w0 + ".win:Month by month!" + wl + ".win})", n.WDem, "d1")));
            st.Row("s5", null, ("no", BookBuilder.V(4)), ("what", BookBuilder.V("Daily rate in the window")), ("how", BookBuilder.V("Window sale ÷ D")), ("v", BookBuilder.F("{s4.v}/{Inputs!D.v}", n.Rate, "d2")));
            st.Row("s6", null, ("no", BookBuilder.V(5)), ("what", BookBuilder.V("Limit in days")), ("how", BookBuilder.V("T1 × (1 + buffer ÷ 100)")), ("v", BookBuilder.F("{Inputs!t1.v}*(1+{Inputs!buf.v}/100)", n.Eff, "d2")));
            st.Row("s7", null, ("no", BookBuilder.V(6)), ("what", BookBuilder.V("Stock needed on the window start")), ("how", BookBuilder.V("Limit days × daily rate")), ("v", BookBuilder.F("{s6.v}*{s5.v}", n.Need, "d1")));
            st.Row("s8", "total", ("no", BookBuilder.V(7)), ("what", BookBuilder.V(c.RequestQty != null ? "Max Permissible" : "Can raise (Max Permissible)")),
                ("how", BookBuilder.V("Stock needed − stock on the window start, rounded down, never below 0")), ("v", BookBuilder.F("MAX(0,ROUNDDOWN({s7.v}-{s3.v},0))", n.Can, "int", "bold")));
            return b.Build(Regex.Replace("Calculation_" + c.Colour + "_" + (c.Month ?? "").Replace(".", "-") + ".xlsx", "[\\\\/:*?\"<>|]", "_"));
        }

        // ── Explain.book: sheets with {rowId.colKey} references resolved to A1 ──

        /// <summary>
        /// Port of <c>Explain.book</c>'s layout and reference resolution: rows 1–3 title / who-when / blank, row 4 header,
        /// data from row 5. A formula may use <c>{rowId.colKey}</c> (same sheet), <c>{Sheet!rowId.colKey}</c> and
        /// <c>{a:b}</c> for a range.
        /// </summary>
        public sealed class BookBuilder
        {
            private static readonly Regex Ref = new(@"\{([^{}]+)\}", RegexOptions.Compiled);
            private readonly List<SheetBuilder> _sheets = new();

            public sealed class SheetBuilder
            {
                internal readonly ExplainSheetDto Dto;
                internal readonly List<(string? Id, string? Style, (string Key, ExplainCellDto? Cell)[] Cells)> Rows = new();
                internal SheetBuilder(ExplainSheetDto dto) => Dto = dto;
                public void Row(string? id, string? style, params (string Key, ExplainCellDto? Cell)[] cells) => Rows.Add((id, style, cells));
            }

            public SheetBuilder Sheet(string name, string title, string? subtitle, string? note, params (string Key, string Label, double Width, string? Fmt)[] cols)
            {
                var sh = new SheetBuilder(new ExplainSheetDto
                {
                    Name = Regex.Replace(name, @"[\\/?*\[\]:]", " ") is var nm && nm.Length > 31 ? nm[..31] : nm,
                    Title = title, Subtitle = subtitle, Note = note,
                    Cols = cols.Select(c => new ExplainColDto { Key = c.Key, Label = c.Label, Width = c.Width, Fmt = c.Fmt }).ToList()
                });
                _sheets.Add(sh);
                return sh;
            }

            public static ExplainCellDto V(object? v) => new() { Value = v is int i ? (double)i : v };
            public static ExplainCellDto In(double v, string? fmt = null) => new() { Value = v, Style = "in", Fmt = fmt };
            public static ExplainCellDto F(string f, object? v, string? fmt = null, string? style = null) => new() { Formula = f, Value = v, Fmt = fmt, Style = style };
            public static ExplainCellDto L(string f, double v, string? fmt = null) => new() { Formula = f, Value = v, Fmt = fmt, Style = "link" };

            private static string ColL(int i)
            {
                var s = ""; i++;
                while (i > 0) { var m = (i - 1) % 26; s = (char)('A' + m) + s; i = (i - 1) / 26; }
                return s;
            }

            public ExplainBookDto Build(string fileName)
            {
                var addr = new Dictionary<string, int>(StringComparer.Ordinal);
                foreach (var sh in _sheets)
                    for (var i = 0; i < sh.Rows.Count; i++)
                        if (sh.Rows[i].Id != null) addr[sh.Dto.Name + "!" + sh.Rows[i].Id] = 5 + i;
                string Resolve(string f, SheetBuilder sh) => Ref.Replace(f, m =>
                {
                    var parts = m.Groups[1].Value.Split(':').Select(one =>
                    {
                        var sn = sh.Dto.Name; var k = one;
                        var bang = one.IndexOf('!');
                        if (bang >= 0) { sn = one[..bang]; k = one[(bang + 1)..]; }
                        var dot = k.LastIndexOf('.');
                        var rid = k[..dot]; var key = k[(dot + 1)..];
                        var tsh = _sheets.FirstOrDefault(s => s.Dto.Name == sn);
                        var ci = tsh?.Dto.Cols.FindIndex(c => c.Key == key) ?? -1;
                        if (!addr.TryGetValue(sn + "!" + rid, out var row) || ci < 0) throw new InvalidOperationException("Excel formula reference not found: " + one);
                        return (Sn: sn, A: ColL(ci) + row.ToString(CultureInfo.InvariantCulture));
                    }).ToList();
                    var pre = parts[0].Sn != sh.Dto.Name ? "'" + parts[0].Sn + "'!" : "";
                    return pre + string.Join(":", parts.Select(p => p.A));
                });
                var book = new ExplainBookDto { FileName = fileName };
                foreach (var sh in _sheets)
                {
                    foreach (var (id, style, cells) in sh.Rows)
                    {
                        var row = new ExplainRowDto { Id = id, Style = style };
                        foreach (var col in sh.Dto.Cols)
                        {
                            var cell = cells.FirstOrDefault(c => c.Key == col.Key).Cell;
                            if (cell?.Formula != null) cell.Formula = Resolve(cell.Formula, sh).TrimStart('=');
                            if (cell?.Value is double d && !double.IsFinite(d)) cell.Value = null;
                            row.Cells.Add(cell);
                        }
                        sh.Dto.Rows.Add(row);
                    }
                    book.Sheets.Add(sh.Dto);
                }
                return book;
            }
        }
    }
}
