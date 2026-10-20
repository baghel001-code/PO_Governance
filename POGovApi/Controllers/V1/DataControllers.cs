using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Application.DTOs.Platform;
using Application.Interfaces.V1;
using Application.Services.V1.Platform;
using Domain.Constants;
using Domain.Entities.AppSettings;
using Domain.Entities.Request;
using Domain.Entities.Response;
using Domain.Entities.Store;
using Domain.Exceptions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.Extensions.Options;

namespace POGovApi.Controllers.V1
{
    /// <summary>Data files — server.py api_GET/PUT/DELETE/POST_data.</summary>
    [Route("api/v1/[Action]")]
    public sealed class DataController : ApiControllerBase
    {
        private readonly IDataService _data;
        private readonly PoGovSettings _cfg;

        public DataController(IDataService data, IOptions<PoGovSettings> cfg)
        {
            _data = data;
            _cfg = cfg.Value;
        }

        /// <summary>The stored {"rows":[…], "meta":{…}} payload of the current (or Version) version, as Data.</summary>
        [HttpPost]
        [ProducesResponseType(typeof(ApiDataResponse<JsonObject>), 200)]
        public async Task<IActionResult> DataGet([FromBody] DataKeyReq req, CancellationToken ct) =>
            new GzipJsonEnvelopeResult(await _data.GetPayloadGzipAsync(req.Key ?? "", req.Version, ct));

        /// <summary>
        /// Stores a new version (v1.1.4 wire format) as multipart/form-data: the file part = {"rows":[…], "meta"?} JSON,
        /// plain or gzip-compressed; fields Key, Rows (row count), UploadId (UPL-…), Label.
        /// </summary>
        [HttpPost]
        [DisableRequestSizeLimit]
        [RequestFormLimits(MultipartBodyLengthLimit = long.MaxValue, ValueLengthLimit = int.MaxValue)]
        public async Task<ActionResult<ApiDataResponse<DataSaveResponse>>> DataPut(CancellationToken ct)
        {
            RequireStaff();
            var (raw, form) = await RequestBody.ReadMultipartAsync(Request, _cfg.MaxUploadMb, ct);
            var key = form["Key"].ToString();
            if (!PlatformCore.KeyRe.IsMatch(key)) throw new AppValidationException("Bad dataset key");
            var gz = DataService.PreparePutBody(raw, RequestBody.IsGzip(raw));
            int.TryParse(form["Rows"].ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var rows);
            return Success(await _data.PutRawAsync(key, gz, rows, form["Label"].ToString(), form["UploadId"].ToString(), Me, ct));
        }

        /// <summary>Clears the current version (history keeps every version). Optional UploadId.</summary>
        [HttpPost]
        public async Task<ActionResult<ApiDataResponse<DataVersionResponse>>> DataClear([FromBody] DataKeyReq req, CancellationToken ct) =>
            Success(await _data.ClearAsync(req.Key ?? "", req.UploadId ?? "", Me, ct));

        /// <summary>Makes an older version current again.</summary>
        [HttpPost]
        public async Task<ActionResult<ApiDataResponse<DataSaveResponse>>> DataRestore([FromBody] DataKeyReq req, CancellationToken ct) =>
            Success(await _data.RestoreVersionAsync(req.Key ?? "", req.Version ?? "", Me, ct));
    }

    /// <summary>Upload actions — server.py api_POST_uploads.</summary>
    [Route("api/v1/[Action]")]
    public sealed class UploadsController : ApiControllerBase
    {
        private readonly IDataService _data;
        public UploadsController(IDataService data) => _data = data;

        /// <summary>Starts one upload action (a zip, a set of files) and returns its Upload ID.</summary>
        [HttpPost]
        public async Task<ActionResult<ApiDataResponse<UploadStartResponse>>> UploadStart([FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] UploadStartRequest? req, CancellationToken ct)
        {
            RequireStaff();
            var files = (req?.SourceFiles ?? new List<string?>()).Where(x => !string.IsNullOrEmpty(x)).Select(x => PlatformCore.Trunc(x!, 200)).ToList();
            var source = PlatformCore.Trunc(string.IsNullOrEmpty(req?.Source) ? "files" : req!.Source!, 20);
            return Success(new UploadStartResponse { UploadId = await _data.NewUploadBatchAsync(source, files, Me, "", ct) });
        }

