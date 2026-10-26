using System.Security.Claims;
using System.Text;
using Application.Interfaces.V1.Sap;
using Application.Services.V1.Sap;
using Infrastructure.Authentication;
using Infrastructure.Middleware;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace POGovApi.Controllers.Sap
{
    /// <summary>
    /// The SAP R/3 integration API — paths, fields and status codes fixed by the SAP API specification v1.4
    /// (legacy/python-app/docs/po-governance-sap-api.yaml): <c>GET /api/sap/v1/ping</c>, <c>GET /api/sap/v1/reason-codes</c>,
    /// <c>POST /api/sap/v1/events</c>, <c>POST /api/sap/v1/decisions</c>. snake_case JSON (not the portal's QPS
    /// conventions), HTTP Basic authentication of a technical user (scheme <c>SapBasic</c>), IP allow-list and error bodies
    /// by <see cref="SapInterfaceMiddleware"/>. Not part of the API master (SAP calls it directly through SM59).
    /// </summary>
    [Route("api/sap/v1")]
    [Authorize(AuthenticationSchemes = SapBasicAuthenticationHandler.SchemeName)]
    [Produces("application/json")]
    [ApiExplorerSettings(GroupName = "v1")]
    public sealed class SapController : ControllerBase
    {
        private const string Json = "application/json; charset=utf-8";

        private readonly ISapUtilityService _utility;
        private readonly ISapEventService _events;
        private readonly ISapDecisionService _decisions;
        private readonly ISapDocumentService _documents;

        public SapController(ISapUtilityService utility, ISapEventService events, ISapDecisionService decisions, ISapDocumentService documents)
        {
            _utility = utility;
            _events = events;
            _decisions = decisions;
            _documents = documents;
        }

        private SapCallContext Caller => new(
            User.FindFirstValue(ClaimTypes.Name) ?? "",
            User.FindFirstValue(SapBasicAuthenticationHandler.SourceSystemClaim) ?? "",
            HttpContext.Items[SapInterfaceMiddleware.ClientIpItem] as string ?? HttpContext.Connection.RemoteIpAddress?.ToString() ?? "",
            SapInterfaceMiddleware.TraceId(HttpContext));

        private async Task<string> BodyAsync(CancellationToken ct)
        {
            using var reader = new StreamReader(Request.Body, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            return await reader.ReadToEndAsync(ct);
        }

        private ContentResult Answer(SapCallResult r) => new() { StatusCode = r.Status, Content = r.Json, ContentType = Json };

        /// <summary>Connection test (SM59) and SAP's health check: 200 when reachable and the credentials are valid.</summary>
        [HttpGet("ping")]
        public async Task<IActionResult> Ping(CancellationToken ct) =>
            new ContentResult { StatusCode = 200, Content = SapJson.Serialize(await _utility.PingAsync(Caller, ct)), ContentType = Json };

        /// <summary>Active exception reason codes (approver dropdown).</summary>
        [HttpGet("reason-codes")]
        public async Task<IActionResult> ReasonCodes(CancellationToken ct) =>
            new ContentResult { StatusCode = 200, Content = SapJson.Serialize(await _utility.ReasonCodesAsync(ct)), ContentType = Json };

        /// <summary>A batch of 1–500 events from the ordered bgRFC queue; 200 = every event stored (accepted, duplicate or parked).</summary>
        [HttpPost("events")]
        public async Task<IActionResult> Events(CancellationToken ct) => Answer(await _events.ReceiveAsync(await BodyAsync(ct), Caller, ct));

        /// <summary>The governance verdict for a PO (synchronous, SAP waits at most 3 s; any non-200 = NOT_CHECKED).</summary>
        [HttpPost("decisions")]
        public async Task<IActionResult> Decisions(CancellationToken ct) => Answer(await _decisions.DecideAsync(await BodyAsync(ct), Caller, ct));

        /// <summary>The final SAP document (MRN) of a decided request_id: one per request_id, safe to resend, replace = true corrects it.</summary>
        [HttpPost("documents")]
        public async Task<IActionResult> Documents(CancellationToken ct) => Answer(await _documents.ReceiveAsync(await BodyAsync(ct), Caller, ct));
    }
}
