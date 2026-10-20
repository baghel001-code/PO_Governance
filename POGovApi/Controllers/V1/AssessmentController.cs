using Application.Interfaces.V1.Tool2;
using DecisionEngine.Results.Tool2;
using Domain.Entities.Response;
using Microsoft.AspNetCore.Mvc;

namespace POGovApi.Controllers.V1
{
    /// <summary>
    /// Tool 2 — "📋 PO Assessment" (index.html runAssessment / renderAssessmentDashboard / exportAss*,
    /// shared.js saveRun / enterHistoryRun). Planners and admins only.
    /// </summary>
    [Route("api/v1/assessment")]
    public sealed class AssessmentController : ApiControllerBase
    {
        private readonly ITool2Service _service;

        public AssessmentController(ITool2Service service) => _service = service;

        /// <summary>Runs the assessment on the current data, saves the run to History and returns it.</summary>
        [HttpPost("run")]
        public async Task<ActionResult<ApiResponse<Tool2RunResultDto>>> Run(CancellationToken ct)
        {
            RequireStaff();
            return Success(await _service.RunAsync(Me, ct));
        }

        /// <summary>Re-opens a saved run (v1.1.4 or this version): the dashboard as it was.</summary>
        [HttpGet("runs/{id}")]
        public async Task<ActionResult<ApiResponse<Tool2ViewDto>>> GetRun(string id, CancellationToken ct)
        {
            RequireStaff();
            return Success(await _service.GetRunAsync(id, ct));
        }

        /// <summary>The detail view of a run for a tab (B / A) and filter.</summary>
        [HttpPost("runs/{id}/detail")]
        public async Task<ActionResult<ApiResponse<Tool2DetailViewDto>>> Detail(string id, [FromBody] Tool2DetailFilterDto filter, CancellationToken ct)
        {
            RequireStaff();
            return Success(await _service.GetDetailAsync(id, filter ?? new Tool2DetailFilterDto(), ct));
        }

        /// <summary>The Vendor and Division × MC tables of a run for a filter.</summary>
        [HttpPost("runs/{id}/rollup")]
        public async Task<ActionResult<ApiResponse<Tool2RollupDto>>> Rollup(string id, [FromBody] Tool2RollupFilterDto filter, CancellationToken ct)
        {
            RequireStaff();
            return Success(await _service.GetRollupAsync(id, filter ?? new Tool2RollupFilterDto(), ct));
        }

        /// <summary>Parameters, assessable F2 lines, and (for a run) filter lists and Category Heads.</summary>
        [HttpGet("options")]
        public async Task<ActionResult<ApiResponse<Tool2OptionsDto>>> Options([FromQuery] string? runId, CancellationToken ct)
        {
            RequireStaff();
            return Success(await _service.GetOptionsAsync(runId, ct));
        }

        /// <summary>Excel (default) or CSV of a run; several Category Heads → one workbook each, zipped.</summary>
        [HttpPost("export")]
        [Produces("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "text/csv", "application/zip", "application/json")]
        public async Task<IActionResult> Export([FromBody] Tool2ExportRequestDto request, CancellationToken ct)
        {
            RequireStaff();
            var f = await _service.ExportAsync(request, ct);
            if (f.Message != null) Response.Headers["X-Export-Message"] = Uri.EscapeDataString(f.Message);
            return File(f.Content, f.ContentType, f.FileName);
        }
    }
}
