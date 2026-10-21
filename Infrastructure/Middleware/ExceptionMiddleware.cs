using System.Net.Sockets;
using System.Text.Json;
using DecisionEngine.Common;
using Domain.Entities.Response;
using Domain.Exceptions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Infrastructure.Middleware
{
    /// <summary>
    /// Turns exceptions into the <see cref="ApiDataResponse{T}"/> envelope (responseCode = HTTP status, responseMessage, Data = payload) (server.py route() error handling):
    /// <list type="bullet">
    /// <item><see cref="AppException"/> → its status, message and payload (e.g. 409 {conflict, byOthers, seq})</item>
    /// <item>a DecisionEngine rule error the service did not translate → 404 (<see cref="EngineNotFoundException"/>) / 400</item>
    /// <item>database not reachable → 503 "The database is not reachable right now - please try again in a minute."</item>
    /// <item>bad JSON body → 400 with the parser message</item>
    /// <item>anything else → 500 "Server error: …"</item>
    /// </list>
    /// </summary>
    public sealed class ExceptionMiddleware
    {
        public const string DbUnreachable = "The database is not reachable right now - please try again in a minute.";
        /// <summary>Same JSON shape as the controllers (PropertyNamingPolicy = null, QPS).</summary>
        public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
        {
            PropertyNamingPolicy = null,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };

        private readonly RequestDelegate _next;
        private readonly ILogger<ExceptionMiddleware> _log;

        public ExceptionMiddleware(RequestDelegate next, ILogger<ExceptionMiddleware> log)
        {
            _next = next;
            _log = log;
        }

        public async Task InvokeAsync(HttpContext ctx)
        {
            try
            {
                await _next(ctx);
            }
            catch (Exception e) when (ctx.RequestAborted.IsCancellationRequested)
            {
                // client went away (the portal cancels a call when its page closes); SqlClient reports that as
                // SqlException 3980 "Operation cancelled by user" rather than OperationCanceledException.
                _log.LogDebug("Request aborted by the client: {Error}", e.Message);
            }
            catch (Exception e)
            {
                if (ctx.Response.HasStarted)
                {
                    _log.LogError(e, "API error after the response started");
                    throw;
                }
                if (SapInterfaceMiddleware.IsSapPath(ctx.Request.Path))
                {
                    // the SAP interface answers with the contract's Error body, never the portal envelope
                    var (sapStatus, code, sapMessage) = SapInterfaceMiddleware.Map(e);
                    if (sapStatus >= 500) _log.LogError(e, "SAP interface error ({Status})", sapStatus);
                    await SapInterfaceMiddleware.WriteAsync(ctx, sapStatus, code, sapMessage);
                    return;
                }
                var (status, message, payload) = Map(e);
                if (status >= 500) _log.LogError(e, "API error ({Status})", status);
                await WriteAsync(ctx, status, message, payload);
            }
        }

        public static bool IsDbUnreachable(Exception e)
        {
            for (var x = e; x != null; x = x.InnerException)
            {
                if (x is PostgresException pe) return pe.SqlState is "57P01" or "57P02" or "57P03" or "53300";
                if (x is NpgsqlException || x is SocketException) return true;
                if (x is Microsoft.Data.SqlClient.SqlException && Repositories.SqlServerStore.IsConnectionError(x)) return true;
            }
            return false;
        }

        public static (int Status, string Message, object? Payload) Map(Exception e) => e switch
        {
            AppException a => (a.Status, a.Message, a.Payload),
            EngineNotFoundException n => (404, n.Message, null),
            DecisionEngineException d => (400, d.Message, null),
            _ when IsDbUnreachable(e) => (503, DbUnreachable, null),
            JsonException j => (400, j.Message, null),
            BadHttpRequestException b => (b.StatusCode, b.Message, null),
            _ => (500, "Server error: " + e.Message, null)
        };

        public static async Task WriteAsync(HttpContext ctx, int status, string message, object? payload = null)
        {
            ctx.Response.Clear();
            ctx.Response.StatusCode = status;
            ctx.Response.ContentType = "application/json; charset=utf-8";
            ctx.Response.Headers.CacheControl = "no-store";
            await ctx.Response.WriteAsync(JsonSerializer.Serialize(ApiDataResponse<object>.Fail(status, message, payload), Json));
        }
    }
}
