using Application.Interfaces.V1.Sap;
using Domain.Entities.Response;
using Domain.Entities.Sap;
using Microsoft.AspNetCore.Mvc;

namespace POGovApi.Controllers.V1
{
    /// <summary>
    /// The portal's SAP interface monitoring screen (/admin/sap, admin only) — normal QPS actions (POST api/v1/[Action],
    /// task names PO_SAP_* in AllApiNames / API_MASTER). The SAP-facing endpoints themselves are in Controllers/Sap.
    /// </summary>
    [Route("api/v1/[Action]")]
    public sealed class SapAdminController : ApiControllerBase
    {
        private const string XlsxContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

        private readonly ISapAdminService _sap;

        public SapAdminController(ISapAdminService sap)
        {
            _sap = sap;
        }

        /// <summary>Settings in force, event-feed status (lag, parked, not applied) and decision counts / latency.</summary>
        [HttpPost]
        public async Task<ActionResult<ApiDataResponse<SapOverviewResponse>>> SapOverview(CancellationToken ct)
        {
            RequireAdmin();
            return Success(await _sap.OverviewAsync(ct));
        }

        [HttpPost]
        public async Task<ActionResult<ApiDataResponse<SapDecisionListResponse>>> SapDecisionList(SapDecisionListReq req, CancellationToken ct)
        {
            RequireAdmin();
            return Success(await _sap.ListDecisionsAsync(req, ct));
        }

        /// <summary>One decision with its items and the request / response JSON.</summary>
        [HttpPost]
        public async Task<ActionResult<ApiDataResponse<SapDecisionDetailResponse>>> SapDecisionGet(SapDecisionGetReq req, CancellationToken ct)
        {
            RequireAdmin();
            return Success(await _sap.GetDecisionAsync(req.Id, ct));
        }

        [HttpPost]
        public async Task<ActionResult<ApiDataResponse<SapEventListResponse>>> SapEventList(SapEventListReq req, CancellationToken ct)
        {
            RequireAdmin();
            return Success(await _sap.ListEventsAsync(req, ct));
        }

        /// <summary>One event with its payload.</summary>
        [HttpPost]
        public async Task<ActionResult<ApiDataResponse<SapEventRow>>> SapEventGet(SapEventGetReq req, CancellationToken ct)
        {
            RequireAdmin();
            return Success(await _sap.GetEventAsync(req.Id, ct));
        }

        [HttpPost]
        public async Task<ActionResult<ApiDataResponse<SapTechUsersResponse>>> SapTechUserList(CancellationToken ct)
        {
            RequireAdmin();
            return Success(await _sap.ListTechUsersAsync(ct));
        }

        /// <summary>A new technical user (empty password = a generated one, returned once).</summary>
        [HttpPost]
        public async Task<ActionResult<ApiDataResponse<SapTechUserResponse>>> SapTechUserAdd(SapTechUserAddReq req, CancellationToken ct)
        {
            RequireAdmin();
            return Success(await _sap.AddTechUserAsync(req, Me.Email, ct));
        }

        /// <summary>enable / disable / unlock / reset-password.</summary>
        [HttpPost]
        public async Task<ActionResult<ApiDataResponse<SapTechUserResponse>>> SapTechUserUpdate(SapTechUserUpdateReq req, CancellationToken ct)
        {
            RequireAdmin();
            return Success(await _sap.UpdateTechUserAsync(req, Me.Email, ct));
        }

        [HttpPost]
        public async Task<ActionResult<ApiDataResponse<SapReasonCodesListResponse>>> SapReasonCodeList(CancellationToken ct)
        {
            RequireAdmin();
            return Success(await _sap.ListReasonCodesAsync(ct));
        }

        /// <summary>Add or change an exception reason code (served to SAP by GET /reason-codes).</summary>
        [HttpPost]
        public async Task<ActionResult<ApiDataResponse<SapReasonCodeRow>>> SapReasonCodeSave(SapReasonCodeSaveReq req, CancellationToken ct)
        {
            RequireAdmin();
            return Success(await _sap.SaveReasonCodeAsync(req, Me.Email, ct));
        }

        /// <summary>Runs a /decisions request body through the real decision path (logged as a test).</summary>
        [HttpPost]
        public async Task<ActionResult<ApiDataResponse<SapTestDecisionResponse>>> SapTestDecision(SapTestDecisionReq req, CancellationToken ct)
        {
            RequireAdmin();
            return Success(await _sap.TestDecisionAsync(req, Me.Email, Me.ClientIp ?? "", ct));
        }

        /// <summary>PO document report: answered decisions with their final SAP document (MRN).</summary>
        [HttpPost]
        public async Task<ActionResult<ApiDataResponse<SapDocumentReportResponse>>> SapDocumentReport(SapDocumentReportReq req, CancellationToken ct)
        {
            RequireAdmin();
            return Success(await _sap.DocumentReportAsync(req, ct));
        }

        /// <summary>The PO document report as .xlsx (all matching rows).</summary>
        [HttpPost]
        [Produces(XlsxContentType, "application/json")]
        public async Task<IActionResult> SapDocumentReportExport(SapDocumentReportReq req, CancellationToken ct)
        {
            RequireAdmin();
            var bytes = await _sap.DocumentReportExcelAsync(req, ct);
            var name = $"PO_Documents_{(string.IsNullOrWhiteSpace(req.From) ? "all" : req.From)}_to_{(string.IsNullOrWhiteSpace(req.To) ? "today" : req.To)}.xlsx";
            return File(bytes, XlsxContentType, name);
        }

        /// <summary>A sample /decisions request built from the uploaded data.</summary>
        [HttpPost]
        public async Task<ActionResult<ApiDataResponse<SapTestDecisionResponse>>> SapSampleRequest(CancellationToken ct)
        {
            RequireAdmin();
            return Success(new SapTestDecisionResponse { SampleRequestJson = await _sap.SampleRequestAsync(ct) });
        }
    }
}
