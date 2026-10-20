using DecisionEngine.Common;
using DecisionEngine.Model;
using DecisionEngine.Results.Recommendation;

namespace DecisionEngine.Recommendation
{
    /// <summary>
    /// The "View Recommendation" modal as index.html <c>openRec</c> / <c>_openRec</c> / <c>_render</c> built it,
    /// over a <see cref="RecommendationContext"/> (snapshot + today's ledger + date). Pure: callers load the context.
    /// </summary>
    public static class RecommendationView
    {
        /// <summary>
        /// The card's engine and <c>_recInfo</c>, as Tool 1 derived them: a GM is resolved with
        /// <c>resolveGM</c> and routed with <c>routeRequest</c> (in Universe Master → B, else A / NEW);
        /// without a GM the bucket is taken from the request (engine: as given, else B when Universe
        /// Master holds the bucket, else A).
        /// </summary>
        public static (string Engine, RecommendationInfoDto Info) Resolve(EngineData data, RecommendationRequestDto req)
        {
            if (!string.IsNullOrWhiteSpace(req.Gm))
            {
                // v1.1.4 behaviour: an unresolved GM (a "new article" card) still opens the modal, with an
                // empty bucket (every filter blank → _match lets every row through).
                var gi = ResolveIndex.ResolveGm(data, req.Gm);
                var engine = !string.IsNullOrEmpty(req.Engine) ? req.Engine! : ResolveIndex.RouteRequest(data, req.Gm, null).Engine;
                return (engine, gi == null ? new RecommendationInfoDto()
                    : new RecommendationInfoDto { McCd = gi.McCd, Mrp = gi.Mrp, Brand = gi.Brand, Flag = gi.Flag, Div = gi.Div, McDesc = gi.McDesc });
            }
            if (string.IsNullOrWhiteSpace(req.McCd)) throw new EngineValidationException("Enter a Generic Material, or the Brand / MC / MRP / Flag of the bucket.");
            var info = new RecommendationInfoDto
            {
                McCd = req.McCd ?? "", Mrp = req.Mrp ?? "", Brand = req.Brand ?? "", Flag = req.Flag ?? "", Div = req.Div ?? "", McDesc = req.McDesc ?? ""
            };
            var eng = req.Engine;
            if (string.IsNullOrEmpty(eng))
            {
                var inUm = data.Um != null && data.Um.Any(r => RecommendationData.Match(r, Js.N(info.McCd), Js.N(info.Mrp), Js.N(info.Brand), Js.N(info.Flag), null));
                eng = inUm ? "B" : "A";
            }
            return (eng!, info);
        }

        /// <summary>The month bar only: every F4 month of the bucket + the default selection for <paramref name="today"/>.</summary>
        public static RecommendationMonthsDto Months(IRecommendationEngine engine, EngineData data, RecommendationRequestDto request, string today)
        {
            var (eng, info) = Resolve(data, request);
            string mc = Js.N(info.McCd), mrp = Js.N(info.Mrp), brand = Js.N(info.Brand), flag = Js.N(info.Flag);
            var all = engine.BuildMonths(data, mc, mrp, brand, flag);
            return new RecommendationMonthsDto
            {
                Engine = eng, Info = info, AllMonths = all, Months = engine.DefaultMonths(all, today),
                RelevantDim = engine.AssessDim(data, mc, mrp, brand, flag)
            };
        }

        /// <summary>
        /// Port of <c>_openRec</c> + <c>_render</c> (index.html lines 10862 / 11121) for one context.
        /// </summary>
        public static RecommendationViewDto Build(IRecommendationEngine engine, RecommendationContext ctx, RecommendationRequestDto request)
        {
            var data = ctx.Data;
            var (eng, info) = Resolve(data, request);
            string mc = Js.N(info.McCd), mrp = Js.N(info.Mrp), brand = Js.N(info.Brand), flag = Js.N(info.Flag);
            var view = new RecommendationViewDto
            {
                Engine = eng, RenderEngine = eng == "B" ? "B" : "A", Info = info,
                RelevantDim = engine.AssessDim(data, mc, mrp, brand, flag),
                DimOverride = string.IsNullOrEmpty(request.DimOverride) ? null : request.DimOverride
            };

            var errs = new List<string>();
            if (data.Get(DatasetKeys.F1) == null) errs.Add("F1 (Stock)");
            if (data.Get(DatasetKeys.F3) == null) errs.Add("F3 (Sales Trend)");
            if (data.Get(DatasetKeys.F4) == null) errs.Add("F4 (Forecast)");
            if (eng == "A")
            {
                if (data.Get(DatasetKeys.ArtMaster) == null) errs.Add("Article Master (required for Multi Concept MRP — provides Style/Fit/Product Material/Design per Generic Material)");
                if (data.Get(DatasetKeys.AttrRel) == null) errs.Add("Attribute Relevance Master (required for Multi Concept MRP recommendation)");
            }
            if (errs.Count > 0)
            {
                view.Error = "Cannot generate recommendation.\n\nMissing files:\n• " + string.Join("\n• ", errs);
                return view;
            }

            view.Title = eng == "B" ? "📊 Colour Recommendation — Single Concept MRP" : "📊 Attribute & Colour Recommendation — Multi Concept MRP";
            view.Subtitle = info.Div + (info.McDesc.Length > 0 ? " · " + info.McDesc : "") + (info.Mrp.Length > 0 ? " · ₹" + info.Mrp : "") + " · " + info.Flag;
            view.AllMonths = engine.BuildMonths(data, mc, mrp, brand, flag);
            view.Months = request.Months is { Count: > 0 }
                ? MonthMath.SortByOrdinal(request.Months.Where(m => !string.IsNullOrWhiteSpace(m)).Select(m => m.Trim()).Distinct())
                : engine.DefaultMonths(view.AllMonths, ctx.Today);
            view.DimOptions = engine.DimOverrideOptions(view.RelevantDim, view.DimOverride);

            if (eng == "B")
            {
                view.B = engine.RunB(ctx, mc, mrp, brand, flag, view.Months);
                view.Paint = engine.Paint(ctx.P, view.B, null);
            }
            else
            {
                view.A = engine.RunA(ctx, mc, mrp, brand, flag, view.Months, view.DimOverride);
                if (view.A.Err == null && view.A.DimMode == "colour-only")
                {
                    // _render: "Rerun as Single Concept MRP" — painted as the colour table.
                    view.RenderEngine = "B";
                    view.B = view.A.Data;
                    view.Paint = engine.Paint(ctx.P, view.B, null);
                }
                else view.Paint = engine.Paint(ctx.P, null, view.A);
            }
            return view;
        }

        /// <summary>
        /// The full-detail Excel export of the modal (Multi Concept only, <c>_exportEngineARecs</c>). Throws
        /// <see cref="EngineValidationException"/> with the v1.1.4 texts when there is nothing to export.
        /// </summary>
        public static RecommendationExportDto Export(IRecommendationEngine engine, RecommendationContext ctx, RecommendationRequestDto request, string fileDate)
        {
            var view = Build(engine, ctx, request);
            if (view.Error != null) throw new EngineValidationException(view.Error);
            if (view.RenderEngine != "A" || view.A == null || view.A.Recs == null || view.A.Recs.Count == 0)
                throw new EngineValidationException("No recommendation rows to export");
            return engine.BuildExport(ctx, view.A, fileDate);
        }
    }
}
