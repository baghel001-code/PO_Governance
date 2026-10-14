using Application.DTOs.Recommendation;
using Application.Interfaces.V1;
using Application.Interfaces.V1.Recommendation;
using Application.Services.V1.Engines.Common;
using Domain.Constants;
using Domain.Entities.Common;
using Domain.Entities.Engine;
using Domain.Exceptions;

namespace Application.Services.V1.Recommendation
{
    /// <inheritdoc cref="IRecommendationService"/>
    public sealed class RecommendationService : IRecommendationService
    {
        private readonly IDatasetCache _cache;
        private readonly IRequestLedger _ledger;
        private readonly IAppClock _clock;
        private readonly IRecommendationEngine _engine;

        public RecommendationService(IDatasetCache cache, IRequestLedger ledger, IAppClock clock, IRecommendationEngine engine)
        {
            _cache = cache; _ledger = ledger; _clock = clock; _engine = engine;
        }

        public async Task<RecommendationContext> CreateContextAsync(CancellationToken ct = default)
        {
            var data = await _cache.GetCurrentAsync(ct);
            var today = _clock.Today;
            var (requests, _) = await _ledger.GetDayAsync(today, ct);
            return new RecommendationContext(data, requests, today);
        }

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
            if (string.IsNullOrWhiteSpace(req.McCd)) throw new AppValidationException("Enter a Generic Material, or the Brand / MC / MRP / Flag of the bucket.");
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

        public async Task<RecommendationMonthsDto> GetMonthsAsync(RecommendationRequestDto request, CancellationToken ct = default)
        {
            var data = await _cache.GetCurrentAsync(ct);
            var (engine, info) = Resolve(data, request);
            string mc = Js.N(info.McCd), mrp = Js.N(info.Mrp), brand = Js.N(info.Brand), flag = Js.N(info.Flag);
            var all = _engine.BuildMonths(data, mc, mrp, brand, flag);
            return new RecommendationMonthsDto
            {
                Engine = engine, Info = info, AllMonths = all, Months = _engine.DefaultMonths(all, _clock.Today),
                RelevantDim = _engine.AssessDim(data, mc, mrp, brand, flag)
            };
        }

        public async Task<RecommendationViewDto> GetAsync(RecommendationRequestDto request, CancellationToken ct = default)
        {
            var ctx = await CreateContextAsync(ct);
            return Build(_engine, ctx, request);
        }

        /// <summary>
        /// Port of <c>_openRec</c> + <c>_render</c> (index.html lines 10862 / 11121) for one context —
        /// public and synchronous so tests and other callers can use it without the I/O dependencies.
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

        public async Task<RecommendationExportDto> BuildExportAsync(RecommendationRequestDto request, CancellationToken ct = default)
        {
            var ctx = await CreateContextAsync(ct);
            var view = Build(_engine, ctx, request);
            if (view.Error != null) throw new AppValidationException(view.Error);
            if (view.RenderEngine != "A" || view.A == null || view.A.Recs == null || view.A.Recs.Count == 0)
                throw new AppValidationException("No recommendation rows to export");
            return _engine.BuildExport(ctx, view.A, _clock.Now.UtcDateTime.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture));
        }
    }
}
