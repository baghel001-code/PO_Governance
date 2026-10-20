using Application.DTOs.Ingest;
using Application.Interfaces.V1.Ingest;
using Domain.Entities.Response;
using Domain.Exceptions;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace POGovApi.Controllers.V1
{
    /// <summary>
    /// Planner "Data Setup" uploads. The files are parsed on the server exactly like v1.1.4 parsed them in
    /// the browser (SheetJS), then saved as ONE upload batch (shared.js pushFiles): each resulting
    /// dataset becomes a new current version labelled with the slot status text.
    /// Multipart form; files in any form field (conventionally "files" / "file"), other inputs as text fields.
    /// </summary>
    [Route("api/v1/[Action]")]
    [IngestUploadLimit]
    public sealed class IngestController : ApiControllerBase
    {
        private readonly IDataIngestService _ingest;

        public IngestController(IDataIngestService ingest) => _ingest = ingest;

        /// <summary>One data slot (loadFile): form field Key = f1, f2, f3, f4, um, bsr, artmaster, attrrel, colmaster, catheadmap.
        /// BSR uploads add to the current BSR rows unless form field Append is "false".</summary>
        [HttpPost]
        public async Task<ActionResult<ApiDataResponse<IngestResponse>>> IngestFile(CancellationToken ct = default)
        {
            RequireStaff();
            var (files, form) = await ReadFilesAsync(ct);
            var append = !bool.TryParse(form["Append"].ToString(), out var a) || a;
            return Success(await _ingest.ImportFilesAsync(form["Key"].ToString(), files, Me, append, ct));
        }

        /// <summary>Zip bundle with every input file (loadZipBundle).</summary>
        [HttpPost]
        public async Task<ActionResult<ApiDataResponse<IngestResponse>>> IngestZip(CancellationToken ct = default)
        {
            RequireStaff();
            var (files, _) = await ReadFilesAsync(ct);
            return Success(await _ingest.ImportZipAsync(files[0], Me, ct));
        }

        /// <summary>Raw system exports, one or many (handleRawImport).</summary>
        [HttpPost]
        public async Task<ActionResult<ApiDataResponse<IngestResponse>>> IngestRaw(CancellationToken ct = default)
        {
            RequireStaff();
            var (files, _) = await ReadFilesAsync(ct);
            return Success(await _ingest.ImportRawAsync(files, Me, ct));
        }

        /// <summary>GRC buying-history file (loadGRCFile).</summary>
        [HttpPost]
        public async Task<ActionResult<ApiDataResponse<IngestResponse>>> IngestGrc(CancellationToken ct = default)
        {
            RequireStaff();
            var (files, _) = await ReadFilesAsync(ct);
            return Success(await _ingest.ImportGrcAsync(files[0], Me, ct));
        }

        private async Task<(List<IngestUpload> Files, IFormCollection Form)> ReadFilesAsync(CancellationToken ct)
        {
            var limit = IngestUploadLimitAttribute.LimitBytes(HttpContext);
            if (Request.ContentLength is long len && len > limit)
                throw new AppValidationException(IngestUploadLimitAttribute.TooLargeMessage(HttpContext));
            if (!Request.HasFormContentType) throw new AppValidationException("Send the file(s) as multipart/form-data");
            var form = await Request.ReadFormAsync(ct);
            var list = new List<IngestUpload>();
            foreach (var f in form.Files)
            {
                using var ms = new MemoryStream();
                await f.CopyToAsync(ms, ct);
                list.Add(new IngestUpload(Path.GetFileName(f.FileName), ms.ToArray()));
            }
            if (list.Count == 0) throw new AppValidationException("No file uploaded");
            return (list, form);
        }
    }

    /// <summary>
    /// Raises the request body / multipart limits of the ingest endpoints to <c>max_upload_mb</c>
    /// (config "Ingest:MaxUploadMb" or "max_upload_mb", default 400 — v1.1.4 config.json).
    /// </summary>
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
    public sealed class IngestUploadLimitAttribute : Attribute, IResourceFilter, IOrderedFilter
    {
        public int Order => -2000;

        public static long MaxUploadMb(HttpContext ctx)
        {
            var cfg = ctx.RequestServices.GetService<IConfiguration>();
            var s = cfg?["Ingest:MaxUploadMb"] ?? cfg?["max_upload_mb"];
            return long.TryParse(s, out var mb) && mb > 0 ? mb : 400;
        }

        public static long LimitBytes(HttpContext ctx) => MaxUploadMb(ctx) * 1024 * 1024;

        public static string TooLargeMessage(HttpContext ctx) =>
            $"Upload is larger than max_upload_mb ({MaxUploadMb(ctx)} MB) in config.json";

        public void OnResourceExecuting(ResourceExecutingContext context)
        {
            var limit = LimitBytes(context.HttpContext);
            var size = context.HttpContext.Features.Get<IHttpMaxRequestBodySizeFeature>();
            if (size is { IsReadOnly: false }) size.MaxRequestBodySize = limit + 1024 * 1024;
            var formOptions = new FormOptions { MultipartBodyLengthLimit = limit, ValueLengthLimit = int.MaxValue };
            context.HttpContext.Features.Set<IFormFeature>(new FormFeature(context.HttpContext.Request, formOptions));
        }

        public void OnResourceExecuted(ResourceExecutedContext context) { }
    }
}
