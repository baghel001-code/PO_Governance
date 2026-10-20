using DecisionEngine.Common;
using DecisionEngine.Model;
using DecisionEngine.Results.Tool1;

namespace DecisionEngine.Tool1
{
    /// <summary>
    /// Port of index.html <c>renderResult(r)</c> (line 4543) as data: produces every figure and text
    /// the Tool 1 result card paints. Step 5's formula follows v1.1.4 README item 2 (exact effective
    /// threshold and the engine's own stock before this PO); everything else is as the JS painted it.
    /// </summary>
    public static class Tool1CardBuilder
    {
        private const string Red = "#DC2626", Green = "#00873E";

        public static readonly IReadOnlyDictionary<string, (string C, string Bg, string Bd)> DecisionColours =
            new Dictionary<string, (string, string, string)>
            {
                [Decisions.AutoAllow] = ("#00873E", "#DCFCE7", "#86EFAC"),
                [Decisions.Allow] = ("#00873E", "#DCFCE7", "#86EFAC"),
                [Decisions.Block] = ("#DC2626", "#FEF2F2", "#FECACA"),
                [Decisions.Hold] = ("#DC2626", "#FEF2F2", "#FECACA"),
                [Decisions.ReduceRecommended] = ("#D97706", "#FFFBEB", "#FDE68A"),
                [Decisions.WatchList] = ("#854F0B", "#FFF3CD", "#FCD34D"),
                [Decisions.CannotValidate] = ("#6B7280", "#F5F5F5", "#D1D5DB")
            };

        /// <summary>dpill class of a decision (Today's Requests table).</summary>
        public static string PillClass(string d) => d switch
        {
            Decisions.AutoAllow or Decisions.Allow => "dp-allow",
            Decisions.ReduceRecommended => "dp-reduce",
            Decisions.WatchList => "dp-watch",
            Decisions.Hold => "dp-hold",
            Decisions.Block => "dp-block",
            _ => "dp-nodata"
        };

        private static string N(double v) => Js.NumberToString(v);
        private static string Or(string? s, string d) => string.IsNullOrEmpty(s) ? d : s;

        public static Tool1CardDto Build(Tool1Outcome r, EngineParams p, string today)
        {
            var (dc, db, dbrd) = DecisionColours.TryGetValue(r.Decision, out var c) ? c : ("#6B7280", "#F5F5F5", "#D1D5DB");
            var card = new Tool1CardDto
            {
                Id = r.Id, Gm = r.Gm, Colour = r.Colour, Decision = r.Decision, DecisionColour = dc, DecisionBg = db, DecisionBorder = dbrd,
                EngineLabel = r.Engine == "B" ? "Single Concept MRP · Colour STR" : r.Engine == "A" ? "Multi Concept MRP · Peer BSR" : "New Product",
                EngineBg = r.Engine == "B" ? "#E0F6FF" : r.Engine == "A" ? "#EAF0FB" : "#F5F3FF",
                EngineColour = r.Engine == "B" ? "#006B99" : r.Engine == "A" ? "#1A4FAF" : "#7C3AED",
                OptionTypeChip = !string.IsNullOrEmpty(r.OptionType) && r.OptionType != "—" ? r.OptionType : null
            };
            var i = r.Info;
            card.SubLine = i != null ? Or(i.Brand, "NON-PL") + " · " + Or(i.Div, "—") + " · " + Or(i.McDesc, "—") + " · ₹" + Or(i.Mrp, "—") + " · " + Or(i.Flag, "—") : "";
            card.Chips.Add(new() { Key = "Brand Type", Value = Or(i?.Brand, "—") });
            card.Chips.Add(new() { Key = "Division", Value = Or(i?.Div, "—") });
            card.Chips.Add(new() { Key = "MC", Value = Or(i?.McDesc, "—") });
            card.Chips.Add(new() { Key = "MRP", Value = "₹" + Or(i?.Mrp, "—") });
            card.Chips.Add(new() { Key = "Flag", Value = Or(i?.Flag, "—") });
            card.Chips.Add(new() { Key = "Del. Month", Value = r.Month });
            card.Chips.Add(new() { Key = "Proposed Qty", Value = Js.Fmt(r.Qty) });
            if (r.FinalApprovedQty != null) card.Chips.Add(new() { Key = "Raised Qty", Value = Js.Fmt(r.FinalApprovedQty.Value), Colour = "#E8006E" });

            if (r.Engine == "B" && r.B != null) card.B = BuildB(r, r.B, p, today);
            if (r.Engine == "A" && r.A != null) card.A = BuildA(r.A, p, dc, r.Decision);

            card.ReasonTitle = r.Decision + " — Reason";
            card.Reason = Or(r.DecisionReason, "—");
            card.StrCaution = r.StrCaution;
            card.RecEngine = r.Engine;
            card.FooterHint = r.Engine == "B" ? "📦 Colour-level STR · monthly qty recommendation" : "🎯 Attribute → Colour → GM recommendation";
            card.RecInfo = new Tool1RecInfoDto
            {
                McCd = i?.McCd ?? "", Mrp = i?.Mrp ?? "", Brand = i?.Brand ?? "", Flag = i?.Flag ?? "", Div = i?.Div ?? "", McDesc = i?.McDesc ?? ""
            };
            return card;
        }

