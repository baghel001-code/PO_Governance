using System.Text.Json;
using Domain.Entities.Base;
using Infrastructure.Services.V1;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;

namespace Infrastructure.Middleware
{
    /// <summary>
    /// While <see cref="StartupService"/> has not finished (database down, schema missing …), answers every api/v1 call
    /// except Health with 503 "The database is not reachable right now …". In Development the reason is appended so a
    /// developer sees e.g. "Cannot open database "POGovernance" requested by the login" on the portal's sign-in page.
    /// </summary>
    public sealed class DatabaseReadinessMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly DatabaseReadiness _readiness;
        private readonly bool _showReason;

        public DatabaseReadinessMiddleware(RequestDelegate next, DatabaseReadiness readiness, IWebHostEnvironment env)
        {
            _next = next;
            _readiness = readiness;
            _showReason = env.IsDevelopment();
        }

        public async Task InvokeAsync(HttpContext context)
        {
            var path = context.Request.Path.Value ?? "";
            if (_readiness.Ready
                || !path.StartsWith("/api/v1/", StringComparison.OrdinalIgnoreCase)
                || path.Equals("/api/v1/Health", StringComparison.OrdinalIgnoreCase))
            {
                await _next(context);
                return;
            }
            var msg = ExceptionMiddleware.DbUnreachable;
            if (_showReason)
                msg += " Reason: " + (_readiness.Error ?? "the API is still starting (connecting to the database).");
            context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            context.Response.Headers["Retry-After"] = StartupService.RetrySeconds.ToString();
            context.Response.ContentType = "application/json; charset=utf-8";
            await context.Response.WriteAsync(JsonSerializer.Serialize(new ReadinessResponse { responseCode = 503, responseMessage = msg }));
        }

        private sealed class ReadinessResponse : BaseResponse { }
    }
}
