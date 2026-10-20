using Application.DTOs.Platform;
using Application.Interfaces.V1;
using Domain.Entities.AppSettings;
using Domain.Entities.Request;
using Domain.Entities.Response;
using Domain.Exceptions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace POGovApi.Controllers.V1
{
    /// <summary>Sign-in, sign-out, password change, me (server.py /api/auth/*). Allowed while the password must be changed.</summary>
    [Route("api/v1/[Action]")]
    public sealed class AuthController : ApiControllerBase
    {
        private readonly IAccountsService _acc;
        private readonly IStatusService _status;
        private readonly PoGovSettings _cfg;

        public AuthController(IAccountsService acc, IStatusService status, IOptions<PoGovSettings> cfg)
        {
            _acc = acc;
            _status = status;
            _cfg = cfg.Value;
        }

        /// <summary>Returns a session token: send it as "X-Session-Token: &lt;token&gt;" on every later call.</summary>
        [AllowAnonymous]
        [HttpPost]
        public async Task<ActionResult<ApiDataResponse<LoginResponse>>> AuthLogin([FromBody] LoginRequest req, CancellationToken ct)
        {
            await Task.Delay(300, ct);   // slows password guessing
            var (token, err) = await _acc.LoginAsync(req.Email ?? "", req.Password ?? "", ClientIp(HttpContext), ct);
            if (err != null) throw new AppException(401, err);
            var u = (await _acc.GetUserAsync(req.Email ?? "", ct))!;
            return Success(new LoginResponse
            {
                Token = token!, MustChange = u.MustChange, Name = u.Name, User = _acc.Public(u),
                ExpiresIn = (int)_cfg.SessionIdleSeconds
            });
        }

        [HttpPost]
        public async Task<ActionResult<ApiDataResponse<OkResponse>>> AuthLogout(CancellationToken ct)
        {
            if (!string.IsNullOrEmpty(Me.SessionToken)) await _acc.LogoutAsync(Me.SessionToken, ct);
            return Success(OkResponse.Instance);
        }

        /// <summary>Changes the password and signs out the user's other sessions.</summary>
        [HttpPost]
        public async Task<ActionResult<ApiDataResponse<OkResponse>>> AuthChangePassword([FromBody] ChangePasswordRequest req, CancellationToken ct)
        {
            var err = await _acc.ChangePasswordAsync(Me.Email, req.Old ?? "", req.New ?? "", ct);
            if (err != null) throw new AppValidationException(err);
            await _acc.EndSessionsAsync(Me.Email, Me.SessionToken, ct);   // sign out other devices
            return Success(OkResponse.Instance);
        }

        /// <summary>The signed-in user (the portal's session revalidation) and the password rules.</summary>
        [HttpPost]
        public async Task<ActionResult<ApiDataResponse<MeResponse>>> AuthMe(CancellationToken ct) =>
            Success(new MeResponse { User = await _status.MePublicAsync(Me, ct), PasswordMinLength = _cfg.PasswordMinLengthOrDefault });
    }

    /// <summary>Health check: no static token, no session (load balancers, monitoring).</summary>
    [Route("api/v1/[Action]")]
    public sealed class HealthController : ApiControllerBase
    {
        private readonly IStatusService _status;
        public HealthController(IStatusService status) => _status = status;

        [AllowAnonymous]
        [HttpGet]
        public ActionResult<ApiDataResponse<HealthResponse>> Health() => Success(_status.Health());
    }

    /// <summary>server.py GET /api/status: versions, datasets, params, today's request seq, me, pending approvals, AI flags.</summary>
    [Route("api/v1/[Action]")]
    public sealed class StatusController : ApiControllerBase
    {
        private readonly IStatusService _status;
        public StatusController(IStatusService status) => _status = status;

        [HttpPost]
        public async Task<ActionResult<ApiDataResponse<StatusResponse>>> GetStatus(CancellationToken ct) => Success(await _status.GetStatusAsync(Me, ct));
    }

    /// <summary>
    /// The API master list (QPS APIController): the rows of API_MASTER the portal loads at start-up. Needs the static
    /// API token, not a session (the portal reads it before anyone signs in).
    /// </summary>
    [Route("api/v1/[Action]")]
    [AllowAnonymous]
    public sealed class APIController : ApiControllerBase
    {
        private readonly IAPI _api;
        public APIController(IAPI api) => _api = api;

        [HttpGet]
        public async Task<ActionResult<ApiMasterRes>> GetAllApi(CancellationToken ct) => Ok(await _api.GetAllApi(ct));

        [HttpPost]
        public async Task<ActionResult<ApiMasterRes>> GetSpecificApi([FromBody] ApiTaskReq req, CancellationToken ct) =>
            Ok(await _api.GetSpecificApiData(req.TaskName, req.Param, ct));
    }
}