        private static Tool1CardBDto BuildB(Tool1Outcome r, Tool1EngineBResult b, EngineParams p, string today)
        {
            var dto = new Tool1CardBDto();
            if (b.MixFailed)
            {
                dto.MixFailed = true;
                dto.MixBanner = "⚠ Colour Mix % = " + Js.ToFixed(b.MixPct * 100, 1) + "% — below " + N(p.Mix) + "% threshold. Broken / cut size likely.";
                return dto;
            }
            var buf = JsOps.Or(p.StrBuffer, 0);
            var effT = p.T1 * (1 + buf / 100);
            var effR = N(Js.Round(effT));
            var art = b.ArtSTR;
            bool Breach(double? v) => v != null && v > effT;

            string maxPerm;
            if (b.MaxPermQty != null && b.MaxPermQty > 0) maxPerm = Js.Fmt(b.MaxPermQty.Value);
            else if (b.MaxPermQty == 0 && b.HasBreach == true) maxPerm = "0";
            else maxPerm = b.MaxPermQty != null ? Js.Fmt(b.MaxPermQty.Value) : "—";
            dto.Kpis.Add(new() { Key = "Op Stock", Value = Js.Fmt(b.OpStock), Colour = "#1A4FAF" });
            dto.Kpis.Add(new() { Key = "Mix %", Value = Js.ToFixed(b.MixPct * 100, 1) + "%", Colour = Green });
            dto.Kpis.Add(new() { Key = "Daily Rate", Value = JsOps.Truthy(b.WindowDailyRate) ? Js.ToFixed(b.WindowDailyRate!.Value, 1) + "/day" : "—", Colour = "#0891B2" });
            dto.Kpis.Add(new() { Key = "Colour STR", Value = art != null ? N(Js.Round(art.Value)) + "d" : "—", Colour = Breach(art) ? Red : Green });
            dto.Kpis.Add(new() { Key = "Max Permissible", Value = maxPerm, Colour = "#E8006E" });
            if (r.FinalApprovedQty != null && b.MaxPermQty != null)
                dto.Kpis.Add(new() { Key = "Max Permissible (After Raising PO)", Value = Js.Fmt(Tool1EngineB.JsMathMax(0, b.MaxPermQty.Value - r.FinalApprovedQty.Value)), Colour = "#7C3AED" });

            // Step 1
            dto.Step1.OpStock = b.Chain.Count > 0 ? Js.Fmt(b.OpStock) + " units" : null;
            dto.Step1.Key3 = r.Info != null ? r.Info.Brand + "-" + r.Info.McCd + "-" + r.Info.Mrp + "-" + r.Info.Flag + "-" + r.Colour : "—";

            // Step 2
            var daily = b.HasDailyF3 == true;
            dto.Step2.Basis = daily ? "across Day 1 to Day " + N(JsOps.Or(p.MixWindowDays, 45)) + " in F3 (sum of all day-columns)" : "over last N=" + N(p.N) + " months from F3";
            dto.Step2.Per = daily ? "day" : "month";
            dto.Step2.ColAvg = Js.Fmt(Js.Round(JsOps.Or(b.ColAvg ?? 0, 0)));
            dto.Step2.McAvg = Js.Fmt(Js.Round(JsOps.Or(b.McAvg ?? 0, 0)));
            dto.Step2.MixPct = Js.ToFixed(b.MixPct * 100, 1) + "%";
            dto.Step2.ThresholdOk = b.MixPct * 100 >= p.Mix;
            dto.Step2.Threshold = N(p.Mix) + "% " + (dto.Step2.ThresholdOk ? "✓ OK" : "✗ FAIL");

            // Step 3
            dto.Step3.ProposedQty = Js.Fmt(r.Qty);
            if (b.Chain.Count > 0)
            {
                var (cY, cM, tD) = MonthMath.ParseIsoDate(today);
                var preStock = b.OpStock;
                var fbm = b.ForecastByMon;
                var epo = ToMap(b.ExistingPO);
                var carry = b.PrevMonthCarry == null ? null : new CarryOver(b.PrevMonthCarry.Qty, b.PrevMonthCarry.Cutoff, b.PrevMonthCarry.PrevKey, b.PrevMonthCarry.CurrKey);
                for (var k = 0; ; k++)
                {
                    var (cy, cm) = MonthMath.AddMonths(cY, cM, k);
                    var mkey = MonthMath.Mmyyyy(cy, cm);
                    if (mkey == r.Month) break;
                    if (k > 24) break;
                    double totalD = MonthMath.DaysInMonth(cy, cm);
                    var remD = k == 0 ? totalD - tD : totalD;
                    var isCurr = k == 0;
                    var fcVal = fbm != null && fbm.TryGetValue(mkey, out var fv) ? JsOps.Or(fv, 0) : 0;
                    var ePO = PrevMonthCarry.EPOWithCarry(epo, mkey, carry);
                    var cp = b.MixPct > 0 ? fcVal * b.MixPct * (remD / totalD) : 0;
                    var closeS = Tool1EngineB.JsMax0(preStock + ePO - cp);
                    dto.Step3.Rows.Add(new Tool1StockRowDto
                    {
                        PreRoll = true, Month = MonthMath.MonLabel(cy, cm), Tag = isCurr ? "(current month)" : null, Bold = isCurr,
                        Background = isCurr ? "#FFF3CD" : "#fff", OpenStock = Js.Fmt(Js.Round(preStock)),
                        ExistingPo = ePO > 0 ? "+" + Js.Fmt(ePO) : "—", Proposed = "—", Demand = "−" + Js.Fmt(Js.Round(cp)),
                        DemandNote = isCurr ? "(" + N(remD) + "/" + N(totalD) + "d)" : null, CloseStock = Js.Fmt(Js.Round(closeS)),
                        Note = isCurr ? "Remaining " + N(remD) + " days only" : "Full month", NoteColour = "#6B7280"
                    });
                    preStock = closeS;
                }
                foreach (var ch in b.Chain.Take(6))
                {
                    var strTxt = ch.Str != null ? N(Js.Round(ch.Str.Value)) + "d" : "—";
                    dto.Step3.Rows.Add(new Tool1StockRowDto
                    {
                        Month = Or(ch.Label, ch.MKey), Tag = ch.IsDM ? "← Delivery Month" : ch.IsD1 ? "← Window Start" : null, Bold = ch.IsDM,
                        Background = ch.IsDM ? "#FFF0F7" : ch.IsD1 ? "#EAF0FB" : "#fff", OpenStock = Js.Fmt(Js.Round(ch.OpenStock)),
                        ExistingPo = ch.EPO > 0 ? "+" + Js.Fmt(ch.EPO) : "—", Proposed = ch.Proposed > 0 ? "+" + Js.Fmt(ch.Proposed) : "—",
                        Demand = "−" + Js.Fmt(Js.Round(ch.ColPlan)),
                        DemandNote = ch.IsCurrMonth ? "(" + N(ch.DemandDays) + "/" + N(ch.TotalDays) + "d remaining)" : null,
                        CloseStock = Js.Fmt(Js.Round(ch.CloseStock)),
                        Note = ch.IsCurrMonth ? "Rem. " + N(ch.DemandDays) + "d only" : ch.Str != null ? "STR=" + strTxt : ch.IsDM ? "DM" : "—",
                        NoteColour = Breach(ch.Str) ? Red : Green
                    });
                }
            }

            // Step 4
            var D = p.D;
            dto.Step4.D = N(D);
            if (b.Chain.Count > 1)
            {
                double budget2 = D, totalDem = 0;
                var d1 = b.Chain.FindIndex(x => x.IsD1);
                var startIdx = d1 >= 0 ? d1 : 1;
                for (var k = startIdx; k < b.Chain.Count && budget2 > 0; k++)
                {
                    var ch = b.Chain[k];
                    double mdays = MonthMath.DaysInMonth(ch.Y, ch.M);
                    var take = Math.Min(mdays, budget2);
                    var periodDem = ch.ColPlan * (take / mdays);
                    totalDem += periodDem;
                    dto.Step4.Rows.Add(new Tool1WindowRowDto
                    {
                        Month = Or(ch.Label, ch.MKey), Demand = Js.Fmt(Js.Round(ch.ColPlan)), DaysInMonth = N(mdays),
                        DaysTaken = N(take) + (take < mdays ? " (partial)" : ""), Partial = take < mdays,
                        PeriodDemand = Js.Fmt(Js.Round(periodDem)), Background = k % 2 == 0 ? "#fff" : "#f9f5ff"
                    });
                    budget2 -= take;
                }
                dto.Step4.TotalLabel = "TOTAL (" + N(D) + " days)";
                dto.Step4.TotalDemand = Js.Fmt(Js.Round(totalDem));
            }
            var wd = JsOps.Or(b.WindowDemand ?? 0, 0);
            var ws = JsOps.Or(b.WinStockStart ?? 0, 0);
            var rateTxt = JsOps.Truthy(b.WindowDailyRate) ? Js.ToFixed(b.WindowDailyRate!.Value, 1) : "0";
            var strR = JsOps.Truthy(art) ? N(Js.Round(art!.Value)) : "0";
            var breach = Breach(art);
            dto.Step4.Breach = breach;
            dto.Step4.StrColour = breach ? Red : Green;
            dto.Step4.Formula.Add("Daily Rate = " + N(D) + "d Demand ÷ D = " + Js.Fmt(Js.Round(wd)) + " ÷ " + N(D) + " = " + rateTxt + " units/day");
            dto.Step4.Formula.Add("Stock at Window Start (DM+1) = " + Js.Fmt(Js.Round(ws)) + " units");
            dto.Step4.Formula.Add("Colour STR = (Stock ÷ " + N(D) + "d Demand) × D = (" + Js.Fmt(Js.Round(ws)) + " ÷ " + Js.Fmt(Js.Round(wd)) + ") × " + N(D) + " = " + strR + "d");
            dto.Step4.Formula.Add("Threshold (T + Buffer) = " + N(p.T1) + "d + " + N(buf) + "% = " + effR + "d");
            dto.Step4.Formula.Add("Result : STR " + strR + "d " + (breach ? "> " + effR + "d → BREACH" : "≤ " + effR + "d → OK"));

            // Step 5 (README item 2: exact threshold, engine's stock before this PO)
            dto.Step5.HeaderColour = breach ? Red : Green;
            dto.Step5.PanelBg = breach ? "#FEF2F2" : "#DCFCE7";
            var rate = JsOps.Or(b.WindowDailyRate ?? 0, 0);
            var stockNoProp = b.StockAtD1NoProp
                ?? (b.Chain.Count > 0 ? b.Chain[0].OpenStock + b.Chain[0].EPO - b.Chain[0].ColPlan : ws - r.Qty);
            var mp = JsOps.Or(b.MaxPermQty ?? 0, 0);
            dto.Step5.Formula.Add("Max Permissible = MAX(0, T×(1+Buffer%) × Daily Rate − Stock at Window Start (DM+1))");
            dto.Step5.Formula.Add("= MAX(0, " + Js.LocaleIN(effT, 2) + " × " + Js.ToFixed(rate, 1) + " − " + Js.Fmt(stockNoProp) + ")");
            dto.Step5.Formula.Add("= MAX(0, " + Js.Fmt(effT * rate) + " − " + Js.Fmt(stockNoProp) + ")");
            dto.Step5.Formula.Add("= " + Js.Fmt(mp) + " units");
            dto.Step5.ResultColour = b.MaxPermQty > 0 ? Green : Red;
            var tTxt = "(T=" + N(p.T1) + "d + " + N(buf) + "% buffer)";
            if (b.MaxPermQty == 0)
            {
                dto.Step5.Verdict = "HOLD"; dto.Step5.VerdictColour = Red; dto.Step5.VerdictLead = "HOLD — Do not raise PO.";
                dto.Step5.VerdictText = "Existing stock at Window Start (DM+1) already covers " + strR + "d — exceeds effective threshold " + effR + "d " + tTxt +
                                        ". Max Permissible = 0. Even without the proposed PO, stock is already excessive.";
            }
            else if ((art ?? 0) <= effT)
            {
                dto.Step5.Verdict = "ALLOW"; dto.Step5.VerdictColour = Green; dto.Step5.VerdictLead = "ALLOW — Proposed qty fits within permissible headroom.";
                dto.Step5.VerdictText = "STR = " + strR + "d ≤ " + effR + "d " + tTxt + ". Stock will be absorbed within the cover window. Full proposed qty of " +
                                        Js.Fmt(JsOps.Or(r.Qty, 0)) + " units is approved.";
            }
            else
            {
                dto.Step5.Verdict = "REDUCE"; dto.Step5.VerdictColour = "#D97706"; dto.Step5.VerdictLead = "REDUCE — Proposed qty exceeds permissible headroom.";
                dto.Step5.VerdictText = "STR breaches " + effR + "d " + tTxt + " at full proposed qty. Max Permissible = " + Js.Fmt(mp) +
                                        " units. Raise only this quantity to stay within the effective threshold.";
            }
            return dto;
        }

