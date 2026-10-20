using Application.Interfaces.V1.Tool1;
using DecisionEngine.Results.Tool1;
using Domain.Constants;
using Domain.Entities.Response;
using Microsoft.AspNetCore.Mvc;

namespace POGovApi.Controllers.V1
{
    /// <summary>
    /// Tool 1 — "✏️ New Request" of the Category view (index.html formGMInput / formSubmit / renderResult,
    /// shared.js saveRequest). For buyers, category heads, planners and admins (server.py api_POST_requests).
    /// </summary>
    [Route("api/v1/tool1")]
    public sealed class Tool1Controller : ApiControllerBase
    {
        private const string NotAllowed = "Not allowed";
        private readonly ITool1Service _service;

        public Tool1Controller(ITool1Service service) => _service = service;

        /// <summary>The form for a GM: attribute strip, engine route, colour options and the 18 delivery months.</summary>
        [HttpGet("options")]
        public async Task<ActionResult<ApiResponse<Tool1FormOptionsDto>>> Options([FromQuery] string? gm, CancellationToken ct)
        {
            RequireRole(NotAllowed, Roles.Buyer, Roles.CatHead, Roles.Planner, Roles.Admin);
            return Success(await _service.GetFormOptionsAsync(gm, ct));
        }

        /// <summary>"Validate Request": the decision against today's ledger (not saved) + seenSeq; may ask for the quantity confirmation.</summary>
        [HttpPost("validate")]
        public async Task<ActionResult<ApiResponse<Tool1ResultDto>>> Validate([FromBody] Tool1RequestInputDto input, CancellationToken ct)
        {
            RequireRole(NotAllowed, Roles.Buyer, Roles.CatHead, Roles.Planner, Roles.Admin);
            return Success(await _service.ValidateAsync(input, Me, ct));
        }

        /// <summary>Decide again with the confirmed quantity and save the request (409 re-check handled on the server).</summary>
        [HttpPost("submit")]
        public async Task<ActionResult<ApiResponse<Tool1ResultDto>>> Submit([FromBody] Tool1SubmitInputDto input, CancellationToken ct)
        {
            RequireRole(NotAllowed, Roles.Buyer, Roles.CatHead, Roles.Planner, Roles.Admin);
            return Success(await _service.SubmitAsync(input, Me, ct));
        }

        /// <summary>Today's Requests table (newest first). <paramref name="mine"/> = "Only mine".</summary>
        [HttpGet("today")]
        public async Task<ActionResult<ApiResponse<Tool1TodayDto>>> Today([FromQuery] bool mine, CancellationToken ct)
        {
            RequireRole(NotAllowed, Roles.Buyer, Roles.CatHead, Roles.Planner, Roles.Admin);
            return Success(await _service.GetTodayAsync(Me, mine, ct));
        }
    }
}
