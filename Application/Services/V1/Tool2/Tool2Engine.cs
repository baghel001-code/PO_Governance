using Application.DTOs.Recommendation;
using Application.Interfaces.V1.Recommendation;
using Application.Services.V1.Engines.Common;
using Application.Services.V1.Recommendation;
using Domain.Constants;
using Domain.Entities.Common;
using Domain.Entities.Engine;
using Domain.Exceptions;

namespace Application.Services.V1.Tool2
{
    /// <summary>
    /// The Tool 2 batch assessment — port of index.html <c>runAssessment</c> (line 5337, incl. the
    /// chunked <c>processChunk</c>, here one synchronous loop), <c>routeRequest2</c> (5065, same rule as
    /// <see cref="ResolveIndex.RouteRequest"/>), <c>_rowKey4</c> / <c>_rowColour</c> / <c>_infoKey4</c> (5585)
    /// and <c>runEngineB2Fast</c> (5602). <c>runEngineB2</c> (5090) has no call site in v1.1.4 (the README
    /// / its own comment say so) and is not ported.
    /// </summary>
    public static class Tool2Engine
    {
        private sealed class SalesEntry
        {
            public JsObjectMap<double> ByMon { get; } = new();
            public double DaySum { get; set; }
            public double? CyAvg { get; set; }
        }

        private sealed class Indexes
        {
            public Dictionary<string, double> F1Col { get; } = new(StringComparer.Ordinal);
            public Dictionary<string, SalesEntry> F3Col { get; } = new(StringComparer.Ordinal);
            public Dictionary<string, SalesEntry> F3Mc { get; } = new(StringComparer.Ordinal);
            public Dictionary<string, JsObjectMap<double>> F4 { get; } = new(StringComparer.Ordinal);
            public Dictionary<string, JsObjectMap<double>> F2Po { get; } = new(StringComparer.Ordinal);
        }

        private static readonly string[] GmCols = { "Generic Material", "generic material", "GENERIC MATERIAL" };
        private static readonly string[] ColourCols = { "Color", "Colour", "COLOR", "COLOUR", "colour", "color" };
        private static readonly string[] DmCols = { "Delivery Month", "delivery month", "DEL MONTH" };

        /// <summary>Port of <c>_rowKey4(r)</c>.</summary>
        public static string RowKey4(SheetRow r) =>
            Js.N(r.Col("BRAND TYPE", "Brand Type", "brand type")) + "|" + Js.N(r.Col("MC CD", "MC_CD", "mc cd")) + "|" +
            Js.N(r.Col("MRP", "mrp")) + "|" + Js.N(r.Col("BOGO/NON-BOGO", "Flag", "flag", "Article Type", "Flag (BOGO/NON-BOGO)"));

        /// <summary>Port of <c>_rowColour(r)</c>.</summary>
        public static string RowColour(SheetRow r) => r.Col(ColourCols).Trim().ToLowerInvariant();

        /// <summary>Port of <c>_infoKey4(info)</c>.</summary>
        public static string InfoKey4(GmInfo? info) =>
            info == null ? "" : Js.N(info.Brand) + "|" + Js.N(info.McCd) + "|" + Js.N(info.Mrp) + "|" + Js.N(info.Flag);

        /// <summary>The F2 lines runAssessment assesses: GM, colour, delivery month and a qty &gt; 0.</summary>
        public static List<SheetRow> AssessableRows(EngineData data) =>
            (data.F2 ?? Array.Empty<SheetRow>()).Where(r =>
            {
                var gm = r.Col(GmCols).Trim();
                var colour = r.Col(ColourCols).Trim();
                var dm = r.Col(DmCols).Trim();
                var q = r.Col("Pending Qty", "PENDING QTY", "pending qty", "Released Qty", "RELEASED QTY", "Vendor Name", "VENDOR NAME");
                var qty = q.Length > 0 ? Js.ParseFloat(q) : 0;
                return gm.Length > 0 && colour.Length > 0 && dm.Length > 0 && qty > 0;
            }).ToList();