        private static JsObjectMap<double>? ToMap(Dictionary<string, double>? d)
        {
            if (d == null) return null;
            var m = new JsObjectMap<double>();
            foreach (var kv in d) m[kv.Key] = kv.Value;
            return m;
        }

        private static Tool1CardADto BuildA(Tool1EngineAResult r, EngineParams p, string dc, string decision)
        {
            var dto = new Tool1CardADto();
            var pdstrPct = r.Pdstr != null ? Js.ToFixed(r.Pdstr.Value * 100, 1) + "%" : "N/A";
            var peerAvgPct = Js.ToFixed(r.PeerAvg * 100, 1) + "%";
            var aboveMean = r.Pdstr != null && r.Pdstr >= r.PeerAvg;
            var gapPct = r.GapPct != null ? (aboveMean ? "0.0% (at/above avg)" : Js.ToFixed(r.GapPct.Value, 1) + "%") : "—";
            dto.Step1.Add(new() { Key = "BSR Options\n(MC×MRP)", Value = N(JsOps.Or(r.TotalBSR, 0)), Colour = "#1A4FAF" });
            dto.Step1.Add(new() { Key = "WOF ≥ " + N(p.Wof) + "w\n(Peer Pool)", Value = N(r.PeerPool.Count), Colour = "#0891B2" });
            dto.Step1.Add(new() { Key = "WOF This Article", Value = Js.ToFixed(r.Wof, 0) + "w", Colour = "#0891B2" });
            dto.Step1.Add(new() { Key = "BSR Source", Value = Or(r.BsrLabel, "—"), Colour = "#7C3AED" });
            dto.Step1.Add(new() { Key = "Option Type", Value = Or(r.OptionType, "—"), Colour = "#7C3AED" });
            if (r.Pdstr != null)
                dto.Step2 = new List<Tool1KvDto>
                {
                    new() { Key = "This Article PDSTR", Value = pdstrPct, Colour = dc },
                    new() { Key = "Peer Avg PDSTR", Value = peerAvgPct, Colour = "#1A4FAF" },
                    new() { Key = "Gap from Avg", Value = gapPct, Colour = aboveMean ? Green : dc },
                    new() { Key = "vs Mean", Value = aboveMean ? "At / Above" : "Below", Colour = aboveMean ? Green : Red }
                };
            if (!string.IsNullOrEmpty(r.Quartile) && r.Percentile != null)
            {
                var pct = r.Percentile.Value;
                var label = pct <= 25 ? "Q4 — Slow Seller" : pct <= 50 ? "Q3 — Watch" : pct <= 75 ? "Q2 — Healthy" : "Q1 — Top Seller";
                var n = r.CleanPool.Count > 0 ? N(r.CleanPool.Count) : r.PeerPool.Count > 0 ? N(r.PeerPool.Count) : "?";
                dto.Step3 = new Tool1QuartileDto
                {
                    Heading = "Step 3 — Quartile Ranking (" + N(pct) + "th pct — " + n + " styles)",
                    PoolText = "Rank within peer group of " + n + " styles",
                    RankText = N(pct) + "th percentile — " + label, RankColour = dc,
                    Position = Math.Min(Math.Max(pct, 2), 98), Percentile = pct
                };
            }
            if (r.Pdstr != null && r.Pdstr < r.PeerAvg && (r.Quartile == "Q4" || r.Quartile == "Q3"))
            {
                var bt2 = r.BorderTolUsed is { } bu && JsOps.Truthy(bu) ? bu : JsOps.Or(p.BorderTolerance, 10);
                var bz = r.InBorderZone == true;
                var gap = r.GapPct != null ? N(r.GapPct.Value) : "undefined";
                dto.Step4 = new Tool1BorderDto
                {
                    InBorderZone = bz,
                    Lines = new()
                    {
                        "Threshold (Q4 & Q3): gap ≤ " + N(bt2) + "%",
                        "This article gap = " + gap + "%",
                        "Result = " + (bz ? "✓ IN BORDER ZONE" : "✗ OUTSIDE BORDER ZONE")
                    },
                    Verdict = bz
                        ? (r.Quartile == "Q3" ? "IN BORDER ZONE (gap ≤" + N(bt2) + "%) → Q3 softened: Watch List → Allow"
                                              : "IN BORDER ZONE (gap ≤" + N(bt2) + "%) → Q4 softened: Block → Watch List")
                        : "OUTSIDE BORDER ZONE (gap " + gap + "% > " + N(bt2) + "%) → Hard decision stands"
                };
            }
            if (decision == Decisions.Block && r.Pdstr != null && r.Pdstr < 0.01 && r.Wof >= 4)
                dto.AbsoluteBlockNote = "⚡ Absolute Block Override: PDSTR = " + Js.ToFixed(r.Pdstr.Value * 100, 1) + "%/day < 1%/day threshold with WOF " +
                                        Js.ToFixed(r.Wof, 0) + "w ≥ 4w. Block regardless of quartile.";
            return dto;
        }
    }
}
