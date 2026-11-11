using Application.Interfaces.V1;

namespace Admin.Services.Platform
{
    /// <summary>
    /// GET /mra-verdict/{token}/{mraNo}/pdf — "Download PDF" on the page of SAP's verdict-url link (no sign-in; the same
    /// token check as the page, by the API: 404 not valid, 410 expired).
    /// </summary>
    public static class MraPdfEndpoints
    {
        public static IEndpointRouteBuilder MapMraPdfEndpoints(this IEndpointRouteBuilder app)
        {
            app.MapGet("/mra-verdict/{token}/{mraNo}/pdf", async (string token, string mraNo, IAPICall api, IWebHostEnvironment env, HttpContext http) =>
            {
                var res = await api.MraVerdictViewAsync(token, mraNo);
                if (!res.IsSuccess || res.Data == null)
                    return Results.Text(res.responseMessage ?? "This MRA verdict link cannot be opened.", "text/plain", statusCode: StatusCodes.Status404NotFound);
                var v = res.Data;
                var bytes = MraPdfReport.Build(v.Row, v.Detail, v.Owners, Path.Combine(env.WebRootPath, "img", "logo.png"), DateTimeOffset.Now);
                return Results.File(bytes, "application/pdf", MraPdfReport.FileName(v.Row.No));
            }).AllowAnonymous();
            return app;
        }
    }
}
