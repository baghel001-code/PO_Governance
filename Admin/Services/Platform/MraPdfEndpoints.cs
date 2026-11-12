using Application.Interfaces.V1;

namespace Admin.Services.Platform
{
    /// <summary>
    /// GET /mra-verdict/{token}/pdf and /mra-verdict/{token}/{mraNo}/pdf — "Download PDF" on the MRA verdict page (view_url, verdict-url) (no sign-in; the same
    /// token check as the page, by the API: 404 not valid, 410 expired).
    /// </summary>
    public static class MraPdfEndpoints
    {
        public static IEndpointRouteBuilder MapMraPdfEndpoints(this IEndpointRouteBuilder app)
        {
            // view_url of a /decisions answer (/mra-verdict/<token>) and GET /verdict-url (/mra-verdict/<token>/<mra>)
            app.MapGet("/mra-verdict/{token}/pdf", (string token, IAPICall api, IWebHostEnvironment env) => PdfAsync(token, "", api, env)).AllowAnonymous();
            app.MapGet("/mra-verdict/{token}/{mraNo}/pdf", (string token, string mraNo, IAPICall api, IWebHostEnvironment env) => PdfAsync(token, mraNo, api, env)).AllowAnonymous();
            return app;
        }

        private static async Task<IResult> PdfAsync(string token, string mraNo, IAPICall api, IWebHostEnvironment env)
        {
            var res = await api.MraVerdictViewAsync(token, mraNo);
            if (!res.IsSuccess || res.Data == null)
                return Results.Text(res.responseMessage ?? "This MRA verdict link cannot be opened.", "text/plain", statusCode: StatusCodes.Status404NotFound);
            var v = res.Data;
            var bytes = MraPdfReport.Build(v.Row, v.Detail, v.Owners, Path.Combine(env.WebRootPath, "img", "logo.png"), DateTimeOffset.Now, v.Note);
            return Results.File(bytes, "application/pdf", MraPdfReport.FileName(v.MraNo, v.Detail.Decision?.DecisionId));
        }
    }
}
