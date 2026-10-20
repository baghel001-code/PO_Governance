using System.Text.Json.Nodes;
using DecisionEngine.Analytics.Common;
using DecisionEngine.Common;
using DecisionEngine.Results.Analytics.CatInsights;
using DecisionEngine.Results.Analytics.Common;

namespace DecisionEngine.Analytics.Insights
{
    /// <summary>
    /// Port of index.html <c>renderCatInsights</c> (line 8622) with <c>_populateSelectPreserving</c>,
    /// <c>classifyReasonBucket</c> and <c>_decColor</c>: KPI strip, decision donut, driver bars, MC risk
    /// table, flagged requests and the auto-generated recommendations — over the requests the user may see.
    /// </summary>
    public sealed class CategoryInsightsBuilder
    {
        public static readonly string[] DecisionOrder = { "Auto Allow", "Allow", "Watch List", "Recommend Reduced Qty", "Block", "Hold — Don't Raise", "Cannot Validate" };
        public static readonly List<OptionDto> EngineOptions = new()
        {
            new("", "All Engines"), new("B", "Single Concept MRP"), new("A", "Multi Concept MRP"), new("NEW", "New Article")
        };
        public static readonly List<OptionDto> DecisionOptions = new()
        {
            new("", "All Decisions"), new("Auto Allow", "Auto Allow"), new("Allow", "Allow"), new("Watch List", "Watch List"),
            new("Block", "Block"), new("Hold — Don't Raise", "Hold — Don't Raise"), new("Cannot Validate", "Cannot Validate")
        };

        private static string Brand(JsonObject r) => AnalyticsJs.InfoStr(r, "brand", "NON-PL");
        private static string Div(JsonObject r) => AnalyticsJs.InfoStr(r, "div", "—");
        private static string Mc(JsonObject r) => AnalyticsJs.InfoStr(r, "mcDesc", "Unclassified");
        private static string Month(JsonObject r) => AnalyticsJs.OrStr(r, "month", "—");
        private static string? Str(JsonObject r, string key) => AnalyticsJs.Get(r, key) as string;