        /// <summary>
        /// Runs the whole batch on <paramref name="ctx"/>'s data (<c>S.files</c> / <c>S.params</c>), today =
        /// the context's server date. Throws <see cref="AppValidationException"/> with the v1.1.4 toast
        /// texts when F2 is missing or has no valid line.
        /// </summary>
        public static List<Tool2LineResult> Run(RecommendationContext ctx, IRecommendationEngine rec)
        {
            var data = ctx.Data;
            if (data.F2 == null || data.F2.Count == 0)
                throw new AppValidationException("Please upload the F2 (Released PO) file in Data Setup first");
            var rows = AssessableRows(data);
            if (rows.Count == 0)
                throw new AppValidationException("No valid rows found in F2 — check that Generic Material, Color and Delivery Month columns exist");

            var idx = BuildIndexes(data);
            var results = new List<Tool2LineResult>(rows.Count);
            for (var i = 0; i < rows.Count; i++)
            {
                var r = rows[i];
                var gm = r.Col(GmCols).Trim();
                var colour = r.Col(ColourCols).Trim();
                var dm = r.Col(DmCols).Trim();
                var relQty = Js.Num(r.Col("Pending Qty", "PENDING QTY", "pending qty", "Vendor Name", "VENDOR NAME"));
                var poValueRaw = Js.Num(r.Col("PO Value", "PO VALUE", "po value", "PO Value (₹)", "PO Val", "Value", "VALUE", "PO Amount", "PO AMOUNT"));
                double? poValue = poValueRaw > 0 ? poValueRaw : null;
                var vRaw = r.Col("Vendor Name", "VENDOR NAME", "vendor name", "Vendor", "vendor");
                string vendorPick = double.IsNaN(Js.Number(vRaw)) || vRaw.Length == 0
                    ? (vRaw.Length > 0 ? vRaw : "—")
                    : (r.Col("Pending Qty", "PENDING QTY", "pending qty") is { Length: > 0 } pq ? pq : "—");
                var vendor = vendorPick.Trim();
                if (vendor.Length == 0) vendor = "—";
                var poNum = r.Col("PO Number", "PO NO", "PO No", "PO_NUMBER", "po number", "PO number",
                    "Purchase Order", "Purchase Order No", "Purchase Order Number",
                    "SAP PO", "SAP PO No", "Order Number", "Order No", "P.O. Number", "P.O No",
                    "PO", "po no", "po").Trim();
                if (poNum.Length == 0) poNum = "—";

                try
                {
                    var info = ResolveIndex.ResolveGm(data, gm);
                    var route = ResolveIndex.RouteRequest(data, gm, colour);
                    var line = new Tool2LineResult { Idx = i + 1, Gm = gm, Colour = colour, Dm = dm, RelQty = relQty, PoNum = poNum, Engine = route.Engine };
                    double? recQtyRaw = null, maxRetain = null;
                    if (route.Engine == "B")
                    {
                        var b = RunEngineB2Fast(ctx, colour, dm, relQty, info, idx);
                        line.Decision = b.Decision; line.DecisionReason = b.Reason; line.ArtStr = b.ArtStr;
                        recQtyRaw = b.RecQty; maxRetain = b.MaxRetainQty; line.MixPct = b.MixPct; line.OpStock = b.OpStock;
                        line.WindowDailyRate = b.WindowDailyRate; line.WinStockStart = b.WinStockStart; line.Chain = b.Chain;
                    }
                    else if (route.Engine == "A")
                    {
                        var rawA = Tool2EngineA.Run(data, gm, colour, route.OptionType, info);
                        StrCautionDto? caution = null;
                        if (rawA.Decision == "Allow" && (rawA.Quartile == "Q1" || rawA.Quartile == "Q2"))
                        {
                            if (info == null) throw new InvalidOperationException("Cannot read properties of null (reading 'mcCd')");
                            caution = rec.StrCaution(ctx, info.McCd, info.Mrp, info.Brand, info.Flag, colour, gm, dm);
                        }
                        line.Decision = MapA(rawA.Decision);
                        line.DecisionReason = rawA.DecisionReason;
                        line.StrCaution = caution;
                        line.OptionType = rawA.OptionType;
                        line.Quartile = rawA.Quartile;
                        line.Percentile = rawA.Percentile;
                        line.Pdstr = rawA.Pdstr;
                        line.PeerAvg = rawA.PeerAvg;
                        line.GapPct = rawA.GapPct;
                        line.InBorderZone = rawA.InBorderZone == true;
                        line.BorderTolUsed = rawA.BorderTolUsed is double bt && bt != 0 && !double.IsNaN(bt) ? bt : null;
                        line.Wof = rawA.Wof;
                        line.TotalBsr = rawA.TotalBsr != 0 ? rawA.TotalBsr : null;
                        line.PeerPool = rawA.PeerPool; line.CleanPool = rawA.CleanPool; line.BsrLabel = string.IsNullOrEmpty(rawA.BsrLabel) ? null : rawA.BsrLabel;
                        line.Siblings = rawA.Siblings; line.QualSibs = rawA.QualSibs; line.SibRanked = rawA.SibRanked;
                    }
                    else
                    {
                        line.Decision = Decisions.Retain;
                        line.DecisionReason = "Not found in Universe Master or CY BSR. Treated as new article — Auto Retain.";
                    }
                    if (string.IsNullOrEmpty(line.Decision)) line.Decision = Decisions.CannotValidate;
                    // const _recQty=result.recQty||result.maxRetainQty||0;
                    var rq = JsOps.Truthy(recQtyRaw) ? recQtyRaw!.Value : JsOps.Truthy(maxRetain) ? maxRetain!.Value : 0;
                    line.RecQty = rq;
                    line.ExcessQty = line.Decision == Decisions.ReduceQty || line.Decision == Decisions.Cancel ? Math.Max(0, relQty - rq) : 0;
                    if (line.ArtStr is double a && (a == 0 || double.IsNaN(a))) line.ArtStr = null; // artSTR:result.artSTR||null
                    line.Div = Or(info?.Div); line.McDesc = Or(info?.McDesc); line.Mrp = Or(info?.Mrp);
                    line.Vendor = vendor;
                    line.Info = info;
                    line.PoValue = poValue;
                    line.PoRate = poValue != null && relQty > 0 ? poValue / relQty : null;
                    if (line.OptionType == "") line.OptionType = null;
                    if (line.WindowDailyRate is double w && (w == 0 || double.IsNaN(w))) line.WindowDailyRate = null;
                    if (line.WinStockStart is double s && (s == 0 || double.IsNaN(s))) line.WinStockStart = null;
                    if (line.Chain is { Count: 0 }) { /* [] is truthy — kept */ }
                    if (double.IsNaN(line.MixPct)) line.MixPct = 0;
                    if (double.IsNaN(line.OpStock)) line.OpStock = 0;
                    results.Add(line);
                }
                catch (Exception ex) when (ex is not AppException)
                {
                    results.Add(new Tool2LineResult
                    {
                        Idx = i + 1, Gm = gm, Colour = colour, Dm = dm, RelQty = relQty, PoNum = poNum, Engine = "?",
                        Decision = Decisions.CannotValidate, DecisionReason = "Error: " + ex.Message, IsErrorRow = true,
                        RecQty = 0, ExcessQty = 0, Div = "—", McDesc = "—", Mrp = "—", Vendor = "—"
                    });
                }
            }
            return results;
        }

