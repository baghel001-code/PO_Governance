using System.IO.Compression;
using System.Text;
using Domain.Exceptions;
using Microsoft.AspNetCore.Mvc;

namespace POGovApi.Controllers.V1
{
    /// <summary>
    /// Streams a stored gzip JSON payload (data-file version, saved run) inside the ApiDataResponse envelope
    /// without parsing it: {"responseCode":0,"responseMessage":"SUCCESS","recordId":0,"Data":&lt;payload&gt;}.
    /// </summary>
    public sealed class GzipJsonEnvelopeResult : IActionResult
    {
        private static readonly byte[] Head = Encoding.UTF8.GetBytes("{\"responseCode\":0,\"responseMessage\":\"SUCCESS\",\"recordId\":0,\"Data\":");
        private static readonly byte[] Tail = Encoding.UTF8.GetBytes("}");
        private readonly byte[] _gz;

        public GzipJsonEnvelopeResult(byte[] gz) => _gz = gz;

        public async Task ExecuteResultAsync(ActionContext context)
        {
            var res = context.HttpContext.Response;
            res.StatusCode = 200;
            res.ContentType = "application/json; charset=utf-8";
            res.Headers.CacheControl = "no-store";
            var ct = context.HttpContext.RequestAborted;
            await res.Body.WriteAsync(Head, ct);
            await using (var z = new GZipStream(new MemoryStream(_gz), CompressionMode.Decompress))
                await z.CopyToAsync(res.Body, 1 << 20, ct);
            await res.Body.WriteAsync(Tail, ct);
        }
    }

    public static class RequestBody
    {
        private static string TooLarge(int maxUploadMb) => $"Upload is larger than max_upload_mb ({maxUploadMb} MB) in config.json";

        /// <summary>
        /// A multipart/form-data body with one file part (any field name) and text fields — DataPut / RunSave.
        /// The max_upload_mb limit applies to the whole body.
        /// </summary>
        public static async Task<(byte[] File, IFormCollection Form)> ReadMultipartAsync(HttpRequest req, int maxUploadMb, CancellationToken ct)
        {
            long limit = (long)maxUploadMb * 1024 * 1024;
            if (req.ContentLength > limit) throw new AppValidationException(TooLarge(maxUploadMb));
            if (!req.HasFormContentType) throw new AppValidationException("Send the file as multipart/form-data");
            var form = await req.ReadFormAsync(ct);
            var f = form.Files.FirstOrDefault() ?? throw new AppValidationException("No file uploaded");
            if (f.Length > limit) throw new AppValidationException(TooLarge(maxUploadMb));
            using var ms = new MemoryStream((int)Math.Min(f.Length, int.MaxValue));
            await f.CopyToAsync(ms, ct);
            return (ms.ToArray(), form);
        }

        /// <summary>gzip magic bytes 1F 8B (v1.1.4 sent "X-Body-Encoding: gzip" instead).</summary>
        public static bool IsGzip(byte[] raw) => raw.Length >= 2 && raw[0] == 0x1F && raw[1] == 0x8B;
    }
}
