using Application.DTOs.Platform;
using Application.Interfaces.V1;
using Domain.Entities.AppSettings;
using Domain.Entities.Response;
using Domain.Exceptions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace POGovApi.Controllers.V1
{
    /// <summary>Sign-in, sign-out, password change (server.py /api/auth/*) and health.</summary>
    [Route("api/v1/auth")]
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

        /// <summary>Returns a session token (send it as "Authorization: Bearer &lt;token&gt;").</summary>
        [AllowAnonymous]
        [HttpPost("login")]
        public async Task<ActionResult<ApiResponse<LoginResponse>>> Login([FromBody] LoginRequest body, CancellationToken ct)
        {
            await Task.Delay(300, ct);   // slows password guessing
            var ip = Request.Headers["X-Forwarded-For"].ToString().Split(',')[0].Trim();
            if (string.IsNullOrEmpty(ip)) ip = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "";
            var (token, err) = await _acc.LoginAsync(body.Email ?? "", body.Password ?? "", ip, ct);
            if (err != null) throw new AppException(401, err);
            var u = (await _acc.GetUserAsync(body.Email ?? "", ct))!;
            return Success(new LoginResponse
            {
                Token = token!, MustChange = u.MustChange, Name = u.Name, User = _acc.Public(u),
                ExpiresIn = (int)_cfg.SessionIdleSeconds
            });
        }

        [HttpPost("logout")]
        public async Task<ActionResult<ApiResponse<OkResponse>>> Logout(CancellationToken ct)
        {
            if (!string.IsNullOrEmpty(Me.SessionToken)) await _acc.LogoutAsync(Me.SessionToken, ct);
            return Success(OkResponse.Instance);
        }

        /// <summary>Changes the password and signs out the user's other sessions.</summary>
        [HttpPost("change-password")]
        public async Task<ActionResult<ApiResponse<OkResponse>>> ChangePassword([FromBody] ChangePasswordRequest body, CancellationToken ct)
        {
            var err = await _acc.ChangePasswordAsync(Me.Email, body.Old ?? "", body.New ?? "", ct);
            if (err != null) throw new AppValidationException(err);
            await _acc.EndSessionsAsync(Me.Email, Me.SessionToken, ct);   // sign out other devices
            return Success(OkResponse.Instance);
        }

        [HttpGet("me")]
        public async Task<ActionResult<ApiResponse<MeResponse>>> GetMe(CancellationToken ct) =>
            Success(new MeResponse { User = await _status.MePublicAsync(Me, ct), PasswordMinLength = _cfg.PasswordMinLengthOrDefault });
    }

    [Route("api/v1/health")]
    public sealed class HealthController : ApiControllerBase
    {
        private readonly IStatusService _status;
        public HealthController(IStatusService status) => _status = status;

        [AllowAnonymous]
        [HttpGet]
        public ActionResult<ApiResponse<HealthResponse>> Get() => Success(_status.Health());
    }

    /// <summary>server.py GET /api/status: versions, datasets, params, today's request seq, me, pending approvals, AI flags.</summary>
    [Route("api/v1/status")]
    public sealed class StatusController : ApiControllerBase
    {
        private readonly IStatusService _status;
        public StatusController(IStatusService status) => _status = status;

        [HttpGet]
        public async Task<ActionResult<ApiResponse<StatusResponse>>> Get(CancellationToken ct) => Success(await _status.GetStatusAsync(Me, ct));
    }
}