        private static string Or(string? s) => string.IsNullOrEmpty(s) ? "—" : s;

        /// <summary>The decision map of processChunk: Tool 1 decisions → Tool 2 actions.</summary>
        public static string MapA(string d) => d switch
        {
            "Auto Allow" => Decisions.Retain,
            "Allow" => Decisions.Retain,
            "Block" => Decisions.Cancel,
            "Hold — Don't Raise" => Decisions.Cancel,
            "Recommend Reduced Qty" => Decisions.ReduceQty,
            "Watch List" => Decisions.WatchList,
            "Cannot Validate" => Decisions.CannotValidate,
            _ => d
        };

        // ── pre-built indexes (runAssessment) ─────────────────────────────────

        private static Indexes BuildIndexes(EngineData data)
        {
            var ix = new Indexes();
            if (data.F1 != null)
                foreach (var r in data.F1)
                {
                    var stk = Js.Num(r.Col("OP STK", "Op Stk", "op stk", "OP STK "));
                    var mk = RowKey4(r) + "-" + RowColour(r);
                    ix.F1Col[mk] = (ix.F1Col.TryGetValue(mk, out var v) ? v : 0) + stk;
                }
            if (data.F3 != null)
                foreach (var r in data.F3)
                {
                    var keys = Tool2Js.JsKeys(r).ToList();
                    var cyv = r.Col("CY APR & MAY AVG", "CY APR & MAY AVG ");
                    var ck = RowKey4(r) + "-" + RowColour(r);
                    var mk = RowKey4(r);
                    for (var isCol = 0; isCol < 2; isCol++)
                    {
                        var target = isCol == 0 ? ix.F3Col : ix.F3Mc;
                        var k = isCol == 0 ? ck : mk;
                        if (!target.TryGetValue(k, out var e)) target[k] = e = new SalesEntry();
                        foreach (var h in keys)
                        {
                            if (Tool2Js.IsDayCol(h)) { e.DaySum += Js.Num(r[h]); continue; }
                            if (MonthMath.IsMMYYYY(h))
                            {
                                var v = Js.Num(r[h]); var yk = Tool2Js.ToYM(h);
                                if (yk != null) e.ByMon[yk] = e.ByMon.Get(yk) + v;
                            }
                        }
                        if (cyv.Length > 0) e.CyAvg = (e.CyAvg is double c && c != 0 ? c : 0) + Js.Num(cyv);
                    }
                }
            if (data.F4 != null)
                foreach (var r in data.F4)
                {
                    var mk = RowKey4(r);
                    if (!ix.F4.TryGetValue(mk, out var e)) ix.F4[mk] = e = new JsObjectMap<double>();
                    foreach (var h in Tool2Js.JsKeys(r))
                    {
                        var hStr = h.Trim();
                        if (MonthMath.IsMMYYYY(hStr))
                        {
                            var v = Js.Num(r[h]);
                            if (v != 0 && !double.IsNaN(v)) e[hStr] = e.Get(hStr) + v;
                            continue;
                        }
                        var mmyy = Tool2Js.DateToMMYYYY(h) ?? Tool2Js.DateToMMYYYY(r[h]);
                        if (mmyy != null)
                        {
                            var v = Js.Num(r[h]);
                            if (v != 0 && !double.IsNaN(v)) e[mmyy] = e.Get(mmyy) + v;
                        }
                    }
                }
            if (data.F2 != null)
                foreach (var r in data.F2)
                {
                    var ck = RowKey4(r) + "-" + RowColour(r);
                    var dm = r.Col(DmCols).Trim();
                    var qty = Js.Num(r.Col("Pending Qty", "PENDING QTY", "pending qty", "Vendor Name", "VENDOR NAME"));
                    if (dm.Length == 0 || qty == 0 || double.IsNaN(qty)) continue;
                    if (!ix.F2Po.TryGetValue(ck, out var e)) ix.F2Po[ck] = e = new JsObjectMap<double>();
                    e[dm] = e.Get(dm) + qty;
                }
            return ix;
        }

