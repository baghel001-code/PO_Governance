using DecisionEngine.Results.Mra;
using Application.Interfaces.V1.Sap;
using Domain.Entities.Response;
using Domain.Entities.Sap;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace POGovApi.Controllers.V1
{
    /// <summary>
    /// The verdict page behind the view_url of a SAP decision (portal /verdict/&lt;token&gt;): opened by anyone with the link,
    /// so no session is needed — only the static API token of the portal (StaticTokenMiddleware). Read-only, business fields only.
    /// </summary>
    [Route("api/v1/[Action]")]
    [AllowAnonymous]
    public sealed class SapVerdictController : ApiControllerBase
    {
        private readonly ISapVerdictViewService _view;
        private readonly IMraService _mra;

        public SapVerdictController(ISapVerdictViewService view, IMraService mra)
        {
            _view = view;
            _mra = mra;
        }

        /// <summary>The verdict of a link token (404 unknown, 410 expired).</summary>
        [HttpPost]
        public async Task<ActionResult<ApiDataResponse<SapVerdictView>>> SapVerdictView(SapVerdictViewReq req, CancellationToken ct) =>
            Success(await _view.GetAsync(req.Token ?? "", ct));

        /// <summary>
        /// The MRA page behind GET /api/sap/v1/verdict-url (portal /mra-verdict/&lt;token&gt;/&lt;mra&gt;): the MRA Queues panel
        /// content. Valid only with the verdict link token of the decision SAP stored this MRA number for (404), not expired (410).
        /// </summary>
        [HttpPost]
        public async Task<ActionResult<ApiDataResponse<MraVerdictView>>> MraVerdictView(MraVerdictViewReq req, CancellationToken ct) =>
            Success(await _mra.VerdictViewAsync(req.Token ?? "", req.MraNo ?? "", ct));
    }
}
