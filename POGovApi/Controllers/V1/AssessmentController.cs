using Application.DTOs.Requests;
using Application.Interfaces.V1.Tool2;
using DecisionEngine.Results.Tool2;
using Domain.Entities.Request;
using Domain.Entities.Response;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace POGovApi.Controllers.V1
{
    /// <summary>
    /// Tool 2 — "📋 PO Assessment" (index.html runAssessment / renderAssessmentDashboard / exportAss*,
    /// shared.js saveRun / enterHistoryRun). Planners and admins only.
    /// </summary>
    [Route("api/v1/[Action]")]
    public sealed class AssessmentController : ApiControllerBase
    {
        private readonly ITool2Service _service;

        public AssessmentController(ITool2Service service) => _service = service;

        /// <summary>Runs the assessment on the current data, saves the run to History and returns it.</summary>
        [HttpPost]
        public async Task<ActionResult<ApiDataResponse<Tool2RunResultDto>>> Tool2Run(CancellationToken ct)
        {
            RequireStaff();
            return Success(await _service.RunAsync(Me, ct));
        }

        /// <summary>Re-opens a saved run (v1.1.4 or this version): the dashboard as it was.</summary>
        [HttpPost]
        public async Task<ActionResult<ApiDataResponse<Tool2ViewDto>>> Tool2GetRun([FromBody] RunIdReq req, CancellationToken ct)
        {
            RequireStaff();
            return Success(await _service.GetRunAsync(req.RunId ?? "", ct));
        }

        /// <summary>The detail view of a run for a tab (B / A) and filter.</summary>
        [HttpPost]
        public async Task<ActionResult<ApiDataResponse<Tool2DetailViewDto>>> Tool2Detail([FromBody] Tool2DetailReq req, CancellationToken ct)
        {
            RequireStaff();
            return Success(await _service.GetDetailAsync(req.RunId ?? "", req.Filter ?? new Tool2DetailFilterDto(), ct));
        }

        /// <summary>The Vendor and Division × MC tables of a run for a filter.</summary>
        [HttpPost]
        public async Task<ActionResult<ApiDataResponse<Tool2RollupDto>>> Tool2Rollup([FromBody] Tool2RollupReq req, CancellationToken ct)
        {
            RequireStaff();
            return Success(await _service.GetRollupAsync(req.RunId ?? "", req.Filter ?? new Tool2RollupFilterDto(), ct));
        }

        /// <summary>Parameters, assessable F2 lines, and (for a run) filter lists and Category Heads.</summary>
        [HttpPost]
        public async Task<ActionResult<ApiDataResponse<Tool2OptionsDto>>> Tool2Options([FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] RunIdReq? req, CancellationToken ct)
        {
            RequireStaff();
            return Success(await _service.GetOptionsAsync(req?.RunId, ct));
        }

        /// <summary>Excel (default) or CSV of a run; several Category Heads → one workbook each, zipped. The toast text is in X-Export-Message.</summary>
        [HttpPost]
        [Produces("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "text/csv", "application/zip", "application/json")]
        public async Task<IActionResult> Tool2Export([FromBody] Tool2ExportRequestDto req, CancellationToken ct)
        {
            RequireStaff();
            var f = await _service.ExportAsync(req, ct);
            if (f.Message != null) Response.Headers["X-Export-Message"] = Uri.EscapeDataString(f.Message);
            return File(f.Content, f.ContentType, f.FileName);
        }
    }
}
