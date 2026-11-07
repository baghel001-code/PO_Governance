using Application.DTOs.Ingest;
using Application.Interfaces.V1.Sap;
using DecisionEngine.Results.Mra;
using Domain.Entities.Response;
using Domain.Exceptions;
using Microsoft.AspNetCore.Mvc;

namespace POGovApi.Controllers.V1
{
    /// <summary>
    /// SAP MRAs (v1.1.7): the morning MRA workbook (Data Setup, planners / admins), the state for the MRA Queues and SAP Feed
    /// pages (every signed-in user; buyers / category heads see their own MCs), the admin switches and the admin test sender.
    /// Live changes from SAP come in through POST /api/sap/v1/mra-events and /po-events.
    /// </summary>
    [Route("api/v1/[Action]")]
    [IngestUploadLimit]
    public sealed class MraController : ApiControllerBase
    {
        private readonly IMraService _mra;

        public MraController(IMraService mra) => _mra = mra;

        /// <summary>The morning MRA workbook (multipart, one .xlsx / .xlsb / .xls): item lines + header report. Replaces the MRA list.</summary>
        [HttpPost]
        public async Task<ActionResult<ApiDataResponse<MraUploadResponse>>> MraUpload(CancellationToken ct)
        {
            RequireStaff();
            if (!Request.HasFormContentType) throw new AppValidationException("Send the file as multipart/form-data");
            var form = await Request.ReadFormAsync(ct);
            var f = form.Files.FirstOrDefault() ?? throw new AppValidationException("No file uploaded");
            using var ms = new MemoryStream();
            await f.CopyToAsync(ms, ct);
            return Success(await _mra.UploadAsync(new IngestUpload(Path.GetFileName(f.FileName), ms.ToArray()), Me, ct));
        }

        /// <summary>Every MRA with its queue and whether it counts, the PO queue, the last 1,500 live messages, the switches.</summary>
        [HttpPost]
        public async Task<ActionResult<ApiDataResponse<MraStateResponse>>> MraState(CancellationToken ct) =>
            Success(await _mra.StateAsync(Me, ct));

        /// <summary>{MraNo}: the tool's decision of the MRA (linked from its saved SAP check) and the working saved with it.</summary>
        [HttpPost]
        public async Task<ActionResult<ApiDataResponse<MraDecisionDetail>>> MraDecision([FromBody] MraNoReq req, CancellationToken ct) =>
            Success(await _mra.DecisionAsync(req.MraNo, Me, ct));

        /// <summary>{ToolReqCapacity?, LivePo?} (admin).</summary>
        [HttpPost]
        public async Task<ActionResult<ApiDataResponse<MraSwitchesResponse>>> MraSwitches([FromBody] MraSwitchesReq req, CancellationToken ct)
        {
            RequireAdmin();
            return Success(await _mra.SetSwitchesAsync(req, Me, ct));
        }

        /// <summary>{Kind: "mra" | "po", Json}: the SAP Feed test sender (admin); stored and applied like a message from SAP.</summary>
        [HttpPost]
        public async Task<ActionResult<ApiDataResponse<MraFeedResponse>>> MraTestSend([FromBody] MraTestSendReq req, CancellationToken ct)
        {
            RequireAdmin();
            return Success(await _mra.TestSendAsync(req, Me, ct));
        }
    }
}