        /// <summary>Makes every file of that upload current again.</summary>
        [HttpPost]
        public async Task<ActionResult<ApiDataResponse<UploadRestoreResponse>>> UploadRestore([FromBody] UploadIdReq req, CancellationToken ct) =>
            Success(await _data.RestoreUploadAsync(req.UploadId ?? "", Me, ct));
    }

    /// <summary>History — server.py api_GET_history.</summary>
    [Route("api/v1/[Action]")]
    public sealed class HistoryController : ApiControllerBase
    {
        private readonly IHistoryService _history;
        public HistoryController(IHistoryService history) => _history = history;

        [HttpPost]
        public async Task<ActionResult<ApiDataResponse<UploadHistoryResponse>>> HistoryUploads([FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] DateRangeReq? req, CancellationToken ct)
        {
            PlatformCore.CheckRange(req?.From, req?.To);
            RequireRole("Planner or admin only", Roles.Planner, Roles.Admin);
            return Success(await _history.UploadsAsync(req?.From, req?.To, ct));
        }

        [HttpPost]
        public async Task<ActionResult<ApiDataResponse<ItemsResponse>>> HistoryRequests([FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] DateRangeReq? req, CancellationToken ct) =>
            Success(await _history.RequestsAsync(req?.From, req?.To, Me, ct));
    }

    /// <summary>Saved Tool 2 runs — server.py api_GET/POST/DELETE_runs.</summary>
    [Route("api/v1/[Action]")]
    public sealed class RunsController : ApiControllerBase
    {
        private readonly IRunService _runs;
        private readonly PoGovSettings _cfg;

        public RunsController(IRunService runs, IOptions<PoGovSettings> cfg)
        {
            _runs = runs;
            _cfg = cfg.Value;
        }

        [HttpPost]
        public async Task<ActionResult<ApiDataResponse<ItemsResponse>>> RunsList([FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] DateRangeReq? req, CancellationToken ct)
        {
            RequireRole("Planner or admin only", Roles.Planner, Roles.Admin);
            return Success(await _runs.ListAsync(req?.From, req?.To, ct));
        }

        /// <summary>The saved run payload (decompressed JSON) as Data.</summary>
        [HttpPost]
        [ProducesResponseType(typeof(ApiDataResponse<JsonObject>), 200)]
        public async Task<IActionResult> RunGet([FromBody] RunIdReq req, CancellationToken ct)
        {
            RequireRole("Planner or admin only", Roles.Planner, Roles.Admin);
            return new GzipJsonEnvelopeResult(await _runs.GetGzipAsync(req.RunId ?? "", ct));
        }

        /// <summary>multipart/form-data: file part = gzip JSON payload; field Meta = JSON object (lines, …).</summary>
        [HttpPost]
        [DisableRequestSizeLimit]
        [RequestFormLimits(MultipartBodyLengthLimit = long.MaxValue, ValueLengthLimit = int.MaxValue)]
        public async Task<ActionResult<ApiDataResponse<RunSaveResponse>>> RunSave(CancellationToken ct)
        {
            RequireStaff();
            var (raw, form) = await RequestBody.ReadMultipartAsync(Request, _cfg.MaxUploadMb, ct);
            var metaText = form["Meta"].ToString();
            if (string.IsNullOrEmpty(metaText)) metaText = "{}";
            JsonObject meta;
            try { meta = JsonNode.Parse(metaText) as JsonObject ?? throw new AppValidationException("Meta must be a JSON object"); }
            catch (JsonException e) { throw new AppValidationException(e.Message); }
            return Success(await _runs.SaveAsync(raw, meta, Me, ct));
        }

        /// <summary>Marks the run deleted (recoverable by an admin).</summary>
        [HttpPost]
        public async Task<ActionResult<ApiDataResponse<OkResponse>>> RunDelete([FromBody] RunIdReq req, CancellationToken ct)
        {
            await _runs.DeleteAsync(req.RunId ?? "", Me, ct);
            return Success(OkResponse.Instance);
        }
    }

    /// <summary>Planner parameters — server.py api_GET/PUT_params.</summary>
    [Route("api/v1/[Action]")]
    public sealed class ParamsController : ApiControllerBase
    {
        private readonly IParamsService _params;
        public ParamsController(IParamsService p) => _params = p;

