using Application.Interfaces.V1.Analytics;
using DecisionEngine.Results.Analytics.Grc;
using Domain.Entities.Request;
using Domain.Entities.Response;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace POGovApi.Controllers.V1
{
    /// <summary>
    /// Planner "📈 GRC Back Testing" screen (index.html view-analytics). Planners and admins only.
    /// </summary>
    [Route("api/v1/[Action]")]
    public sealed class GrcBacktestController : ApiControllerBase
    {
        private const string XlsxContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
        private readonly IGrcBacktestService _grc;
        private readonly IGrcExcelExporter _excel;

        public GrcBacktestController(IGrcBacktestService grc, IGrcExcelExporter excel)
        {
            _grc = grc;
            _excel = excel;
        }

        /// <summary>Control state: "Assess as of" / cutover periods, View-by visibility, filter and drill options.</summary>
        [HttpPost]
        public async Task<ActionResult<ApiDataResponse<GrcControlsDto>>> GrcOptions([FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] GrcImpactRequest? req, CancellationToken ct)
        {
            RequireStaff();
            return Success(await _grc.GetOptionsAsync(req ?? new GrcImpactRequest(), ct));
        }

        /// <summary>Computes the GRC Impact card for the given controls (filters, view mode, as-of period, cutover, drill).</summary>
        [HttpPost]
        public async Task<ActionResult<ApiDataResponse<GrcImpactDto>>> GrcCompute([FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] GrcImpactRequest? req, CancellationToken ct)
        {
            RequireStaff();
            return Success(await _grc.ComputeAsync(req ?? new GrcImpactRequest(), ct));
        }

        /// <summary>Article Trace — week-by-week audit of one GM.</summary>
        [HttpPost]
        public async Task<ActionResult<ApiDataResponse<GrcArticleTraceDto>>> GrcTrace([FromBody] GrcTraceReq req, CancellationToken ct)
        {
            RequireStaff();
            return Success(await _grc.TraceAsync(req.Gm ?? "", req.AsOfPeriod, ct));
        }

        /// <summary>"⬇ Export Row Detail" — GRC_Row_Detail_yyyy-MM-dd.xlsx (toast text in X-Message). 404 "No GRC file loaded" without a GRC file.</summary>
        [HttpPost]
        [Produces(XlsxContentType, "application/json")]
        public async Task<IActionResult> GrcExport([FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] GrcImpactRequest? req, CancellationToken ct)
        {
            RequireStaff();
            var rows = await _grc.GetRowDetailAsync(req?.AsOfPeriod, ct);
            Response.Headers["X-Message"] = Uri.EscapeDataString(rows.Message);
            return File(_excel.WriteRowDetail(rows), XlsxContentType, rows.FileName);
        }
    }
}