        public CatInsightsDto Build(IReadOnlyList<JsonObject> allReqs, CatInsightsQuery? query)
        {
            query ??= new CatInsightsQuery();
            var dto = new CatInsightsDto { TotalAll = allReqs.Count };
            dto.Options.Engines = EngineOptions;
            dto.Options.Decisions = DecisionOptions;
            if (allReqs.Count == 0)
            {
                dto.State = "noRequests";
                dto.EmptyTitle = "No insights yet";
                dto.EmptyMessage = "Validate a few PO requests first — insights build automatically from your session's decisions.";
                dto.EmptyAction = "New Request →";
                return dto;
            }

            // filter dropdowns from the full universe
            dto.Options.Brands = AnalyticsJs.SortStrings(AnalyticsJs.Distinct(allReqs.Select(Brand)));
            dto.Options.Divisions = AnalyticsJs.SortStrings(AnalyticsJs.Distinct(allReqs.Select(Div)));
            dto.Options.Mcs = AnalyticsJs.SortStrings(AnalyticsJs.Distinct(allReqs.Select(Mc)));
            dto.Options.Months = AnalyticsJs.SortStrings(AnalyticsJs.Distinct(allReqs.Select(Month)));

            var fBrand = DashboardMath.Preserve(query.Brand, dto.Options.Brands);
            var fDiv = DashboardMath.Preserve(query.Division, dto.Options.Divisions);
            var fMc = DashboardMath.Preserve(query.Mc, dto.Options.Mcs);
            var fMonth = DashboardMath.Preserve(query.Month, dto.Options.Months);
            var fEngine = DashboardMath.StaticSelect(query.Engine, EngineOptions);
            var fDecision = DashboardMath.StaticSelect(query.Decision, DecisionOptions);
            dto.Applied = new CatInsightsQuery { Brand = fBrand, Division = fDiv, Mc = fMc, Month = fMonth, Engine = fEngine, Decision = fDecision, From = query.From, To = query.To };

            var reqs = allReqs.Where(r =>
            {
                if (fBrand.Length > 0 && Brand(r) != fBrand) return false;
                if (fDiv.Length > 0 && Div(r) != fDiv) return false;
                if (fMc.Length > 0 && Mc(r) != fMc) return false;
                if (fMonth.Length > 0 && Month(r) != fMonth) return false;
                if (fEngine.Length > 0 && Str(r, "engine") != fEngine) return false;
                if (fDecision.Length > 0 && Str(r, "decision") != fDecision) return false;
                return true;
            }).ToList();
            dto.FilterActive = fBrand.Length + fDiv.Length + fMc.Length + fMonth.Length + fEngine.Length + fDecision.Length > 0;

            if (reqs.Count == 0)
            {
                dto.State = "noMatch";
                dto.EmptyTitle = "No requests match these filters";
                dto.EmptyMessage = allReqs.Count + " request" + (allReqs.Count > 1 ? "s" : "") + " exist this session, but none match the current filter combination.";
                dto.EmptyAction = "Clear Filters";
                return dto;
            }

            var total = reqs.Count;
            dto.Total = total;
            var decCounts = new JsObjectMap<int>();
            foreach (var r in reqs) { var k = AnalyticsJs.OrStr(r, "decision", "—"); decCounts[k] = decCounts.Get(k) + 1; }
            int C(string d) => decCounts.Get(d);
            double qtyRequested = 0, qtyApproved = 0;
            foreach (var r in reqs) qtyRequested += AnalyticsJs.OrZero(r, "qty");
            foreach (var r in reqs)
            {
                if (Str(r, "engine") == "B")
                {
                    var a = LedgerHelpers.ApprovedQtyFor(r);
                    qtyApproved += a is { } x && x != 0 && !double.IsNaN(x) ? x : 0;
                }
                else if (Str(r, "decision") is "Auto Allow" or "Allow") qtyApproved += AnalyticsJs.OrZero(r, "qty");
            }
            var blockCt = C("Block") + C("Hold — Don't Raise");
            var watchCt = C("Watch List");
            var allowCt = C("Auto Allow") + C("Allow");
            var blockRate = Js.Round((double)blockCt / total * 100);
            var watchRate = Js.Round((double)watchCt / total * 100);
            var allowRate = Js.Round((double)allowCt / total * 100);
            var headroomPct = qtyRequested > 0 ? Js.Round(qtyApproved / qtyRequested * 100) : 0;
            string N(double v) => Js.NumberToString(v);

            dto.Kpis = new List<KpiTileDto>
            {
                new() { Label = "Total Requests", Value = total + (dto.FilterActive ? " / " + allReqs.Count : ""), Color = "#1A4FAF", Background = "#EAF0FB" },
                new() { Label = "Auto Allow", Value = allowCt + " (" + N(allowRate) + "%)", Color = "#00873E", Background = "#DCFCE7" },
                new() { Label = "Watch List", Value = watchCt + " (" + N(watchRate) + "%)", Color = "#B45309", Background = "#FFF3CD" },
                new() { Label = "Blocked / Hold", Value = blockCt + " (" + N(blockRate) + "%)", Color = "#DC2626", Background = "#FEF2F2" },
                new() { Label = "Qty Requested", Value = Js.Fmt(qtyRequested), Color = "#12213D", Background = "#F5F8FF" },
                new() { Label = "Qty Approved", Value = Js.Fmt(qtyApproved), Color = "#7C3AED", Background = "#F5F3FF" },
                new() { Label = "Headroom Utilised", Value = N(headroomPct) + "%", Color = "#00AEEF", Background = "#E0F6FF" }
            };

            dto.DecisionMix = new DecisionMixDto
            {
                Title = "📊 Decision Mix" + (dto.FilterActive ? " (filtered)" : ""),
                Segments = DashboardMath.Donut(DecisionOrder, C, total, d => { var c = DashboardMath.DecColor(d); return (c.C, c.Bg, c.Bd); })
            };

            // drivers of the flagged (non-allow) requests
            var flagged = reqs.Where(r => Str(r, "decision") is not ("Auto Allow" or "Allow")).ToList();
            var reasonCounts = new JsObjectMap<int>();
            foreach (var r in flagged) { var b = DashboardMath.ClassifyReasonBucket(r); reasonCounts[b] = reasonCounts.Get(b) + 1; }
            var reasonRows = DashboardMath.DriverRows(reasonCounts, flagged.Count);
            dto.Drivers = new DriverBreakdownDto
            {
                Title = "🔎 What's Driving Watch List / Block Decisions", Rows = reasonRows,
                EmptyMessage = reasonRows.Count > 0 ? null : "No Watch List or Block decisions in this view — every matching request has cleared the engine at full quantity. 🎉"
            };

            // MC risk table
            var mcMap = new JsObjectMap<CatMcRiskRowDto>();
            foreach (var r in reqs)
            {
                var mc = Mc(r);
                if (!mcMap.TryGetValue(mc, out var m)) mcMap[mc] = m = new CatMcRiskRowDto { Mc = mc };
                m.Requests++;
                m.Qty += AnalyticsJs.OrZero(r, "qty");
                var d = Str(r, "decision");
                if (d is "Auto Allow" or "Allow") m.Allow++;
                else if (d is "Watch List" or "Recommend Reduced Qty") m.Watch++;
                else if (d is "Block" or "Hold — Don't Raise") m.Block++;
            }
            var mcRowsAll = mcMap.Values.ToList();
            JsSort.StableSort(mcRowsAll, (a, b) => (double)(b.Block + b.Watch) / b.Requests - (double)(a.Block + a.Watch) / a.Requests);
            foreach (var m in mcRowsAll)
            {
                m.RiskPct = Js.Round((double)(m.Block + m.Watch) / m.Requests * 100);
                m.RiskColor = DashboardMath.RiskColor(m.RiskPct);
                m.QtyText = Js.Fmt(m.Qty);
            }
            dto.McRisk = new CatMcRiskTableDto
            {
                Title = "Risk by Merchandise Category (MC)" + (mcRowsAll.Count > 20 ? " — top 20 of " + mcRowsAll.Count : ""),
                TotalMcs = mcRowsAll.Count, Rows = mcRowsAll.Take(20).ToList()
            };

            // flagged detail, most recent first
            if (flagged.Count > 0)
            {
                dto.Flagged = new CatFlaggedTableDto
                {
                    Title = "Flagged Requests — Needs Attention Before Raising" + (flagged.Count > 25 ? " (most recent 25 of " + flagged.Count + ")" : ""),
                    TotalFlagged = flagged.Count,
                    Rows = Enumerable.Reverse(flagged).Take(25).Select(r => new CatFlaggedRowDto
                    {
                        Id = AnalyticsJs.Concat(r, "id"), ReqId = Str(r, "reqId"), Gm = AnalyticsJs.Concat(r, "gm"), Colour = AnalyticsJs.Concat(r, "colour"),
                        McDesc = AnalyticsJs.InfoStr(r, "mcDesc", "—"), Decision = AnalyticsJs.Concat(r, "decision"),
                        DecisionPill = DashboardMath.DecisionPill(Str(r, "decision")), Driver = DashboardMath.ClassifyReasonBucket(r)
                    }).ToList()
                };
            }

            // recommendations
            var recs = new List<string>();
            if (blockRate >= 30)
            {
                var topReason = reasonRows.Count > 0 ? reasonRows[0].Label : null;
                recs.Add("🚨 <strong>" + N(blockRate) + "% of requests were Blocked or put on Hold</strong> in this view" + (topReason != null ? " — the leading driver is <strong>" + topReason + "</strong>" : "") + ". Raising POs against these articles/colours is not recommended without a sell-through or vendor review.");
            }
            else if (blockCt > 0)
                recs.Add("⚠️ " + blockCt + " request" + (blockCt > 1 ? "s were" : " was") + " Blocked or put on Hold. Review these before resubmitting — see \"Flagged Requests\" below for the exact driver.");
            if (watchRate >= 25)
                recs.Add("👀 <strong>" + N(watchRate) + "% of requests landed on Watch List</strong>, needing Category Head sign-off. Consider batching these for a single review session rather than raising them one-by-one.");
            var riskiest = mcRowsAll.FirstOrDefault(v => v.Requests >= 2 && (double)(v.Block + v.Watch) / v.Requests >= 0.4);
            if (riskiest != null)
                recs.Add("📌 <strong>" + riskiest.Mc + "</strong> is the riskiest MC in this view — " + N(riskiest.RiskPct) + "% of its requests were Watch List or Block. Before raising more POs here, check whether the underlying sell-through problem is category-wide or vendor/style specific.");
            if (qtyRequested > 0 && headroomPct < 70)
                recs.Add("💰 Only <strong>" + N(headroomPct) + "%</strong> of requested quantity (" + Js.Fmt(qtyApproved) + " of " + Js.Fmt(qtyRequested) + " units) was actually approved — the rest is held back by stock-cover limits. Re-check proposed quantities against Max Permissible before resubmitting to avoid repeat holds.");
            var repeat = new JsObjectMap<int>();
            foreach (var r in flagged) { var k = AnalyticsJs.Concat(r, "gm") + "·" + AnalyticsJs.Concat(r, "colour"); repeat[k] = repeat.Get(k) + 1; }
            var repeatKey = repeat.FirstOrDefault(kv => kv.Value > 1).Key;
            if (repeatKey != null)
            {
                var i = repeatKey.IndexOf('·');
                var shown = repeatKey[..i] + " / " + repeatKey[(i + 1)..];
                recs.Add("🔁 <strong>" + shown + "</strong> was flagged more than once in this view — repeated attempts at the same qty are unlikely to change the outcome without a real change in sell-through.");
            }
            if (recs.Count == 0)
                recs.Add("✅ Everything in this view is Auto Allow / Allow — no red flags detected. Keep raising with confidence.");
            dto.Recommendations = new RecommendationsCardDto { Title = "💡 Recommendations for the Category Team", Items = recs.Select(DashboardMath.Rec).ToList() };
            return dto;
        }
    }
}