        // ── runEngineB2Fast ───────────────────────────────────────────────────

        private sealed class BResult
        {
            public string Decision = "";
            public string Reason = "";
            public double? ArtStr;
            public double? RecQty;
            public double? MaxRetainQty;
            public double MixPct;
            public double OpStock;
            public double? WindowDailyRate;
            public double? WinStockStart;
            public List<Tool2ChainStep>? Chain;
        }

        private static BResult RunEngineB2Fast(RecommendationContext ctx, string colour, string deliveryMonth, double releasedQty, GmInfo? info, Indexes idx)
        {
            var p = ctx.P;
            double T = p.T2, winDays = p.D, mixT = p.Mix, N = p.N;
            if (info == null || info.McCd.Length == 0)
                return new BResult { Decision = Decisions.CannotValidate, Reason = "GM not resolved to Brand×MC×MRP×Flag." };
            var dmParts = MonthMath.ParseMmyyyy(deliveryMonth);
            if (dmParts == null) return new BResult { Decision = Decisions.CannotValidate, Reason = "Invalid delivery month." };
            var (dmM, dmY) = dmParts.Value;

            var mk4 = InfoKey4(info);
            var colKey = mk4 + "-" + colour.Trim().ToLowerInvariant();
            var opStock = idx.F1Col.TryGetValue(colKey, out var os) ? os : 0;
            var colEntry = idx.F3Col.TryGetValue(colKey, out var ce) ? ce : new SalesEntry();
            var mcEntry = idx.F3Mc.TryGetValue(mk4, out var me) ? me : new SalesEntry();
            double colAvg, mcAvg, mixPct;
            if (colEntry.DaySum > 0 || mcEntry.DaySum > 0)
            {
                var rDays = JsOps.Or(p.MixWindowDays, 45);
                colAvg = colEntry.DaySum / rDays;
                mcAvg = mcEntry.DaySum / rDays;
                mixPct = mcEntry.DaySum > 0 ? colEntry.DaySum / mcEntry.DaySum : 0;
            }
            else
            {
                double AvgOf(SalesEntry e)
                {
                    if (e.CyAvg != null) return e.CyAvg.Value;
                    var sorted = e.ByMon.Keys.ToList();
                    sorted.Sort(string.CompareOrdinal);
                    var refMons = Tool2Js.SliceLast(sorted, N);
                    if (refMons.Count == 0) return 0;
                    double s = 0; foreach (var m in refMons) s += e.ByMon.Get(m);
                    return s / refMons.Count;
                }
                colAvg = AvgOf(colEntry);
                mcAvg = AvgOf(mcEntry);
                mixPct = mcAvg > 0 ? colAvg / mcAvg : 0;
            }

            if (mcAvg > 0 && mixPct < mixT / 100)
                return new BResult
                {
                    Decision = Decisions.WatchList, MixPct = mixPct, OpStock = opStock,
                    Reason = "Colour Mix % = " + Js.ToFixed(mixPct * 100, 1) + "% < " + Js.NumberToString(mixT) + "% threshold. Broken/cut size likely."
                };

            var forecastByMon = idx.F4.TryGetValue(mk4, out var fm) ? fm : new JsObjectMap<double>();
            var existingPO = idx.F2Po.TryGetValue(colKey, out var ep) ? ep : new JsObjectMap<double>();
            double? effectiveMixPct = mixPct > 0 ? mixPct : (mcAvg > 0 ? 0 : null);

            int currY = ctx.Y, currM = ctx.M, todayDate = ctx.D;
            var (winY, winM) = MonthMath.AddMonths(dmY, dmM, 1);
            var winKey = MonthMath.Mmyyyy(winY, winM);
            var carry = PrevMonthCarry.Compute(existingPO, currY, currM, todayDate, p);
            var carryNote = PrevMonthCarry.Note(carry);

            var runStock = opStock;
            for (var i = 0; ; i++)
            {
                var (cy, cm) = MonthMath.AddMonths(currY, currM, i);
                var mkey = MonthMath.Mmyyyy(cy, cm);
                if (mkey == winKey || i > 24) break;
                var ePO = PrevMonthCarry.EPOWithCarry(existingPO, mkey, carry);
                var fc = JsOps.Or(forecastByMon.Get(mkey), 0);
                double totalDays = MonthMath.DaysInMonth(cy, cm);
                var remainingDays = i == 0 ? totalDays - todayDate : totalDays;
                var cp = effectiveMixPct != null ? fc * effectiveMixPct.Value * (remainingDays / totalDays) : 0;
                runStock = Math.Max(runStock + ePO - cp, 0);
            }

            var currKey2 = MonthMath.Mmyyyy(currY, currM);
            var chain = new List<Tool2ChainStep>();
            for (var i = 0; i < 13; i++)
            {
                var (cy, cm) = MonthMath.AddMonths(winY, winM, i);
                var mkey = MonthMath.Mmyyyy(cy, cm);
                var ePO = PrevMonthCarry.EPOWithCarry(existingPO, mkey, carry);
                var fc = JsOps.Or(forecastByMon.Get(mkey), 0);
                double totalDays = MonthMath.DaysInMonth(cy, cm);
                var isCurr = mkey == currKey2;
                var demandDays = isCurr ? totalDays - todayDate : totalDays;
                var colPlan = effectiveMixPct != null ? fc * effectiveMixPct.Value * (demandDays / totalDays) : 0;
                var openStock = i == 0 ? runStock : JsOps.Or(chain[i - 1].CloseStock, 0);
                var closeStock = Math.Max(openStock + ePO - colPlan, 0);
                chain.Add(new Tool2ChainStep
                {
                    Y = cy, M = cm, MKey = mkey, OpenStock = openStock, EPO = ePO, ColPlan = colPlan, IsCurrMonth = isCurr,
                    DemandDays = demandDays, TotalDays = totalDays, Fc = fc, CloseStock = closeStock
                });
            }

            var winStockStart = JsOps.Or(chain[0].OpenStock, 0);
            double budget = winDays, windowDemand = 0;
            for (var i = 0; i < chain.Count && budget > 0; i++)
            {
                double days = MonthMath.DaysInMonth(chain[i].Y, chain[i].M);
                var take = Math.Min(days, budget);
                windowDemand += chain[i].ColPlan * (take / days);
                budget -= take;
            }
            var windowDailyRate = winDays > 0 && windowDemand > 0 ? windowDemand / winDays : 0;

            if (windowDemand == 0)
                return new BResult
                {
                    Decision = Decisions.Retain, Reason = "No forecast data for MC in F4. Cannot compute STR — treated as Retain.",
                    MixPct = mixPct, OpStock = opStock, WindowDailyRate = 0, WinStockStart = winStockStart
                };

            var artStr = winStockStart / windowDemand * winDays;
            var buf = JsOps.Or(p.StrBuffer, 0);
            var effT = T * (1 + buf / 100);

            for (var i = 0; i < chain.Count; i++)
            {
                double rollingDemand = 0, rollingBudget = winDays;
                for (var j = i; j < chain.Count && rollingBudget > 0; j++)
                {
                    double mDays = MonthMath.DaysInMonth(chain[j].Y, chain[j].M);
                    var take = Math.Min(mDays, rollingBudget);
                    rollingDemand += chain[j].ColPlan * (take / mDays);
                    rollingBudget -= take;
                }
                var rollingRate = rollingDemand > 0 ? rollingDemand / winDays : 0;
                chain[i].Str = rollingRate > 0 ? chain[i].OpenStock / rollingRate : null;
                chain[i].Status = chain[i].Str != null ? (chain[i].Str <= effT ? "OK" : "Breach") : null;
            }

            var hasBreach = artStr > effT;
            double maxRetain; double? recQty = null; string decision, reason;
            string strR = Js.NumberToString(Js.Round(artStr)), effR = Js.NumberToString(Js.Round(effT)), tS = Js.NumberToString(T), bufS = Js.NumberToString(buf);
            if (!hasBreach)
            {
                maxRetain = releasedQty;
                decision = Decisions.Retain;
                reason = "Colour STR = " + strR + "d ≤ T=" + tS + "d + " + bufS + "% buffer (" + effR + "d) at window start (" + MonthMath.Mmyyyy(winY, winM) + "). PO within cover limit. Retain full qty.";
            }
            else
            {
                var stockNoPO = Math.Max(0, winStockStart - releasedQty);
                maxRetain = Math.Max(0, Js.Round(effT * windowDailyRate - stockNoPO));
                if (maxRetain >= releasedQty)
                {
                    maxRetain = releasedQty;
                    decision = Decisions.Retain;
                    reason = "STR = " + strR + "d > " + effR + "d (T=" + tS + "d + " + bufS + "% buffer) but pre-existing stock is the primary cause. Full PO can be retained.";
                }
                else if (maxRetain > 0)
                {
                    decision = Decisions.ReduceQty;
                    recQty = maxRetain;
                    reason = "STR = " + strR + "d > " + effR + "d (T=" + tS + "d + " + bufS + "% buffer). Max Permissible Qty = " + Js.Fmt(maxRetain) + " units (= " + effR + "d × " +
                             Js.ToFixed(windowDailyRate, 1) + "/d − " + Js.Fmt(Math.Max(0, Js.Round(stockNoPO))) + " stock w/o PO). Cut balance.";
                }
                else
                {
                    decision = Decisions.Cancel;
                    reason = "STR = " + strR + "d > " + effR + "d (T=" + tS + "d + " + bufS + "% buffer). Stock w/o PO (" + Js.Fmt(Math.Max(0, Js.Round(stockNoPO))) + " units) already exceeds " + effR + "d cover. Cancel full PO.";
                }
            }
            reason += carryNote;
            return new BResult
            {
                Decision = decision, Reason = reason, ArtStr = artStr, RecQty = recQty, MaxRetainQty = maxRetain, MixPct = mixPct, OpStock = opStock,
                WindowDailyRate = windowDailyRate, WinStockStart = winStockStart, Chain = chain
            };
        }
    }
}
