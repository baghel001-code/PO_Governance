using Application.DTOs.Analytics.Grc;
using Application.Interfaces.V1.Analytics;
using Domain.Entities.Response;
using Microsoft.AspNetCore.Mvc;

namespace POGovApi.Controllers.V1
{
    /// <summary>
    /// Planner "📈 GRC Back Testing" screen (index.html view-analytics). Planners and admins only.
    /// </summary>
    [Route("api/v1/grc-backtest")]
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
        [HttpGet("options")]
        public async Task<ActionResult<ApiResponse<GrcControlsDto>>> Options([FromQuery] GrcImpactRequest request, CancellationToken ct)
        {
            RequireStaff();
            return Success(await _grc.GetOptionsAsync(request, ct));
        }

        /// <summary>Computes the GRC Impact card for the given controls (filters, view mode, as-of period, cutover, drill).</summary>
        [HttpPost("compute")]
        public async Task<ActionResult<ApiResponse<GrcImpactDto>>> Compute([FromBody] GrcImpactRequest request, CancellationToken ct)
        {
            RequireStaff();
            return Success(await _grc.ComputeAsync(request ?? new GrcImpactRequest(), ct));
        }

        /// <summary>Article Trace — week-by-week audit of one GM.</summary>
        [HttpGet("trace/{gm}")]
        public async Task<ActionResult<ApiResponse<GrcArticleTraceDto>>> Trace(string gm, [FromQuery] string? asOfPeriod, CancellationToken ct)
        {
            RequireStaff();
            return Success(await _grc.TraceAsync(gm, asOfPeriod, ct));
        }

        /// <summary>"⬇ Export Row Detail" — GRC_Row_Detail_yyyy-MM-dd.xlsx. 404 "No GRC file loaded" without a GRC file.</summary>
        [HttpPost("export")]
        [Produces(XlsxContentType, "application/json")]
        public async Task<IActionResult> Export([FromBody] GrcImpactRequest? request, CancellationToken ct)
        {
            RequireStaff();
            var rows = await _grc.GetRowDetailAsync(request?.AsOfPeriod, ct);
            Response.Headers["X-Message"] = Uri.EscapeDataString(rows.Message);
            return File(_excel.WriteRowDetail(rows), XlsxContentType, rows.FileName);
        }
    }
}
