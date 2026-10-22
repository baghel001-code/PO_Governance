using Admin.Auth;
using Admin.Components;
using Admin.ServiceRegistration;
using Domain.Constants;
using Infrastructure.Services.V1;
using Infrastructure.ServicesConfiguration;
using MudBlazor;
using MudBlazor.Services;
using Serilog;

Log.Logger = new LoggerConfiguration().WriteTo.Console().CreateBootstrapLogger();

var builder = WebApplication.CreateBuilder(args);
builder.Host.UseSerilog((ctx, lc) => lc
    .ReadFrom.Configuration(ctx.Configuration)
    .WriteTo.Console()
    .WriteTo.File(Path.Combine(AppContext.BaseDirectory, "Logs", "portal-.log"), rollingInterval: RollingInterval.Day, retainedFileCountLimit: 30));

builder.Services.AddRazorComponents().AddInteractiveServerComponents();
builder.Services.AddServerSideBlazor()
    .AddHubOptions(o =>
    {
        o.MaximumReceiveMessageSize = 50 * 1024 * 1024; // planner uploads go through the circuit
        o.KeepAliveInterval = TimeSpan.FromSeconds(15);
        o.ClientTimeoutInterval = TimeSpan.FromSeconds(30);
    });
builder.Services.AddMudServices(c =>
{
    c.SnackbarConfiguration.PositionClass = Defaults.Classes.Position.TopCenter;
    c.SnackbarConfiguration.VisibleStateDuration = 2800; // v1.1.4 toast duration
    c.SnackbarConfiguration.ShowCloseIcon = true;
});
builder.Services.AddHttpContextAccessor();
// QPS: MasterApiSettings (where GetAllApi is + the encoded static API token) and ApiNamesSettings (task names)
ConfigurationInitializer.InitializeApiSettings(builder.Configuration);
builder.Services.AddPortal(builder.Configuration, builder.Environment);

var app = builder.Build();

// QPS: load the API master list (AllApi.ApiDetails) through AllApiNames.MasterApi = GetAllApi. When POGovApi is not up yet the
// portal still starts (the error is logged) and APICallService loads the list on the first call.
{
    var log = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("ApiMaster");
    var apiMaster = new APICallService(builder.Configuration.GetValue<int>("AppConfigurationSettings:ProxyValue"), null, log);
    if (!await apiMaster.LoadApiMasterAsync())
        log.LogError("POGovApi API master not loaded at start-up ({Url}) - sign-in shows 'Cannot reach the server' until POGovApi answers.",
            Domain.Entities.Common.ApiMasterData.apiData?.Api_Url);
}

app.Use(async (ctx, next) =>
{
    var h = ctx.Response.Headers;
    h["X-Content-Type-Options"] = "nosniff";
    h["X-Frame-Options"] = "SAMEORIGIN";
    h["Referrer-Policy"] = "same-origin";
    await next();
});

if (!app.Environment.IsDevelopment())
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
// HTTPS only when the portal really is served over https (PortalSettings:UseHttps). On plain http://host:port the
// redirect has no https port ("Failed to determine the https port for redirect") and a Secure cookie would be dropped.
if (builder.Configuration.GetValue<bool>("PortalSettings:UseHttps"))
{
    app.UseHsts();
    app.UseHttpsRedirection();
}

app.UseStaticFiles();
app.UseAuthentication();
app.UseAuthorization();
app.UseAccountRedirects();   // signed in → skip the sign-in page; must change password → change-password page
app.UseAntiforgery();

// Sign-in, password change, sign-out and the "/" start page are plain HTTP endpoints (Auth/AccountEndpoints.cs):
// they set the cookie and redirect without going through a Blazor page.
app.MapAccountEndpoints();

app.MapRazorComponents<App>().AddInteractiveServerRenderMode();
app.Lifetime.ApplicationStopped.Register(Log.CloseAndFlush);
app.Run();
