using Application.DIResolver;
using Domain.Constants;
using Domain.Entities.AppSettings;
using Domain.Entities.Response;
using Infrastructure.Services.V1.Excel;
using Infrastructure.ServicesConfiguration;
using Microsoft.AspNetCore.Mvc;
using Microsoft.OpenApi.Models;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

// ── logging: console + rolling file Logs/pogov-api-yyyyMMdd.log ─────────
builder.Host.UseSerilog((ctx, lc) => lc
    .ReadFrom.Configuration(ctx.Configuration)
    .Enrich.FromLogContext()
    .WriteTo.Console()
    .WriteTo.File(Path.Combine(ctx.HostingEnvironment.ContentRootPath, "Logs", "pogov-api-.log"),
        rollingInterval: RollingInterval.Day, retainedFileCountLimit: 30, fileSizeLimitBytes: 50_000_000, rollOnFileSizeLimit: true));

// ── uploads: allow bodies up to max_upload_mb (the endpoints check the limit themselves) ─
var maxMb = builder.Configuration.GetSection(PoGovSettings.SectionName).GetValue<int?>(nameof(PoGovSettings.MaxUploadMb)) ?? 400;
builder.WebHost.ConfigureKestrel(k => k.Limits.MaxRequestBodySize = (long)maxMb * 1024 * 1024);

// ── services ───────────────────────────────────────────────────────────
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddPlatformServices();
// ── decision engines, recommendation, analytics (Application) + their Excel writers (Infrastructure) ─
builder.Services.AddRecommendationServices();
builder.Services.AddRecommendationExcel();
builder.Services.AddGrcAnalyticsServices();
builder.Services.AddGrcExcelExport();
builder.Services.AddHistoryExcelExport();
builder.Services.AddIngestServices();
builder.Services.AddIngestInfrastructure();
builder.Services.AddTool1Services();
builder.Services.AddSingleton(builder.Configuration.GetSection("Tool1").Get<Application.Services.V1.Tool1.Tool1Settings>()
    ?? new Application.Services.V1.Tool1.Tool1Settings());
builder.Services.AddTool2Services();
builder.Services.AddTool2Excel();
builder.Services.AddAnalyticsServices();
builder.Services.AddAnalyticsAdapters();
builder.Services.AddAuthorization();

builder.Services.AddControllers()
    .AddJsonOptions(o => o.JsonSerializerOptions.Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping)
    .ConfigureApiBehaviorOptions(o => o.InvalidModelStateResponseFactory = ctx =>
    {
        var msg = ctx.ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage).FirstOrDefault(m => !string.IsNullOrEmpty(m))
                  ?? "Bad request";
        return new BadRequestObjectResult(ApiResponse<object>.Fail(400, msg));
    });

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo { Title = "PO Governance API", Version = "v1" });
    c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Type = SecuritySchemeType.Http, Scheme = "bearer", In = ParameterLocation.Header,
        Description = "Session token from POST api/v1/auth/login"
    });
    c.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        [new OpenApiSecurityScheme { Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" } }] = Array.Empty<string>()
    });
});

var app = builder.Build();

// ── pipeline ───────────────────────────────────────────────────────────
app.UseApiExceptionHandling();
app.UseSerilogRequestLogging(o => o.GetLevel = (ctx, _, ex) =>
    ex != null || ctx.Response.StatusCode >= 500 ? Serilog.Events.LogEventLevel.Error
    : ctx.Request.Path.StartsWithSegments("/api/v1/status") ? Serilog.Events.LogEventLevel.Debug
    : Serilog.Events.LogEventLevel.Information);

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.Use(async (ctx, next) =>
{
    ctx.Response.Headers.XContentTypeOptions = "nosniff";
    ctx.Response.Headers.XFrameOptions = "DENY";
    ctx.Response.Headers["Referrer-Policy"] = "same-origin";
    if (ctx.Request.Path.StartsWithSegments("/api")) ctx.Response.Headers.CacheControl = "no-store";
    await next();
});

app.UseRouting();
app.UseAuthentication();
app.UseMustChangePassword();
app.UseAuthorization();
app.MapControllers();

app.Run();

/// <summary>Entry point (public for WebApplicationFactory-based tests).</summary>
public partial class Program
{
    public const string AuthScheme = AppConstants.AuthScheme;
}
