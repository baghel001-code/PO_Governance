using Application.Interfaces.V1.Tool1;
using DecisionEngine.Results.Tool1;
using Domain.Constants;
using Domain.Entities.Response;
using Domain.Entities.Request;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace POGovApi.Controllers.V1
{
    /// <summary>
    /// Tool 1 — "✏️ New Request" of the Category view (index.html formGMInput / formSubmit / renderResult,
    /// shared.js saveRequest). For buyers, category heads, planners and admins (server.py api_POST_requests).
    /// </summary>
    [Route("api/v1/[Action]")]
    public sealed class Tool1Controller : ApiControllerBase
    {
        private const string NotAllowed = "Not allowed";
        private readonly ITool1Service _service;

        public Tool1Controller(ITool1Service service) => _service = service;

        /// <summary>The form for a GM: attribute strip, engine route, colour options and the 18 delivery months.</summary>
        [HttpPost]
        public async Task<ActionResult<ApiDataResponse<Tool1FormOptionsDto>>> Tool1Options([FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] GmReq? req, CancellationToken ct)
        {
            RequireRole(NotAllowed, Roles.Buyer, Roles.CatHead, Roles.Planner, Roles.Admin);
            return Success(await _service.GetFormOptionsAsync(req?.Gm, ct));
        }

        /// <summary>"Validate Request": the decision against today's ledger (not saved) + seenSeq; may ask for the quantity confirmation.</summary>
        [HttpPost]
        public async Task<ActionResult<ApiDataResponse<Tool1ResultDto>>> Tool1Validate([FromBody] Tool1RequestInputDto input, CancellationToken ct)
        {
            RequireRole(NotAllowed, Roles.Buyer, Roles.CatHead, Roles.Planner, Roles.Admin);
            return Success(await _service.ValidateAsync(input, Me, ct));
        }

        /// <summary>Decide again with the confirmed quantity and save the request (409 re-check handled on the server).</summary>
        [HttpPost]
        public async Task<ActionResult<ApiDataResponse<Tool1ResultDto>>> Tool1Submit([FromBody] Tool1SubmitInputDto input, CancellationToken ct)
        {
            RequireRole(NotAllowed, Roles.Buyer, Roles.CatHead, Roles.Planner, Roles.Admin);
            return Success(await _service.SubmitAsync(input, Me, ct));
        }

        /// <summary>Today's Requests table (newest first). Mine = "Only mine".</summary>
        [HttpPost]
        public async Task<ActionResult<ApiDataResponse<Tool1TodayDto>>> Tool1Today([FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] MineReq? req, CancellationToken ct)
        {
            RequireRole(NotAllowed, Roles.Buyer, Roles.CatHead, Roles.Planner, Roles.Admin);
            return Success(await _service.GetTodayAsync(Me, req?.Mine == true, ct));
        }
    }
}
