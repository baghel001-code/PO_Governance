using System.IO.Compression;
using System.Text;
using Domain.Exceptions;
using Microsoft.AspNetCore.Mvc;

namespace POGovApi.Controllers.V1
{
    /// <summary>
    /// Streams a stored gzip JSON payload (data-file version, saved run) inside the ApiResponse
    /// envelope without parsing it: {"responseCode":0,"responseMessage":"SUCCESS","data":&lt;payload&gt;,"isSuccess":true}.
    /// </summary>
    public sealed class GzipJsonEnvelopeResult : IActionResult
    {
        private static readonly byte[] Head = Encoding.UTF8.GetBytes("{\"responseCode\":0,\"responseMessage\":\"SUCCESS\",\"data\":");
        private static readonly byte[] Tail = Encoding.UTF8.GetBytes(",\"isSuccess\":true}");
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
        /// <summary>server.py read_body with the max_upload_mb limit.</summary>
        public static async Task<byte[]> ReadAllAsync(HttpRequest req, int maxUploadMb, CancellationToken ct)
        {
            long limit = (long)maxUploadMb * 1024 * 1024;
            if (req.ContentLength > limit)
                throw new AppValidationException($"Upload is larger than max_upload_mb ({maxUploadMb} MB) in config.json");
            using var ms = new MemoryStream(req.ContentLength is > 0 and < int.MaxValue ? (int)req.ContentLength.Value : 0);
            var buf = new byte[1 << 20];
            int n;
            while ((n = await req.Body.ReadAsync(buf, ct)) > 0)
            {
                ms.Write(buf, 0, n);
                if (ms.Length > limit) throw new AppValidationException($"Upload is larger than max_upload_mb ({maxUploadMb} MB) in config.json");
            }
            return ms.ToArray();
        }
    }
}
