using System.Text.Json.Nodes;
using DecisionEngine.Common;
using DecisionEngine.Model;
using DecisionEngine.Results.Tool1;

namespace DecisionEngine.Tool1
{
    /// <summary>
    /// Port of index.html <c>runEngineB</c> (line 3847) — Tool 1 Single Concept MRP: colour mix gate,
    /// forward stock chain to the delivery month, days of cover (STR) of the D-day window from Day 1 of
    /// the month after delivery vs T × (1 + buffer), Max Permissible Qty, previous-month pending carry-over
    /// and the same-day ledger of quantities approved today (all users).
    /// "Today" is the server date (v1.1.4 used the PC clock here and the server date for the ledger).
    /// </summary>
    public sealed class Tool1EngineB
    {
        /// <param name="data">The data snapshot (<c>S.files</c> + <c>S.params</c>).</param>
        /// <param name="ledger">Today's requests of all users (<c>S.requests</c>).</param>
        /// <param name="today">Server date "yyyy-MM-dd".</param>
        public Tool1EngineBResult Run(EngineData data, IReadOnlyList<JsonObject> ledger, string today,
                                      string gm, string colour, string deliveryMonth, double proposedQty, Tool1InfoDto? info)
        {
            var p = data.Params;
            double T = p.T1, winDays = p.D, mixT = p.Mix, N = p.N;

            if (info == null || string.IsNullOrEmpty(info.McCd))
                return new Tool1EngineBResult
                {
                    Decision = Decisions.CannotValidate,
                    DecisionReason = "Generic Material not resolved to Brand×MC×MRP×Flag. Upload Universe Master file.",
                    MixPct = 0, OpStock = 0, MixFailed = false, RecQty = null, ArtSTR = null
                };

            var dmParts = MonthMath.ParseMmyyyy(deliveryMonth);
            if (dmParts == null)
                return new Tool1EngineBResult
                {
                    Decision = Decisions.CannotValidate, DecisionReason = "Invalid delivery month.",
                    MixPct = 0, OpStock = 0, MixFailed = false, RecQty = null, ArtSTR = null
                };
            var (dmM, dmY) = dmParts.Value;

            var rBrand = (info.Brand ?? "").Trim();
            var rMC = Tool1JsHelpers.StripDot0(info.McCd);
            var rMRP = Tool1JsHelpers.StripDot0(info.Mrp);
            var rFlag = (info.Flag ?? "").Trim();
            var rColour = (colour ?? "").Trim();

            bool MatchRow(SheetRow r, bool withColour)
            {
                var mc = Js.N(r.Col("MC CD", "MC_CD", "mc cd"));
                var mrp = Js.N(r.Col("MRP", "mrp"));
                var brand = Js.N(r.Col("BRAND TYPE", "Brand Type", "brand type"));
                var flag = Js.N(r.Col("BOGO/NON-BOGO", "Flag", "flag", "Article Type", "Flag (BOGO/NON-BOGO)"));
                var colourVal = Js.N(r.Col("Color", "Colour", "COLOR", "COLOUR", "colour", "color"));
                var mcOk = rMC.Length == 0 || mc == rMC.ToLowerInvariant();
                var mrpOk = rMRP.Length == 0 || mrp == rMRP.ToLowerInvariant();
                var bOk = rBrand.Length == 0 || brand == rBrand.ToLowerInvariant();
                var fOk = rFlag.Length == 0 || flag == rFlag.ToLowerInvariant() || flag == "";
                var cOk = !withColour || colourVal == rColour.ToLowerInvariant();
                return mcOk && mrpOk && bOk && fOk && cOk;
            }

            // ── F1: Op Stock — sum every row of KEY-3 (colour level) ──
            double opStock = 0;
            if (data.F1 != null)
                foreach (var r in data.F1)
                    if (MatchRow(r, true)) opStock += Js.Num(r.Col("OP STK", "Op Stk", "op stk", "OP STK "));

            // ── F3: Sales trend ──
            var colSalesByMon = new Dictionary<string, double>(StringComparer.Ordinal);
            var mcSalesByMon = new Dictionary<string, double>(StringComparer.Ordinal);
            double colDaySum = 0, mcDaySum = 0;
            double? cyAvgColour = null, cyAvgMCTotal = null;
            var hasDailyF3 = false;
            if (data.F3 != null)
            {
                foreach (var r in data.F3)
                {
                    var isThisCol = MatchRow(r, true);
                    var isMCTotal = MatchRow(r, false);
                    if (!isThisCol && !isMCTotal) continue;
                    foreach (var h in Tool1JsHelpers.JsKeys(r))
                    {
                        if (Tool1JsHelpers.IsDayCol(h))
                        {
                            hasDailyF3 = true;
                            var dv = Js.Num(r[h]);
                            if (isThisCol) colDaySum += dv;
                            if (isMCTotal) mcDaySum += dv;
                            continue;
                        }
                        if (!MonthMath.IsMMYYYY(h)) continue;
                        var v = Js.Num(r[h]);
                        var yk = Tool1JsHelpers.ToYM(h);
                        if (yk == null) continue;
                        if (isThisCol) colSalesByMon[yk] = colSalesByMon.GetValueOrDefault(yk) + v;
                        if (isMCTotal) mcSalesByMon[yk] = mcSalesByMon.GetValueOrDefault(yk) + v;
                    }
                    var cyv = r.Col("CY APR & MAY AVG", "CY APR & MAY AVG ");
                    if (cyv != "")
                    {
                        if (isThisCol) cyAvgColour = Js.Num(cyv);
                        if (isMCTotal) cyAvgMCTotal = (cyAvgMCTotal ?? 0) + Js.Num(cyv);
                    }
                }
            }

            double colAvg, mcAvg, mixPct;
            if (hasDailyF3)
            {
                var rDays = JsOps.Or(p.MixWindowDays, 45);
                colAvg = colDaySum / rDays;
                mcAvg = mcDaySum / rDays;
                mixPct = mcDaySum > 0 ? colDaySum / mcDaySum : 0;
            }
            else
            {
                var sortedMons = colSalesByMon.Keys.OrderBy(k => k, StringComparer.Ordinal).ToList();
                var refMons = Tool1JsHelpers.SliceNeg(sortedMons, N);
                colAvg = cyAvgColour ?? (refMons.Count > 0 ? refMons.Aggregate(0.0, (s, m) => s + colSalesByMon.GetValueOrDefault(m)) / refMons.Count : 0);
                var mcRefMons = Tool1JsHelpers.SliceNeg(mcSalesByMon.Keys.OrderBy(k => k, StringComparer.Ordinal).ToList(), N);
                mcAvg = cyAvgMCTotal ?? (mcRefMons.Count > 0 ? mcRefMons.Aggregate(0.0, (s, m) => s + mcSalesByMon.GetValueOrDefault(m)) / mcRefMons.Count : 0);
                mixPct = mcAvg > 0 ? colAvg / mcAvg : 0;
            }

            // Gate 1: Mix % below threshold (and the MC has sales) → Watch List
            if (mcAvg > 0 && mixPct < mixT / 100)
                return new Tool1EngineBResult
                {
                    Decision = Decisions.WatchList,
                    DecisionReason = "Colour Mix % = " + Js.ToFixed(mixPct * 100, 1) + "% < " + Js.NumberToString(mixT) +
                                     "% threshold. Broken/cut size likely. STR not computed.",
                    MixPct = mixPct, ColAvg = colAvg, McAvg = mcAvg, OpStock = opStock, MixFailed = true, RecQty = null, ArtSTR = null,
                    HasDailyF3 = hasDailyF3, ForecastByMon = new(), ExistingPO = new()
                };

            // ── F4: forecast — Brand × MC × MRP × Flag ──
            var forecastByMon = new JsObjectMap<double>();
            void AddFc(string k, double v) => forecastByMon[k] = (forecastByMon.TryGetValue(k, out var o) ? o : 0) + v;
            if (data.F4 != null)
            {
                foreach (var r in data.F4)
                {
                    if (!MatchRow(r, false)) continue;
                    foreach (var h in Tool1JsHelpers.JsKeys(r))
                    {
                        var hStr = h.Trim();
                        if (MonthMath.IsMMYYYY(hStr))
                        {
                            var v = Js.Num(r[h]);
                            if (v != 0 && !double.IsNaN(v)) AddFc(hStr, v);
                            continue;
                        }
                        var mmyy = Tool1JsHelpers.DateToMMYYYY(h) ?? Tool1JsHelpers.DateToMMYYYY(r[h]);
                        if (!string.IsNullOrEmpty(mmyy)) { var v = Js.Num(r[h]); if (v != 0 && !double.IsNaN(v)) AddFc(mmyy, v); }
                    }
                }
                if (forecastByMon.Count == 0)
                {
                    foreach (var r in data.F4)
                    {
                        if (!MatchRow(r, false)) continue;
                        foreach (var h in Tool1JsHelpers.JsKeys(r))
                        {
                            var n = Js.Num(h);
                            if (n > 32874 && n < 109574)
                            {
                                var mmyy = Tool1JsHelpers.DateToMMYYYY(n);
                                var v = Js.Num(r[h]);
                                if (!string.IsNullOrEmpty(mmyy) && v != 0 && !double.IsNaN(v)) AddFc(mmyy, v);
                            }
                        }
                    }
                }
            }

            // ── F2: existing pending PO for this colour ──
            var existingPO = new JsObjectMap<double>();
            void AddPo(string k, double v) => existingPO[k] = (existingPO.TryGetValue(k, out var o) ? o : 0) + v;
            if (data.F2 != null)
                foreach (var r in data.F2)
                {
                    if (!MatchRow(r, true)) continue;
                    var dm = r.Col("Delivery Month", "delivery month", "DEL MONTH").Trim();
                    var qty = Js.Num(r.Col("Pending Qty", "PENDING QTY", "pending qty"));
                    if (dm.Length > 0 && qty != 0 && !double.IsNaN(qty)) AddPo(dm, qty);
                }

            // ── Same-day ledger: quantities approved today for this exact Brand × MC × MRP × Flag × Colour ──
            if (ledger.Count > 0)
            {
                foreach (var req in ledger)
                {
                    if (!LedgerHelpers.IsToday(req, today)) continue;
                    var ri = LedgerHelpers.Info(req);
                    if (ri == null) continue;
                    if (!LedgerHelpers.SameBucket(ri, info.McCd, info.Mrp, info.Brand, info.Flag)) continue;
                    if (LedgerHelpers.Norm(LedgerHelpers.Get(req, "colour")) != LedgerHelpers.Norm(colour)) continue;
                    var approved = LedgerHelpers.ApprovedQtyFor(req);
                    var month = LedgerHelpers.Get(req, "month");
                    if (approved is > 0 && Js.Truthy(month)) AddPo(Js.ToJsString(month), approved.Value);
                }
            }

            double? effectiveMixPct = mixPct > 0 ? mixPct : (mcAvg > 0 ? 0 : null);

            var (currY, currM, todayDate) = MonthMath.ParseIsoDate(today);
            var dmKey = MonthMath.Mmyyyy(dmY, dmM);
            var chain = new List<Tool1ChainMonthDto>();

            var carry = PrevMonthCarry.Compute(existingPO, currY, currM, todayDate, p);
            var carryNote = PrevMonthCarry.Note(carry);

            // Pre-roll from today's month to the delivery month
            var runStock = opStock;
            for (var i = 0; ; i++)
            {
                var (cy, cm) = MonthMath.AddMonths(currY, currM, i);
                var mkey = MonthMath.Mmyyyy(cy, cm);
                if (mkey == dmKey) break;
                if (i > 24) break;
                var ePO = PrevMonthCarry.EPOWithCarry(existingPO, mkey, carry);
                var fc = JsOps.Or(forecastByMon.Get(mkey), 0);
                double totalDays = MonthMath.DaysInMonth(cy, cm);
                var remainingDays = i == 0 ? totalDays - todayDate : totalDays;
                var cp = effectiveMixPct != null ? fc * effectiveMixPct.Value * (remainingDays / totalDays) : 0;
                runStock = JsMax0(runStock + ePO - cp);
            }

            // Forward chain from the delivery month (13 months)
            var currKey = MonthMath.Mmyyyy(currY, currM);
            for (var i = 0; i < 13; i++)
            {
                var (cy, cm) = MonthMath.AddMonths(dmY, dmM, i);
                var mkey = MonthMath.Mmyyyy(cy, cm);
                var ePO = PrevMonthCarry.EPOWithCarry(existingPO, mkey, carry);
                var fc = JsOps.Or(forecastByMon.Get(mkey), 0);
                double totalDays = MonthMath.DaysInMonth(cy, cm);
                var isCurrMonth = mkey == currKey;
                var demandDays = isCurrMonth ? totalDays - todayDate : totalDays;
                var colPlan = effectiveMixPct != null ? fc * effectiveMixPct.Value * (demandDays / totalDays) : 0;
                var proposed = i == 0 ? proposedQty : 0;
                var openStock = i == 0 ? runStock : JsOps.Or(chain[i - 1].CloseStock, 0);
                var closeStock = JsMax0(openStock + ePO + proposed - colPlan);
                chain.Add(new Tool1ChainMonthDto
                {
                    Y = cy, M = cm, MKey = mkey, Label = MonthMath.MonLabel(cy, cm), IsDM = i == 0, IsD1 = i == 1,
                    OpenStock = openStock, EPO = ePO, Proposed = proposed, ColPlan = colPlan, IsCurrMonth = isCurrMonth,
                    DemandDays = demandDays, TotalDays = totalDays, Fc = fc, CloseStock = closeStock, Str = null, Status = null
                });
            }

            // ── STR window: D days from Day 1 of DM+1 ──
            const int d1Idx = 1;
            var winStockStart = JsOps.Or(chain[d1Idx].OpenStock, 0);
            double budget = winDays, windowDemand = 0;
            for (var i = d1Idx; i < chain.Count && budget > 0; i++)
            {
                double days = MonthMath.DaysInMonth(chain[i].Y, chain[i].M);
                var take = Math.Min(days, budget);
                windowDemand += chain[i].ColPlan * (take / days);
                budget -= take;
            }
            var windowDailyRate = winDays > 0 && windowDemand > 0 ? windowDemand / winDays : 0;

            var forecastDict = forecastByMon.ToDictionary();
            if (windowDemand == 0)
            {
                var f4Rows = data.F4?.Count ?? 0;
                var f4Matched = data.F4?.Count(r => MatchRow(r, false)) ?? 0;
                return new Tool1EngineBResult
                {
                    Decision = Decisions.AutoAllow,
                    DecisionReason = "No forecast data found for MC CD " + rMC + " MRP " + rMRP + " in F4. " +
                        "Cannot compute STR — treated as Auto Allow. " +
                        "[DEBUG] forecastByMon keys: " + Tool1JsHelpers.JsonArray(forecastByMon.Keys) + " | " +
                        "F4 rows total: " + f4Rows + " | " +
                        "F4 matched rows: " + f4Matched + " | " +
                        "effectiveMixPct: " + (effectiveMixPct != null ? Js.ToFixed(effectiveMixPct.Value * 100, 4) + "%" : "null") + " | " +
                        "rFlag: " + rFlag + " | rMC: " + rMC + " | rMRP: " + rMRP,
                    MixPct = mixPct, ColAvg = colAvg, McAvg = mcAvg, OpStock = opStock, Chain = chain, MixFailed = false, RecQty = null,
                    HasDailyF3 = hasDailyF3, ArtSTR = 0, WinStockStart = winStockStart, WindowDemand = 0, WindowDailyRate = 0, MaxPermQty = 0
                };
            }

            var artSTR = winStockStart / windowDemand * winDays;
            var buffer = JsOps.Or(p.StrBuffer, 0);
            var effectiveT = T * (1 + buffer / 100);

            for (var i = d1Idx; i < chain.Count; i++)
            {
                var ch = chain[i];
                double rollingDemand = 0, rollingBudget = winDays;
                for (var j = i; j < chain.Count && rollingBudget > 0; j++)
                {
                    double mDays = MonthMath.DaysInMonth(chain[j].Y, chain[j].M);
                    var take = Math.Min(mDays, rollingBudget);
                    rollingDemand += chain[j].ColPlan * (take / mDays);
                    rollingBudget -= take;
                }
                var rollingRate = rollingDemand > 0 ? rollingDemand / winDays : 0;
                ch.Str = rollingRate > 0 ? ch.OpenStock / rollingRate : null;
                ch.Status = ch.Str != null ? (ch.Str <= T * (1 + buffer / 100) ? "OK" : "Breach") : null;
            }

            var hasBreach = artSTR > effectiveT;
            var futureRisk = chain.Select((ch, i) => (ch, i)).Where(x => x.i > d1Idx && x.ch.Status == "Breach")
                                  .Select(x => new Tool1FutureRiskDto { Month = x.ch.MKey, Str = x.ch.Str }).ToList();
            var stockAtD1NoProp = chain[0].OpenStock + chain[0].EPO - chain[0].ColPlan;
            var maxPermQty = Math.Max(0, Math.Floor(effectiveT * windowDailyRate - stockAtD1NoProp));
            if (double.IsNaN(effectiveT * windowDailyRate - stockAtD1NoProp)) maxPermQty = double.NaN;

            string decision, reason;
            double? recQty = null;
            var tTxt = Js.NumberToString(T);
            var bTxt = Js.NumberToString(buffer);
            var effR = Js.NumberToString(Js.Round(effectiveT));
            if (!hasBreach)
            {
                decision = Decisions.AutoAllow;
                reason = "Colour STR = " + Js.NumberToString(Js.Round(artSTR)) + " days ≤ T=" + tTxt + "d + " + bTxt + "% buffer (" + effR +
                         "d). Safe to raise PO at full requested qty of " + Js.Fmt(proposedQty) + " units.";
                if (futureRisk.Count > 0)
                    reason += " [Heads-up: " + RiskLabel(futureRisk[0].Month) + " shows a later STR of " + Js.NumberToString(Js.Round(futureRisk[0].Str ?? double.NaN)) +
                              "d, above target — worth reviewing separately. This did not affect the current decision.]";
            }
            else
            {
                decision = Decisions.Hold;
                recQty = maxPermQty;
                reason = maxPermQty > 0
                    ? "STR exceeds T=" + tTxt + "d + " + bTxt + "% buffer (" + effR + "d). Hold PO. Max Permissible Qty = " + Js.Fmt(maxPermQty) +
                      " units (= " + effR + "d × " + Js.ToFixed(windowDailyRate, 0) + " daily rate − " + Js.Fmt(Js.Round(JsMathMax(0, stockAtD1NoProp))) + " existing stock at D+1)."
                    : "STR exceeds T=" + tTxt + "d + " + bTxt + "% buffer (" + effR + "d). Existing stock already covers " + Js.NumberToString(Js.Round(artSTR)) +
                      " days. Max Permissible Qty = 0. Do not raise any PO.";
                if (futureRisk.Count > 0)
                    reason += " [Separately: " + RiskLabel(futureRisk[0].Month) + " also shows STR of " + Js.NumberToString(Js.Round(futureRisk[0].Str ?? double.NaN)) +
                              "d, above target — worth reviewing on its own.]";
            }
            reason += carryNote;

            return new Tool1EngineBResult
            {
                Decision = decision, DecisionReason = reason, MixPct = mixPct, ColAvg = colAvg, McAvg = mcAvg, OpStock = opStock,
                ProposedQty = proposedQty, RecQty = recQty, Chain = chain, D1Idx = d1Idx, WinStockStart = winStockStart,
                WindowDemand = windowDemand, WindowDailyRate = windowDailyRate, ArtSTR = artSTR, HasBreach = hasBreach,
                MaxPermQty = maxPermQty, MixFailed = false, HasDailyF3 = hasDailyF3, ForecastByMon = forecastDict,
                ExistingPO = existingPO.ToDictionary(), PrevMonthCarryNote = carryNote,
                PrevMonthCarry = new Tool1CarryDto { Qty = carry.Qty, Cutoff = carry.Cutoff, PrevKey = carry.PrevKey, CurrKey = carry.CurrKey },
                FutureRiskMonths = futureRisk, StockAtD1NoProp = stockAtD1NoProp, EffectiveT = effectiveT
            };
        }

        /// <summary><c>monLabel(parseInt(m.split('.')[1]), parseInt(m.split('.')[0]))</c>.</summary>
        private static string RiskLabel(string mkey)
        {
            var parts = mkey.Split('.');
            return MonthMath.MonLabel((int)MonthMath.ParseInt(parts[1]), (int)MonthMath.ParseInt(parts[0]));
        }

        /// <summary>JS <c>Math.max(v, 0)</c> (NaN stays NaN).</summary>
        internal static double JsMax0(double v) => double.IsNaN(v) ? double.NaN : Math.Max(v, 0);

        internal static double JsMathMax(double a, double b) => double.IsNaN(a) || double.IsNaN(b) ? double.NaN : Math.Max(a, b);
    }
}
