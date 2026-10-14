using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Application.DTOs.Platform;
using Application.Interfaces.V1;
using Application.Services.V1.Platform;
using Domain.Constants;
using Domain.Entities.AppSettings;
using Domain.Entities.Response;
using Domain.Entities.Store;
using Domain.Exceptions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.Extensions.Options;

namespace POGovApi.Controllers.V1
{
    /// <summary>Data files — server.py api_GET/PUT/DELETE/POST_data.</summary>
    [Route("api/v1/data")]
    public sealed class DataController : ApiControllerBase
    {
        private readonly IDataService _data;
        private readonly PoGovSettings _cfg;

        public DataController(IDataService data, IOptions<PoGovSettings> cfg)
        {
            _data = data;
            _cfg = cfg.Value;
        }

        /// <summary>The stored {"rows":[…], "meta":{…}} payload of the current (or ?version=) version, as data.</summary>
        [HttpGet("{key}")]
        [ProducesResponseType(typeof(ApiResponse<JsonObject>), 200)]
        public async Task<IActionResult> Get(string key, [FromQuery] string? version, CancellationToken ct) =>
            new GzipJsonEnvelopeResult(await _data.GetPayloadGzipAsync(key, version, ct));

        /// <summary>
        /// Stores a new version (v1.1.4 wire format): body = {"rows":[…], "meta"?} JSON, gzip-compressed when
        /// header X-Body-Encoding: gzip. Headers: X-Rows (row count), X-Upload-Id (UPL-…), X-Label (URL-encoded).
        /// </summary>
        [HttpPut("{key}")]
        [DisableRequestSizeLimit]
        public async Task<ActionResult<ApiResponse<DataSaveResponse>>> Put(string key, CancellationToken ct)
        {
            RequireStaff();
            if (!PlatformCore.KeyRe.IsMatch(key)) throw new AppValidationException("Bad dataset key");
            var raw = await RequestBody.ReadAllAsync(Request, _cfg.MaxUploadMb, ct);
            var gz = DataService.PreparePutBody(raw, Request.Headers["X-Body-Encoding"] == "gzip");
            int.TryParse(Request.Headers["X-Rows"].ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var rows);
            var label = Uri.UnescapeDataString(Request.Headers["X-Label"].ToString());
            return Success(await _data.PutRawAsync(key, gz, rows, label, Request.Headers["X-Upload-Id"].ToString(), Me, ct));
        }

        /// <summary>Clears the current version (history keeps every version). Optional header X-Upload-Id.</summary>
        [HttpDelete("{key}")]
        public async Task<ActionResult<ApiResponse<DataVersionResponse>>> Clear(string key, CancellationToken ct) =>
            Success(await _data.ClearAsync(key, Request.Headers["X-Upload-Id"].ToString(), Me, ct));

        /// <summary>Makes an older version current again.</summary>
        [HttpPost("{key}/restore")]
        public async Task<ActionResult<ApiResponse<DataSaveResponse>>> Restore(string key, [FromQuery] string? version, CancellationToken ct) =>
            Success(await _data.RestoreVersionAsync(key, version ?? "", Me, ct));
    }

    /// <summary>Upload actions — server.py api_POST_uploads.</summary>
    [Route("api/v1/uploads")]
    public sealed class UploadsController : ApiControllerBase
    {
        private readonly IDataService _data;
        public UploadsController(IDataService data) => _data = data;

        /// <summary>Starts one upload action (a zip, a set of files) and returns its Upload ID.</summary>
        [HttpPost]
        public async Task<ActionResult<ApiResponse<UploadStartResponse>>> Start([FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] UploadStartRequest? body, CancellationToken ct)
        {
            RequireStaff();
            var files = (body?.SourceFiles ?? new List<string?>()).Where(x => !string.IsNullOrEmpty(x)).Select(x => PlatformCore.Trunc(x!, 200)).ToList();
            var source = PlatformCore.Trunc(string.IsNullOrEmpty(body?.Source) ? "files" : body!.Source!, 20);
            return Success(new UploadStartResponse { UploadId = await _data.NewUploadBatchAsync(source, files, Me, "", ct) });
        }

        /// <summary>Makes every file of that upload current again.</summary>
        [HttpPost("{uploadId}/restore")]
        public async Task<ActionResult<ApiResponse<UploadRestoreResponse>>> Restore(string uploadId, CancellationToken ct) =>
            Success(await _data.RestoreUploadAsync(uploadId, Me, ct));
    }

    /// <summary>History — server.py api_GET_history.</summary>
    [Route("api/v1/history")]
    public sealed class HistoryController : ApiControllerBase
    {
        private readonly IHistoryService _history;
        public HistoryController(IHistoryService history) => _history = history;

        [HttpGet("uploads")]
        public async Task<ActionResult<ApiResponse<UploadHistoryResponse>>> Uploads([FromQuery] string? from, [FromQuery] string? to, CancellationToken ct)
        {
            PlatformCore.CheckRange(from, to);
            RequireRole("Planner or admin only", Roles.Planner, Roles.Admin);
            return Success(await _history.UploadsAsync(from, to, ct));
        }

        [HttpGet("requests")]
        public async Task<ActionResult<ApiResponse<ItemsResponse>>> Requests([FromQuery] string? from, [FromQuery] string? to, CancellationToken ct) =>
            Success(await _history.RequestsAsync(from, to, Me, ct));
    }

    /// <summary>Saved Tool 2 runs — server.py api_GET/POST/DELETE_runs.</summary>
    [Route("api/v1/runs")]
    public sealed class RunsController : ApiControllerBase
    {
        private readonly IRunService _runs;
        private readonly PoGovSettings _cfg;

