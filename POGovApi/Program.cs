using Application.DIResolver;
using Domain.Constants;
using Domain.Entities.AppSettings;
using Domain.Entities.Common;
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
// ── SAP R/3 interface (/api/sap/v1, spec v1.4): SapInterface settings, PRO_SAP_* repository, SapBasic scheme ─
builder.Services.AddSapInterface(builder.Configuration);
builder.Services.AddAuthorization();

builder.Services.AddControllers()
    .AddJsonOptions(o =>
    {
        // QPS: keep the C# property names (PascalCase) - DTOs with [JsonPropertyName] keep their v1.1.4 camelCase names.
        o.JsonSerializerOptions.PropertyNamingPolicy = null;
        o.JsonSerializerOptions.Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping;
    })
    .ConfigureApiBehaviorOptions(o => o.InvalidModelStateResponseFactory = ctx =>
    {
        var msg = ctx.ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage).FirstOrDefault(m => !string.IsNullOrEmpty(m))
                  ?? "Bad request";
        return new BadRequestObjectResult(ApiDataResponse<object>.Fail(400, msg));
    });

// ── CORS (QPS "AllowBlazorApp"): browser origins allowed to call the API; the portal calls it server-side (no Origin) ─
var corsSettings = builder.Configuration.GetSection("Cors").Get<CorsSettings>() ?? new CorsSettings();
builder.Services.AddCors(o => o.AddPolicy("AllowBlazorApp", policy =>
    policy.WithOrigins(corsSettings.AllowBlazorApp).AllowAnyHeader().AllowAnyMethod()));

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo { Title = "PO Governance API", Version = "v1" });
    c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Type = SecuritySchemeType.Http, Scheme = "bearer", In = ParameterLocation.Header,
        Description = "The static API token (JwtSettings:SecretKey decoded with MyNewEncDec) - every api/v1 call except Health"
    });
    c.AddSecurityDefinition("SessionToken", new OpenApiSecurityScheme
    {
        Type = SecuritySchemeType.ApiKey, Name = AppConstants.SessionHeader, In = ParameterLocation.Header,
        Description = "The user's session token from POST api/v1/AuthLogin (Data.token)"
    });
    c.AddSecurityDefinition(POGovApi.Swagger.SapSwaggerFilter.SchemeName, new OpenApiSecurityScheme
    {
        Type = SecuritySchemeType.Http, Scheme = "basic",
        Description = "SAP technical user (api/sap/v1 only) - user name and password from the portal's SAP Interface page"
    });
    c.OperationFilter<POGovApi.Swagger.SapSwaggerFilter>();
    c.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        [new OpenApiSecurityScheme { Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" } }] = Array.Empty<string>(),
        [new OpenApiSecurityScheme { Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "SessionToken" } }] = Array.Empty<string>()
    });
});

var app = builder.Build();

// ── pipeline ───────────────────────────────────────────────────────────
app.UseApiExceptionHandling();
app.UseSapInterface();          // /api/sap/*: trace id, IP allow-list, enabled / storage / database checks, SAP error bodies
app.UseDatabaseReadiness();     // 503 + reason until the database is reachable (the API itself always starts)
app.UseSerilogRequestLogging(o => o.GetLevel = (ctx, _, ex) =>
    ex != null || ctx.Response.StatusCode >= 500 ? Serilog.Events.LogEventLevel.Error
    : ctx.Request.Path.StartsWithSegments("/api/v1/GetStatus") || ctx.Request.Path.StartsWithSegments("/api/v1/AuthMe") ? Serilog.Events.LogEventLevel.Debug
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

// QPS origin check: a browser request from an origin that is not in Cors:AllowBlazorApp is refused.
var allowedOrigins = new HashSet<string>(corsSettings.AllowBlazorApp.Select(o => o.TrimEnd('/')), StringComparer.OrdinalIgnoreCase);
app.Use(async (ctx, next) =>
{
    var origin = ctx.Request.Headers.Origin.ToString();
    if (!string.IsNullOrEmpty(origin) && !allowedOrigins.Contains(origin.TrimEnd('/')))
    {
        await Infrastructure.Middleware.ExceptionMiddleware.WriteAsync(ctx, 403, "Origin not allowed");
        return;
    }
    await next();
});
app.UseCors("AllowBlazorApp");

app.UseRouting();
app.UseStaticToken();        // Authorization: Bearer <static API token> (QPS StaticTokenMiddleware)
app.UseAuthentication();     // X-Session-Token: <user session> (SessionTokenAuthenticationHandler)
app.UseMustChangePassword();
app.UseAuthorization();
app.MapControllers();

app.Run();

/// <summary>Entry point (public for WebApplicationFactory-based tests).</summary>
public partial class Program
{
    public const string AuthScheme = AppConstants.AuthScheme;
}
