using Admin.Auth;
using Admin.Components;
using Admin.ServiceRegistration;
using Domain.Constants;
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
builder.Services.AddPortal(builder.Configuration, builder.Environment);

var app = builder.Build();

app.Use(async (ctx, next) =>
{
    var h = ctx.Response.Headers;
    h["X-Content-Type-Options"] = "nosniff";
    h["X-Frame-Options"] = "SAMEORIGIN";
    h["Referrer-Policy"] = "same-origin";
    await next();
});

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
    app.UseHttpsRedirection();
}

app.UseStaticFiles();
app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();

// Sign out: ends the API session (app_sessions) and clears the portal cookie.
async Task<IResult> SignOut(HttpContext http, PortalAuthService auth)
{
    await auth.SignOutAsync(http);
    return Results.Redirect("/Account/Login");
}
app.MapGet("/Account/Logout", SignOut);
app.MapPost("/Account/Logout", SignOut);

app.MapRazorComponents<App>().AddInteractiveServerRenderMode();
app.Lifetime.ApplicationStopped.Register(Log.CloseAndFlush);
app.Run();