        public RunsController(IRunService runs, IOptions<PoGovSettings> cfg)
        {
            _runs = runs;
            _cfg = cfg.Value;
        }

        [HttpGet]
        public async Task<ActionResult<ApiResponse<ItemsResponse>>> List([FromQuery] string? from, [FromQuery] string? to, CancellationToken ct)
        {
            RequireRole("Planner or admin only", Roles.Planner, Roles.Admin);
            return Success(await _runs.ListAsync(from, to, ct));
        }

        /// <summary>The saved run payload (decompressed JSON) as data.</summary>
        [HttpGet("{id}")]
        [ProducesResponseType(typeof(ApiResponse<JsonObject>), 200)]
        public async Task<IActionResult> Get(string id, CancellationToken ct)
        {
            RequireRole("Planner or admin only", Roles.Planner, Roles.Admin);
            return new GzipJsonEnvelopeResult(await _runs.GetGzipAsync(id, ct));
        }

        /// <summary>Body = gzip JSON payload; header X-Run-Meta = URL-encoded JSON object (lines, …).</summary>
        [HttpPost]
        [DisableRequestSizeLimit]
        public async Task<ActionResult<ApiResponse<RunSaveResponse>>> Save(CancellationToken ct)
        {
            RequireStaff();
            var raw = await RequestBody.ReadAllAsync(Request, _cfg.MaxUploadMb, ct);
            var metaText = Uri.UnescapeDataString(Request.Headers["X-Run-Meta"].ToString());
            if (string.IsNullOrEmpty(metaText)) metaText = "{}";
            JsonObject meta;
            try { meta = JsonNode.Parse(metaText) as JsonObject ?? throw new AppValidationException("X-Run-Meta must be a JSON object"); }
            catch (JsonException e) { throw new AppValidationException(e.Message); }
            return Success(await _runs.SaveAsync(raw, meta, Me, ct));
        }

        /// <summary>Marks the run deleted (recoverable by an admin).</summary>
        [HttpDelete("{id}")]
        public async Task<ActionResult<ApiResponse<OkResponse>>> Delete(string id, CancellationToken ct)
        {
            await _runs.DeleteAsync(id, Me, ct);
            return Success(OkResponse.Instance);
        }
    }

    /// <summary>Planner parameters — server.py api_GET/PUT_params.</summary>
    [Route("api/v1/params")]
    public sealed class ParamsController : ApiControllerBase
    {
        private readonly IParamsService _params;
        public ParamsController(IParamsService p) => _params = p;

        [HttpGet]
        public async Task<ActionResult<ApiResponse<JsonNode>>> Get(CancellationToken ct) => Success(await _params.GetAsync(ct));

        [HttpPut]
        public async Task<ActionResult<ApiResponse<ParamsSaveResponse>>> Put([FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] JsonNode? body, CancellationToken ct) =>
            Success(await _params.PutAsync(body ?? new JsonObject(), Me, ct));
    }

    /// <summary>Tool 1 request log — server.py api_GET/POST/DELETE_requests.</summary>
    [Route("api/v1/requests")]
    public sealed class RequestsController : ApiControllerBase
    {
        private readonly IRequestService _requests;
        public RequestsController(IRequestService requests) => _requests = requests;

        /// <summary>A day's live requests (default today), filtered to what the user may see.</summary>
        [HttpGet]
        public async Task<ActionResult<ApiResponse<RequestsDayResponse>>> Get([FromQuery] string? date, CancellationToken ct) =>
            Success(await _requests.GetDayForUserAsync(date, Me, ct));

        /// <summary>
        /// body: {request:{…}, seenSeq, force}. 409 (data {conflict, byOthers, seq}) when someone else saved
        /// a request on the same colour since seenSeq.
        /// </summary>
        [HttpPost]
        public async Task<ActionResult<ApiResponse<SaveRequestResponse>>> Save([FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] JsonObject? body, CancellationToken ct)
        {
            if (!Me.Has(Roles.Buyer, Roles.CatHead, Roles.Planner, Roles.Admin)) throw new ForbiddenException("Not allowed");
            body ??= new JsonObject();
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
        [HttpDelete]
        public async Task<ActionResult<ApiResponse<SeqResponse>>> Clear([FromQuery] string? date, CancellationToken ct) =>
            Success(await _requests.ClearDayAsync(date, Me, ct));
    }

    /// <summary>Watch List approvals — server.py api_GET/POST_approvals.</summary>
    [Route("api/v1/approvals")]
    public sealed class ApprovalsController : ApiControllerBase
    {
        private readonly IApprovalService _approvals;
        public ApprovalsController(IApprovalService approvals) => _approvals = approvals;

        /// <summary>Pending items waiting for me and decided ones (last ?days= request days, default 90).</summary>
        [HttpGet]
        public async Task<ActionResult<ApiResponse<ApprovalsResponse>>> Get([FromQuery] string? days, CancellationToken ct)
        {
            var d = 90;
            if (!string.IsNullOrEmpty(days) && !int.TryParse(days.Trim(), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out d))
                throw new AppValidationException($"invalid literal for int() with base 10: '{days}'");
            return Success(await _approvals.GetAsync(d, Me, ct));
        }

        /// <summary>body: {decision: approved | rejected, qty, comment}.</summary>
        [HttpPost("{reqId}")]
        public async Task<ActionResult<ApiResponse<SeqResponse>>> Decide(string reqId, [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] JsonObject? body, CancellationToken ct) =>
            Success(await _approvals.DecideAsync(reqId, body ?? new JsonObject(), Me, ct));
    }
}