        [HttpPost]
        public async Task<ActionResult<ApiDataResponse<JsonNode>>> ParamsGet(CancellationToken ct) => Success(await _params.GetAsync(ct));

        /// <summary>Replaces the whole parameters document with Params.</summary>
        [HttpPost]
        public async Task<ActionResult<ApiDataResponse<ParamsSaveResponse>>> ParamsSave([FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] ParamsSaveReq? req, CancellationToken ct) =>
            Success(await _params.PutAsync(req?.Params ?? new JsonObject(), Me, ct));
    }

    /// <summary>Tool 1 request log — server.py api_GET/POST/DELETE_requests.</summary>
    [Route("api/v1/[Action]")]
    public sealed class RequestsController : ApiControllerBase
    {
        private readonly IRequestService _requests;
        public RequestsController(IRequestService requests) => _requests = requests;

        /// <summary>A day's live requests (default today), filtered to what the user may see.</summary>
        [HttpPost]
        public async Task<ActionResult<ApiDataResponse<RequestsDayResponse>>> RequestsGet([FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] RequestsDayReq? req, CancellationToken ct) =>
            Success(await _requests.GetDayForUserAsync(req?.Date, Me, ct));

        /// <summary>
        /// {Request:{…}, SeenSeq, Force}. 409 (Data {conflict, byOthers, seq}) when someone else saved
        /// a request on the same colour since SeenSeq.
        /// </summary>
        [HttpPost]
        public async Task<ActionResult<ApiDataResponse<SaveRequestResponse>>> RequestSave([FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] RequestSaveReq? req, CancellationToken ct)
        {
            if (!Me.Has(Roles.Buyer, Roles.CatHead, Roles.Planner, Roles.Admin)) throw new ForbiddenException("Not allowed");
            var body = req?.ToBody() ?? new JsonObject();
            if (body["request"] is not JsonObject r) throw new AppValidationException("Missing request");
            var seen = body.GetTruthy("seenSeq") ? (int)PlatformCore.PyInt(body["seenSeq"]) : 0;
            r = r.DeepClone().AsObject();
            var saved = await _requests.SaveAsync(r, seen, body.GetTruthy("force"), Me, ct);
            return Success(new SaveRequestResponse
            {
                Id = saved.GetLong("id"), ReqId = saved.GetStr("reqId") ?? "", Seq = (int)saved.GetLong("seq"),
                Approval = saved.Get("approval")?.DeepClone()
            });
        }

        /// <summary>Clears the day's live ledger (requests stay in history, marked cleared).</summary>
        [HttpPost]
        public async Task<ActionResult<ApiDataResponse<SeqResponse>>> RequestsClear([FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] RequestsDayReq? req, CancellationToken ct) =>
            Success(await _requests.ClearDayAsync(req?.Date, Me, ct));
    }

    /// <summary>Watch List approvals — server.py api_GET/POST_approvals.</summary>
    [Route("api/v1/[Action]")]
    public sealed class ApprovalsController : ApiControllerBase
    {
        private readonly IApprovalService _approvals;
        public ApprovalsController(IApprovalService approvals) => _approvals = approvals;

        /// <summary>Pending items waiting for me and decided ones (last Days request days, default 90).</summary>
        [HttpPost]
        public async Task<ActionResult<ApiDataResponse<ApprovalsResponse>>> ApprovalsGet([FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] ApprovalsGetReq? req, CancellationToken ct)
        {
            var d = 90;
            var days = req?.Days is JsonValue v ? (v.GetValueKind() == JsonValueKind.String ? v.GetValue<string>() : v.ToJsonString()) : null;
            if (!string.IsNullOrEmpty(days) && !int.TryParse(days.Trim(), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out d))
                throw new AppValidationException($"invalid literal for int() with base 10: '{days}'");
            return Success(await _approvals.GetAsync(d, Me, ct));
        }

        /// <summary>{ReqId, Decision: approved | rejected, Qty, Comment}.</summary>
        [HttpPost]
        public async Task<ActionResult<ApiDataResponse<SeqResponse>>> ApprovalDecide([FromBody] ApprovalDecideReq req, CancellationToken ct) =>
            Success(await _approvals.DecideAsync(req.ReqId ?? "", req.ToBody(), Me, ct));
    }
}
