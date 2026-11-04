using Application.DTOs.Ingest;
using Application.Interfaces.V1.Sap;
using Domain.Entities.Response;
using Domain.Entities.Sap;
using Domain.Exceptions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace POGovApi.Controllers.V1
{
    /// <summary>
    /// Data Setup → MRA status (planners / admins): the morning MRA file replaces all MRA lines; during the day SAP updates
    /// them through POST /api/sap/v1/mra-status. Summary, list and the history of one MRA.
    /// </summary>
    [Route("api/v1/[Action]")]
    [IngestUploadLimit]
    public sealed class MraController : ApiControllerBase
    {
        private readonly IMraService _mra;

        public MraController(IMraService mra) => _mra = mra;

        /// <summary>The morning MRA file (multipart, one .xlsx / .xls / .csv). Refused files change nothing.</summary>
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

        /// <summary>Lines and quantity per status, last upload, SAP changes today.</summary>
        [HttpPost]
        public async Task<ActionResult<ApiDataResponse<MraSummaryResponse>>> MraSummary(CancellationToken ct)
        {
            RequireStaff();
            return Success(await _mra.SummaryAsync(ct));
        }

        /// <summary>{Status?, Search?, Page, PageSize}: the current MRA lines, newest change first.</summary>
        [HttpPost]
        public async Task<ActionResult<ApiDataResponse<MraListResponse>>> MraList([FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] MraListReq? req, CancellationToken ct)
        {
            RequireStaff();
            return Success(await _mra.ListAsync(req ?? new MraListReq(), ct));
        }

        /// <summary>{MraNo}: every change SAP sent for the MRA.</summary>
        [HttpPost]
        public async Task<ActionResult<ApiDataResponse<MraHistoryResponse>>> MraHistory([FromBody] MraNoReq req, CancellationToken ct)
        {
            RequireStaff();
            return Success(await _mra.HistoryAsync(req.MraNo, ct));
        }
    }
}
