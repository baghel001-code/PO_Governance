using Application.Interfaces.V1.Recommendation;
using DecisionEngine.Results.Recommendation;
using Domain.Entities.Response;
using Microsoft.AspNetCore.Mvc;

namespace POGovApi.Controllers.V1
{
    /// <summary>
    /// "📊 View Recommendation" (index.html openRec / _openRec / _render / _exportEngineARecs).
    /// Open to every signed-in role (admin, planner, cathead, buyer).
    /// </summary>
    [Route("api/v1/[Action]")]
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
        public async Task<ActionResult<ApiDataResponse<RecommendationViewDto>>> RecommendationGet([FromBody] RecommendationRequestDto request, CancellationToken ct) =>
            Success(await _service.GetAsync(request, ct));

        /// <summary>The month bar: every F4 month of the bucket and the default selection.</summary>
        [HttpPost]
        public async Task<ActionResult<ApiDataResponse<RecommendationMonthsDto>>> RecommendationMonths([FromBody] RecommendationRequestDto request, CancellationToken ct) =>
            Success(await _service.GetMonthsAsync(new RecommendationRequestDto
            {
                Gm = request.Gm, Brand = request.Brand, McCd = request.McCd, Mrp = request.Mrp, Flag = request.Flag, Engine = request.Engine
            }, ct));

        /// <summary>"⬇ Download Excel (all columns)" — the 5-sheet full-detail workbook (Multi Concept).</summary>
        [HttpPost]
        [Produces("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "application/json")]
        public async Task<IActionResult> RecommendationExport([FromBody] RecommendationRequestDto request, CancellationToken ct)
        {
            var export = await _service.BuildExportAsync(request, ct);
            var bytes = _excel.Write(export);
            return File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", export.FileName);
        }
    }
}
