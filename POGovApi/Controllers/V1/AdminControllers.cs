using System.Text.Json.Nodes;
using Application.DTOs.Platform;
using Application.Interfaces.V1;
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
    /// <summary>User management (admin) — server.py api_GET/POST_users.</summary>
    [Route("api/v1/[Action]")]
    public sealed class UsersController : ApiControllerBase
    {
        private readonly IAccountsService _acc;
        private readonly IScrSource _scr;
        private readonly PoGovSettings _cfg;

        public UsersController(IAccountsService acc, IScrSource scr, IOptions<PoGovSettings> cfg)
        {
            _acc = acc;
            _scr = scr;
            _cfg = cfg.Value;
        }

        [HttpPost]
        public async Task<ActionResult<ApiDataResponse<UsersListResponse>>> UsersList(CancellationToken ct)
        {
            RequireAdmin();
            var scope = await _acc.ScopeIndexAsync(ct);
            var users = (await _acc.GetUsersAsync(ct)).Select(u =>
            {
                var d = _acc.Public(u);
                d.McCount = scope.KeysOf(u.Email).Count;
                return d;
            }).OrderBy(d => d.Active ? 0 : 1).ThenBy(d => (d.Name ?? "").ToLowerInvariant(), StringComparer.Ordinal).ToList();
            return Success(new UsersListResponse
            {
                Users = users,
                Roles = Roles.All.Select(r => new RoleDto(r, Roles.Labels[r])).ToList(),
                DefaultPassword = _acc.DefaultPassword,
                Login = new LoginInfo { Mode = _cfg.HrmsLogin ? LoginModes.Hrms : LoginModes.Local, HrmsPasswordUrl = _cfg.HrmsPasswordUrl }
            });
        }

        /// <summary>SCR products file analysis: e-mail columns, role guesses, mapping, people found.</summary>
        [HttpPost]
        public async Task<ActionResult<ApiDataResponse<ScrOverviewResponse>>> UsersScr(CancellationToken ct)
        {
            RequireAdmin();
            var (_, rows) = await _scr.GetScrRowsAsync(ct);
            var mapping = await _acc.GetMappingAsync(ct);
            var people = rows.Count > 0 ? _acc.ScrPeople(rows, mapping) : new Dictionary<string, ScrPerson>();
            var existing = (await _acc.GetUsersAsync(ct)).Select(u => u.Email).ToHashSet();
            return Success(new ScrOverviewResponse
            {
                HasScr = rows.Count > 0, ScrRows = rows.Count, Analysis = _acc.AnalyseScr(rows), Mapping = mapping,
                People = people.OrderBy(kv => kv.Key, StringComparer.Ordinal).Select(kv => new ScrPersonDto
                {
                    Email = kv.Key, Name = kv.Value.Name, Roles = kv.Value.Roles.OrderBy(r => r, StringComparer.Ordinal).ToList(),
                    Mcs = kv.Value.Keys.Count, Exists = existing.Contains(kv.Key)
                }).ToList()
            });
        }

        /// <summary>
        /// HRMS sign-in: {EmpCode, Roles, Email?} — the employee's details are fetched from HRMS here (the portal's copy is
        /// not trusted); Email is only used when HRMS has none. Local sign-in: {Email, Name, Roles}.
        /// </summary>
        [HttpPost]
        public async Task<ActionResult<ApiDataResponse<UserResponse>>> UserAdd([FromBody] AddUserRequest req, CancellationToken ct)
        {
            RequireAdmin();
            var u = await AddAsync(req, "manual", ct);
            return Success(new UserResponse { User = _acc.Public(u) });
        }

        private Task<AppUser> AddAsync(AddUserRequest req, string source, CancellationToken ct) =>
            _cfg.HrmsLogin || !string.IsNullOrWhiteSpace(req.EmpCode)
                ? _acc.AddEmployeeAsync(req.EmpCode ?? "", req.Roles ?? new List<string>(), Me.Email, req.Email, source, ct)
                : _acc.AddUserAsync(req.Email ?? "", (req.Name ?? "").Trim(), req.Roles ?? new List<string>(), Me.Email, source, ct);

        /// <summary>"Fetch from HRMS" (admin): the employee's HRMS details and whether they can be added.</summary>
        [HttpPost]
        public async Task<ActionResult<ApiDataResponse<HrmsLookupResponse>>> UserHrmsLookup([FromBody] EmpCodeRequest req, CancellationToken ct)
        {
            RequireAdmin();
            var (e, registered) = await _acc.LookupEmployeeAsync(req.EmpCode ?? "", ct);
            var email = PoGovSettingsExt.Norm(e.EmpMailId);
            var existing = email.Length > 0 ? await _acc.GetUserAsync(email, ct) : null;
            return Success(new HrmsLookupResponse
            {
                EmpCode = (e.EmpCode ?? "").Trim(), Name = e.EmpName?.Trim(), Email = email, Department = e.EmpDeptName, Designation = e.EmpDesig,
                Location = e.EmpLocation, LocationCode = e.EmpLocCode, Mobile = e.EmpContactNo, Manager = e.EmpRptMng,
                Grade = string.IsNullOrWhiteSpace(e.EmpGradeDesc) ? (e.EmpGrade > 0 ? e.EmpGrade.ToString() : null) : e.EmpGradeDesc,
                Active = e.EmpStatus, Resigned = e.IsResign, RegisteredAs = registered?.Email,
                LinksExisting = registered == null && existing is { IsEmployee: false },
                Problem = registered != null ? $"Already registered ({registered.Email})"
                        : e.IsResign ? "Resigned in HRMS — can't be added"
                        : !e.EmpStatus ? "Not active in HRMS — can't be added"
                        : existing is { IsEmployee: true } ? $"{email} is already registered with Employee ID {existing.EmpCode}"
                        : null
            });
        }

        /// <summary>{Email, EmpCode?}: links a login to an Employee ID (fetched from HRMS), or re-reads its HRMS details.</summary>
        [HttpPost]
        public async Task<ActionResult<ApiDataResponse<UserResponse>>> UserLinkEmployee([FromBody] LinkEmployeeRequest req, CancellationToken ct)
        {
            var e = await ExistingAsync(req.Email, ct);
            var u = await _acc.LinkEmployeeAsync(e, req.EmpCode, Me.Email, ct);
            return Success(new UserResponse { User = _acc.Public(u) });
        }

        [HttpPost]
        public async Task<ActionResult<ApiDataResponse<BulkUsersResponse>>> UsersBulk([FromBody] BulkUsersRequest req, CancellationToken ct)
        {
            RequireAdmin();
            var res = new BulkUsersResponse();
            foreach (var row in req.Users ?? new List<AddUserRequest>())
            {
                try
                {
                    var u = await AddAsync(row, "bulk", ct);
                    res.Added.Add(u.Email);
                }
                catch (AppValidationException e) { res.Errors.Add(e.Message); }
                catch (AppException e) when (e.Status == 503)
                {
                    res.Errors.Add(e.Message);
                    break;   // HRMS is down: the other rows would fail the same way
                }
            }
            return Success(res);
        }

        /// <summary>Creates logins for every buyer / category head in the SCR products file.</summary>
        [HttpPost]
        public Task<ActionResult<ApiDataResponse<ScrSyncResponse>>> UsersScrSync(CancellationToken ct) => SyncAsync(ct);

        /// <summary>Saves the admin's column mapping, then syncs.</summary>
        [HttpPost]
        public async Task<ActionResult<ApiDataResponse<ScrSyncResponse>>> UsersScrMapping([FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] ScrMappingRequest? req, CancellationToken ct)
        {
            RequireAdmin();
            await _acc.SaveMappingAsync(req?.Mapping ?? new ScrMapping(), Me.Email, ct);
            return await SyncAsync(ct);
        }

        private async Task<ActionResult<ApiDataResponse<ScrSyncResponse>>> SyncAsync(CancellationToken ct)
        {
            RequireAdmin();
            var (_, rows) = await _scr.GetScrRowsAsync(ct);
            if (rows.Count == 0) throw new AppValidationException("Upload the SCR products file first (Data Setup → Cat Head Mapping / SCR)");
            return Success(new ScrSyncResponse { Result = await _acc.SyncFromScrAsync(rows, Me.Email, ct) });
        }

        private async Task<string> ExistingAsync(string? email, CancellationToken ct)
        {
            RequireAdmin();
            var e = PoGovSettingsExt.Norm(email);
            if (e.Length == 0 || await _acc.GetUserAsync(e, ct) == null) throw new NotFoundException("No such user");
            return e;
        }

        /// <summary>{Email, Name?, Roles?, Active?, Unlock?} — only the properties sent are changed.</summary>
        [HttpPost]
        public async Task<ActionResult<ApiDataResponse<UserResponse>>> UserUpdate([FromBody] UserUpdateReq req, CancellationToken ct)
        {
            var e = await ExistingAsync(req.Email, ct);
            var u = await _acc.UpdateUserAsync(e, req.ToBody(), Me.Email, ct);
            return Success(new UserResponse { User = _acc.Public(u) });
        }

        [HttpPost]
        public async Task<ActionResult<ApiDataResponse<OkResponse>>> UserResetPassword([FromBody] UserEmailReq req, CancellationToken ct)
        {
            var e = await ExistingAsync(req.Email, ct);
            await _acc.ResetPasswordAsync(e, Me.Email, ct);
            return Success(OkResponse.Instance);
        }

        [HttpPost]
        public async Task<ActionResult<ApiDataResponse<OkResponse>>> UserDelete([FromBody] UserEmailReq req, CancellationToken ct)
        {
            var e = await ExistingAsync(req.Email, ct);
            if (e == Me.Email) throw new AppValidationException("You cannot delete your own login");
            if (e == PoGovSettingsExt.Norm(_cfg.AdminEmailOrDefault))
                throw new AppValidationException("The main admin (config.json admin_email) cannot be deleted");
            await _acc.DeleteUserAsync(e, Me.Email, ct);
            return Success(OkResponse.Instance);
        }
    }

    internal static class PoGovSettingsExt
    {
        public static string Norm(string? e) => (e ?? "").Trim().ToLowerInvariant();
    }

    /// <summary>Audit trail (admin) — server.py api_GET_audit.</summary>
    [Route("api/v1/[Action]")]
    public sealed class AuditController : ApiControllerBase
    {
        private readonly IAuditService _audit;
        public AuditController(IAuditService audit) => _audit = audit;

        [HttpPost]
        public async Task<ActionResult<ApiDataResponse<AuditQueryResponse>>> AuditQuery([FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] AuditQueryReq? req, CancellationToken ct)
        {
            RequireAdmin();
            return Success(await _audit.QueryAsync(req?.From, req?.To, req?.Q, ct));
        }
    }

    /// <summary>Backups (admin) — server.py api_GET/POST_backups.</summary>
    [Route("api/v1/[Action]")]
    public sealed class BackupsController : ApiControllerBase
    {
        private readonly IBackupService _backups;
        public BackupsController(IBackupService backups) => _backups = backups;

        [HttpPost]
        public ActionResult<ApiDataResponse<BackupListResponse>> BackupsList()
        {
            RequireAdmin();
            return Success(_backups.List());
        }

        [HttpPost]
        public async Task<ActionResult<ApiDataResponse<BackupResponse>>> BackupCreate(CancellationToken ct)
        {
            RequireAdmin();
            return Success(new BackupResponse { Name = await _backups.BackupNowAsync(Me.AuditName, ct) });
        }
    }

    /// <summary>User feedback — server.py api_GET/POST_feedback.</summary>
    [Route("api/v1/[Action]")]
    public sealed class FeedbackController : ApiControllerBase
    {
        private readonly IFeedbackService _fb;
        public FeedbackController(IFeedbackService fb) => _fb = fb;

        /// <summary>Admins see all items unless Mine is set; everyone else sees their own.</summary>
        [HttpPost]
        public async Task<ActionResult<ApiDataResponse<ItemsResponse>>> FeedbackList([FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] FeedbackListReq? req, CancellationToken ct) =>
            Success(await _fb.ListAsync(req?.Mine == true, Me, ct));

        [HttpPost]
        public async Task<ActionResult<ApiDataResponse<FeedbackCreatedResponse>>> FeedbackCreate([FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] FeedbackCreateReq? req, CancellationToken ct) =>
            Success(await _fb.CreateAsync(req?.ToBody() ?? new JsonObject(), Me, ct));

        /// <summary>Admin: {Id, Status: new | in progress | closed, Note}.</summary>
        [HttpPost]
        public async Task<ActionResult<ApiDataResponse<OkResponse>>> FeedbackSetStatus([FromBody] FeedbackSetStatusReq req, CancellationToken ct)
        {
            await _fb.SetStatusAsync(req.Id ?? "", req.ToBody(), Me, ct);
            return Success(OkResponse.Instance);
        }
    }

    /// <summary>AI proxy — server.py api_POST_ai (chat: Anthropic shape; insights: OpenAI shape).</summary>
    [Route("api/v1/[Action]")]
    public sealed class AiController : ApiControllerBase
    {
        private readonly IAiService _ai;
        public AiController(IAiService ai) => _ai = ai;

        private ActionResult<ApiDataResponse<JsonNode>> Upstream(AiResult r)
        {
            if (r.Status == 200) return Success(r.Data);
            var msg = (r.Data as JsonObject)?["error"] is JsonObject e && e["message"] is JsonValue m ? m.ToString()
                    : (r.Data as JsonObject)?["error"]?.ToString() ?? $"AI service answered HTTP {r.Status}";
            return StatusCode(r.Status, ApiDataResponse<JsonNode>.Fail(r.Status, msg, r.Data));
        }

        [HttpPost]
        public async Task<ActionResult<ApiDataResponse<JsonNode>>> AiChat([FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] AiChatReq? req, CancellationToken ct) =>
            Upstream(await _ai.ChatAsync(req?.ToBody() ?? new JsonObject(), Me, ct));

        [HttpPost]
        public async Task<ActionResult<ApiDataResponse<JsonNode>>> AiInsights([FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] AiPromptReq? req, CancellationToken ct) =>
            Upstream(await _ai.InsightsAsync(req?.ToBody() ?? new JsonObject(), Me, ct));
    }
}
