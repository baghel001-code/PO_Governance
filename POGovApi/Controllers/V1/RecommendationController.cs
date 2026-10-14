using Application.DTOs.Recommendation;
using Application.Interfaces.V1.Recommendation;
using Domain.Entities.Response;
using Microsoft.AspNetCore.Mvc;

namespace POGovApi.Controllers.V1
{
    /// <summary>
    /// "📊 View Recommendation" (index.html openRec / _openRec / _render / _exportEngineARecs).
    /// Open to every signed-in role (admin, planner, cathead, buyer).
    /// </summary>
    [Route("api/v1/recommendation")]
    public sealed class RecommendationController : ApiControllerBase
    {
        private readonly IRecommendationService _service;
        private readonly IRecommendationExcelExporter _excel;

        public RecommendationController(IRecommendationService service, IRecommendationExcelExporter excel)
        {
            _service = service;
            _excel = excel;
        }

        /// <summary>
        /// The whole recommendation for a GM (resolved as Tool 1 does) or a Brand / MC / MRP / Flag bucket,
        /// for the selected months (default: the next 4 F4 months) and an optional dimension override.
        /// </summary>
        [HttpPost]
        public async Task<ActionResult<ApiResponse<RecommendationViewDto>>> Get([FromBody] RecommendationRequestDto request, CancellationToken ct) =>
            Success(await _service.GetAsync(request, ct));

        /// <summary>The month bar: every F4 month of the bucket and the default selection.</summary>
        [HttpGet("months")]
        public async Task<ActionResult<ApiResponse<RecommendationMonthsDto>>> Months([FromQuery] string? gm, [FromQuery] string? brand,
            [FromQuery] string? mcCd, [FromQuery] string? mrp, [FromQuery] string? flag, [FromQuery] string? engine, CancellationToken ct) =>
            Success(await _service.GetMonthsAsync(new RecommendationRequestDto { Gm = gm, Brand = brand, McCd = mcCd, Mrp = mrp, Flag = flag, Engine = engine }, ct));

        /// <summary>"⬇ Download Excel (all columns)" — the 5-sheet full-detail workbook (Multi Concept).</summary>
        [HttpPost("export")]
        [Produces("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "application/json")]
        public async Task<IActionResult> Export([FromBody] RecommendationRequestDto request, CancellationToken ct)
        {
            var export = await _service.BuildExportAsync(request, ct);
            var bytes = _excel.Write(export);
            return File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", export.FileName);
        }
    }
}
