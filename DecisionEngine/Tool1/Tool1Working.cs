using System.Text.Json;
using System.Text.Json.Nodes;
using DecisionEngine.Model;
using DecisionEngine.Results.Tool1;

namespace DecisionEngine.Tool1
{
    /// <summary>
    /// v1.1.7 "Download working (JSON)" (app/mra.js <c>M.working</c>, SAP-MRA-INTERFACE.txt §5): every number behind a single
    /// concept New PO request answer — the same fields SAP may send back as an MRA's <c>working</c>.
    /// </summary>
    public static class Tool1Working
    {
        private static JsonNode? N(object? v) => v == null ? null : JsonSerializer.SerializeToNode(v);

        public static JsonObject Build(Tool1Outcome r, EngineParams p, string today, Tool1LaterCautionDto? caution)
        {
            var i = r.Info;
            var w = new JsonObject
            {
                ["decision"] = r.Decision, ["reason"] = r.DecisionReason ?? "", ["article"] = r.Gm, ["colour"] = r.Colour, ["deliveryMonth"] = r.Month,
                ["askedQty"] = r.Qty,
                ["group"] = i == null ? null : new JsonObject { ["brand"] = i.Brand, ["mc"] = i.McCd, ["mcDesc"] = i.McDesc, ["mrp"] = i.Mrp, ["flag"] = i.Flag },
                ["dataAsOf"] = today
            };
            var b = r.B;
            if (r.Engine == "B" && b is { Chain.Count: > 0 })
            {
                var eff = (p.T1 > 0 ? p.T1 : 75) * (1 + p.StrBuffer / 100);
                w["stockToday"] = b.OpStock;
                w["colourShare"] = b.EffMix ?? b.MixPct;
                w["windowDays"] = p.D > 0 ? p.D : 75;
                w["windowStart"] = b.Chain.Count > 1 ? b.Chain[1].MKey : null;
                w["windowDailyRate"] = b.WindowDailyRate;
                w["limitDays"] = eff;
                w["stockNeededAtWindowStart"] = eff * (b.WindowDailyRate ?? 0);
                w["stockAtWindowStart"] = b.WinStockStart;
                w["maxPermissible"] = b.MaxPermQty;
                var months = new JsonArray();
                foreach (var x in b.PreRoll ?? new())
                    months.Add(new JsonObject { ["month"] = x.MKey, ["open"] = x.Open, ["incoming"] = x.Po, ["planSale"] = x.Sale, ["close"] = x.Close, ["daysCounted"] = x.Counted });
                foreach (var x in b.Chain.Take(5))
                    months.Add(new JsonObject { ["month"] = x.MKey, ["open"] = x.OpenStock, ["incoming"] = x.EPO, ["asked"] = x.Proposed, ["planSale"] = x.ColPlan, ["close"] = x.CloseStock });
                w["monthByMonth"] = months;
                w["incomingParts"] = new JsonObject
                {
                    ["releasedPOs"] = N(b.F2ByMon), ["openMRAs"] = N(b.MraIn?.ByMon ?? new Dictionary<string, long>()),
                    ["approvedInToolToday"] = N(b.LedgerByMon ?? new Dictionary<string, double>())
                };
                var items = new JsonArray();
                foreach (var it in b.MraIn?.Items ?? new())
                    items.Add(it.Type == "PO"
                        ? new JsonObject { ["type"] = "PO", ["no"] = it.No, ["mon"] = it.Mon, ["qty"] = it.Qty, ["status"] = it.Status }
                        : new JsonObject { ["type"] = "MRA", ["no"] = it.No, ["mon"] = it.Mon, ["qty"] = it.Qty, ["level"] = it.Level, ["live"] = it.Live });
                w["openMRAsCounted"] = items;
            }
            if (caution != null) w["caution"] = N(caution);
            return w;
        }
    }
}
