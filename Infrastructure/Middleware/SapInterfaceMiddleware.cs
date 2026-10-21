using Application.Interfaces.V1.Sap;
using Application.Services.V1.Sap;
using Domain.Entities.AppSettings;
using Domain.Entities.Sap;
using Domain.Exceptions;
using Infrastructure.Services.V1;
using Infrastructure.Utilitys;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Infrastructure.Middleware
{
    /// <summary>
    /// Front door of the SAP interface (<c>/api/sap/*</c> only — the portal's StaticTokenMiddleware / X-Session-Token /
    /// readiness middleware do not apply there): trace id, SapInterface:OnlyOnPort, SapInterface:Enabled, the IP allow-list
    /// (SapInterface:AllowedIps with CIDR; X-Forwarded-For only from SapInterface:TrustedProxies), SQL Server storage,
    /// database readiness and the body size limit. Every refusal and every unhandled error is written as the contract's
    /// Error body ({error_code, message, trace_id}). Basic authentication follows (scheme <c>SapBasic</c>).
    /// </summary>
    public sealed class SapInterfaceMiddleware
    {
        public const string PathPrefix = "/api/sap";
        public const string TraceItem = "Sap.TraceId";
        public const string ClientIpItem = "Sap.ClientIp";

        private readonly RequestDelegate _next;
        private readonly SapInterfaceSettings _cfg;
        private readonly IpAllowList _allowed;
        private readonly IpAllowList _proxies;
        private readonly ISapRepository _repo;
        private readonly DatabaseReadiness _readiness;
        private readonly ILogger<SapInterfaceMiddleware> _log;

        public SapInterfaceMiddleware(RequestDelegate next, IOptions<SapInterfaceSettings> cfg, ISapRepository repo, DatabaseReadiness readiness,
                                      ILogger<SapInterfaceMiddleware> log)
        {
            _next = next;
            _cfg = cfg.Value;
            _allowed = new IpAllowList(_cfg.AllowedIps);
            _proxies = new IpAllowList(_cfg.TrustedProxies);
            _repo = repo;
            _readiness = readiness;
            _log = log;
            if (_allowed.Count == 0)
                _log.LogWarning("SapInterface:AllowedIps is empty - every /api/sap call is refused (401 IP_NOT_ALLOWED)");
        }

        public static bool IsSapPath(PathString path) => path.StartsWithSegments(PathPrefix, StringComparison.OrdinalIgnoreCase);

        public static string TraceId(HttpContext ctx) => ctx.Items[TraceItem] as string ?? "";

        public async Task InvokeAsync(HttpContext ctx)
        {
            if (!IsSapPath(ctx.Request.Path))
            {
                await _next(ctx);
                return;
            }
            var trace = SapJson.NewTraceId();
            ctx.Items[TraceItem] = trace;
            ctx.Response.Headers["X-Trace-Id"] = trace;
            try
            {
                if (_cfg.OnlyOnPort > 0 && ctx.Connection.LocalPort != _cfg.OnlyOnPort)
                {
                    await WriteAsync(ctx, 404, SapErrorCodes.NotFound, "The SAP interface is served on port " + _cfg.OnlyOnPort + " only");
                    return;
                }
                var ip = IpAllowList.ClientAddress(ctx.Connection.RemoteIpAddress, ctx.Request.Headers["X-Forwarded-For"].ToString(), _proxies);
                var ipText = ip?.ToString() ?? "";
                ctx.Items[ClientIpItem] = ipText;
                if (!_allowed.Contains(ip))
                {
                    _log.LogWarning("SAP call from {Ip} refused: not in SapInterface:AllowedIps ({Method} {Path}, trace {TraceId})",
                        ipText, ctx.Request.Method, ctx.Request.Path, trace);
                    await WriteAsync(ctx, 401, SapErrorCodes.IpNotAllowed, "Caller IP " + ipText + " is not allowed");
                    return;
                }
                if (!_cfg.Enabled)
                {
                    await WriteAsync(ctx, 503, SapErrorCodes.InterfaceDisabled, "The SAP interface is switched off on the tool side - continue as NOT_CHECKED");
                    return;
                }
                if (!_repo.Available)
                {
                    await WriteAsync(ctx, 503, SapErrorCodes.StorageNotSupported, _repo.UnavailableReason ?? "SAP interface requires SQL Server storage");
                    return;
                }
                if (!_readiness.Ready)
                {
                    ctx.Response.Headers["Retry-After"] = StartupService.RetrySeconds.ToString();
                    await WriteAsync(ctx, 503, SapErrorCodes.DatabaseUnavailable, "The database is not reachable right now - continue as NOT_CHECKED");
                    return;
                }
                if (ctx.Request.ContentLength is long len && len > _cfg.MaxBodyBytes)
                {
                    await WriteAsync(ctx, 413, SapErrorCodes.BatchTooLarge, "Body larger than " + _cfg.MaxBodyBytes / (1024 * 1024) + " MB - split and resend");
                    return;
                }
                await _next(ctx);
            }
            catch (Exception ex) when (!ctx.RequestAborted.IsCancellationRequested && !ctx.Response.HasStarted)
            {
                var (status, code, message) = Map(ex);
                if (status >= 500) _log.LogError(ex, "SAP interface error {Status} {Code} (trace {TraceId})", status, code, trace);
                await WriteAsync(ctx, status, code, message);
            }
        }

        public static (int Status, string Code, string Message) Map(Exception ex) => ex switch
        {
            SapApiException s => (s.Status, s.ErrorCode, s.Message),
            BadHttpRequestException b => (b.StatusCode == 413 ? 413 : 400, b.StatusCode == 413 ? SapErrorCodes.BatchTooLarge : SapErrorCodes.InvalidJson, b.Message),
            _ when ExceptionMiddleware.IsDbUnreachable(ex) => (503, SapErrorCodes.DatabaseUnavailable, "The database is not reachable right now - retry later / continue as NOT_CHECKED"),
            _ => (503, SapErrorCodes.InternalError, "Server error: " + ex.Message)
        };

        /// <summary>The contract's Error body with the request's trace id.</summary>
        public static async Task WriteAsync(HttpContext ctx, int status, string errorCode, string message)
        {
            ctx.Response.Clear();
            ctx.Response.StatusCode = status;
            ctx.Response.ContentType = "application/json; charset=utf-8";
            ctx.Response.Headers.CacheControl = "no-store";
            var trace = TraceId(ctx);
            if (trace.Length > 0) ctx.Response.Headers["X-Trace-Id"] = trace;
            if (status == 401) ctx.Response.Headers.WWWAuthenticate = "Basic realm=\"PO Governance SAP interface\", charset=\"UTF-8\"";
            await ctx.Response.WriteAsync(SapJson.Error(errorCode, message, trace.Length > 0 ? trace : null));
        }
    }
}
