using System.Text.Json.Nodes;
using Application.DTOs.Platform;
using Application.Interfaces.V1;
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
    /// <summary>User management (admin) — server.py api_GET/POST_users.</summary>
    [Route("api/v1/users")]
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

        [HttpGet]
        public async Task<ActionResult<ApiResponse<UsersListResponse>>> List(CancellationToken ct)
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
                DefaultPassword = _acc.DefaultPassword
            });
        }

        /// <summary>SCR products file analysis: e-mail columns, role guesses, mapping, people found.</summary>
        [HttpGet("scr")]
        public async Task<ActionResult<ApiResponse<ScrOverviewResponse>>> Scr(CancellationToken ct)
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

        [HttpPost]
        public async Task<ActionResult<ApiResponse<UserResponse>>> Add([FromBody] AddUserRequest body, CancellationToken ct)
        {
            RequireAdmin();
            var u = await _acc.AddUserAsync(body.Email ?? "", (body.Name ?? "").Trim(), body.Roles ?? new List<string>(), Me.Email, "manual", ct);
            return Success(new UserResponse { User = _acc.Public(u) });
        }

        [HttpPost("bulk")]
        public async Task<ActionResult<ApiResponse<BulkUsersResponse>>> Bulk([FromBody] BulkUsersRequest body, CancellationToken ct)
        {
            RequireAdmin();
            var res = new BulkUsersResponse();
            foreach (var row in body.Users ?? new List<AddUserRequest>())
            {
                try
                {
                    await _acc.AddUserAsync(row.Email ?? "", (row.Name ?? "").Trim(), row.Roles ?? new List<string>(), Me.Email, "bulk", ct);
                    res.Added.Add(row.Email);
                }
                catch (AppValidationException e) { res.Errors.Add(e.Message); }
            }
            return Success(res);
        }

        /// <summary>Creates logins for every buyer / category head in the SCR products file.</summary>
        [HttpPost("scr")]
        public Task<ActionResult<ApiResponse<ScrSyncResponse>>> ScrSync(CancellationToken ct) => SyncAsync(ct);

        /// <summary>Saves the admin's column mapping, then syncs.</summary>
        [HttpPost("scr/mapping")]
        public async Task<ActionResult<ApiResponse<ScrSyncResponse>>> ScrMapping([FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] ScrMappingRequest? body, CancellationToken ct)
        {
            RequireAdmin();
            await _acc.SaveMappingAsync(body?.Mapping ?? new ScrMapping(), Me.Email, ct);
            return await SyncAsync(ct);
        }

        private async Task<ActionResult<ApiResponse<ScrSyncResponse>>> SyncAsync(CancellationToken ct)
        {
            RequireAdmin();
            var (_, rows) = await _scr.GetScrRowsAsync(ct);
            if (rows.Count == 0) throw new AppValidationException("Upload the SCR products file first (Data Setup → Cat Head Mapping / SCR)");
            return Success(new ScrSyncResponse { Result = await _acc.SyncFromScrAsync(rows, Me.Email, ct) });
        }

        private async Task<string> ExistingAsync(string email, CancellationToken ct)
        {
            RequireAdmin();
            var e = PoGovSettingsExt.Norm(email);
            if (await _acc.GetUserAsync(e, ct) == null) throw new NotFoundException("No such user");
            return e;
        }

        /// <summary>body: {name?, roles?, active?, unlock?}.</summary>
        [HttpPost("{email}/update")]
        public async Task<ActionResult<ApiResponse<UserResponse>>> Update(string email, [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] JsonObject? body, CancellationToken ct)
        {
            var e = await ExistingAsync(email, ct);
            var u = await _acc.UpdateUserAsync(e, body ?? new JsonObject(), Me.Email, ct);
            return Success(new UserResponse { User = _acc.Public(u) });
        }

        [HttpPost("{email}/reset-password")]
        public async Task<ActionResult<ApiResponse<OkResponse>>> ResetPassword(string email, CancellationToken ct)
        {
            var e = await ExistingAsync(email, ct);
            await _acc.ResetPasswordAsync(e, Me.Email, ct);
            return Success(OkResponse.Instance);
        }

        [HttpPost("{email}/delete")]
        public async Task<ActionResult<ApiResponse<OkResponse>>> Delete(string email, CancellationToken ct)
        {
            var e = await ExistingAsync(email, ct);
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
    [Route("api/v1/audit")]
    public sealed class AuditController : ApiControllerBase
    {
        private readonly IAuditService _audit;
        public AuditController(IAuditService audit) => _audit = audit;

        [HttpGet]
        public async Task<ActionResult<ApiResponse<AuditQueryResponse>>> Get([FromQuery] string? from, [FromQuery] string? to, [FromQuery] string? q, CancellationToken ct)
        {
            RequireAdmin();
            return Success(await _audit.QueryAsync(from, to, q, ct));
        }
    }

    /// <summary>Backups (admin) — server.py api_GET/POST_backups.</summary>
    [Route("api/v1/backups")]
    public sealed class BackupsController : ApiControllerBase
    {
        private readonly IBackupService _backups;
        public BackupsController(IBackupService backups) => _backups = backups;

        [HttpGet]
        public ActionResult<ApiResponse<BackupListResponse>> List()
        {
            RequireAdmin();
            return Success(_backups.List());
        }

        [HttpPost]
        public async Task<ActionResult<ApiResponse<BackupResponse>>> Create(CancellationToken ct)
        {
            RequireAdmin();
            return Success(new BackupResponse { Name = await _backups.BackupNowAsync(Me.AuditName, ct) });
        }
    }

    /// <summary>User feedback — server.py api_GET/POST_feedback.</summary>
    [Route("api/v1/feedback")]
    public sealed class FeedbackController : ApiControllerBase
    {
        private readonly IFeedbackService _fb;
        public FeedbackController(IFeedbackService fb) => _fb = fb;

        /// <summary>Admins see all items unless mine is set; everyone else sees their own.</summary>
        [HttpGet]
        public async Task<ActionResult<ApiResponse<ItemsResponse>>> List([FromQuery] string? mine, CancellationToken ct) =>
            Success(await _fb.ListAsync(!string.IsNullOrEmpty(mine), Me, ct));

        [HttpPost]
        public async Task<ActionResult<ApiResponse<FeedbackCreatedResponse>>> Create([FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] JsonObject? body, CancellationToken ct) =>
            Success(await _fb.CreateAsync(body ?? new JsonObject(), Me, ct));

        /// <summary>Admin: {status: new | in progress | closed, note}.</summary>
        [HttpPost("{id}")]
        public async Task<ActionResult<ApiResponse<OkResponse>>> SetStatus(string id, [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] JsonObject? body, CancellationToken ct)
        {
            await _fb.SetStatusAsync(id, body ?? new JsonObject(), Me, ct);
            return Success(OkResponse.Instance);
        }
    }

    /// <summary>AI proxy — server.py api_POST_ai (chat: Anthropic shape; insights: OpenAI shape).</summary>
    [Route("api/v1/ai")]
    public sealed class AiController : ApiControllerBase
    {
        private readonly IAiService _ai;
        public AiController(IAiService ai) => _ai = ai;

        private ActionResult<ApiResponse<JsonNode>> Upstream(AiResult r)
        {
            if (r.Status == 200) return Success(r.Data);
            var msg = (r.Data as JsonObject)?["error"] is JsonObject e && e["message"] is JsonValue m ? m.ToString()
                    : (r.Data as JsonObject)?["error"]?.ToString() ?? $"AI service answered HTTP {r.Status}";
            return StatusCode(r.Status, ApiResponse<JsonNode>.Fail(r.Status, msg, r.Data));
        }

        [HttpPost("chat")]
        public async Task<ActionResult<ApiResponse<JsonNode>>> Chat([FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] JsonObject? body, CancellationToken ct) =>
            Upstream(await _ai.ChatAsync(body ?? new JsonObject(), Me, ct));

        [HttpPost("insights")]
        public async Task<ActionResult<ApiResponse<JsonNode>>> Insights([FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] JsonObject? body, CancellationToken ct) =>
            Upstream(await _ai.InsightsAsync(body ?? new JsonObject(), Me, ct));
    }
}
