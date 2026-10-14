using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Application.Interfaces.V1;
using Application.Services.V1.Platform;
using Domain.Constants;
using Domain.Exceptions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace POGovApi.Controllers.V1
{
    /// <summary>
    /// History tab Excel downloads (history.js built these in the browser with SheetJS):
    /// the Tool 1 request export, all files of one upload as one workbook, and one stored file version.
    /// Needs <c>builder.Services.AddHistoryExcelExport()</c> (Infrastructure) in Program.cs.
    /// </summary>
    [Route("api/v1/history/export")]
    public sealed class HistoryExportController : ApiControllerBase
    {
        private const string XlsxContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
        private readonly IHistoryService _history;
        private readonly IDataService _data;
        private readonly IHistoryExcelExporter _excel;

        public HistoryExportController(IHistoryService history, IDataService data, IHistoryExcelExporter excel)
        {
            _history = history;
            _data = data;
            _excel = excel;
        }

        public sealed class RequestExportBody
        {
            [JsonPropertyName("from")] public string? From { get; set; }
            [JsonPropertyName("to")] public string? To { get; set; }
            /// <summary>Search text (Request ID, GM, colour, user, MC desc, brand, division).</summary>
            [JsonPropertyName("q")] public string? Q { get; set; }
            /// <summary>Exact decision, or empty for all.</summary>
            [JsonPropertyName("decision")] public string? Decision { get; set; }
        }

        /// <summary>
        /// "⬇ Excel" on Tool 1 requests: the requests of the range the user may see, filtered like the screen,
        /// as Tool1_Requests_&lt;from|all&gt;_to_&lt;to|today&gt;.xlsx. 400 "Nothing to export" when none match.
        /// </summary>
        [HttpPost("requests")]
        [Produces(XlsxContentType, "application/json")]
        public async Task<IActionResult> Requests([FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] RequestExportBody? body, CancellationToken ct)
        {
            body ??= new RequestExportBody();
            var from = body.From ?? "";
            var to = body.To ?? "";
            var all = (await _history.RequestsAsync(from, to, Me, ct)).Items;
            var q = (body.Q ?? "").ToLowerInvariant();
            var dec = body.Decision ?? "";
            var list = all.Where(r =>
            {
                if (dec.Length > 0 && Str(r, "decision") != dec) return false;
                if (q.Length == 0) return true;
                var i = r["info"] as JsonObject;
                return string.Join(" ", Str(r, "reqId"), Str(r, "gm"), Str(r, "colour"), Str(r, "requestedBy"),
                    Str(i, "mcDesc"), Str(i, "brand"), Str(i, "div")).ToLowerInvariant().Contains(q);
            }).ToList();
            if (list.Count == 0) throw new AppValidationException("Nothing to export");
            var name = $"Tool1_Requests_{(from.Length > 0 ? from : "all")}_to_{(to.Length > 0 ? to : "today")}.xlsx";
            return File(_excel.WriteRequests(list), XlsxContentType, name);
        }

        /// <summary>"⬇ All files (one Excel)": every stored file of the upload, one sheet each, as &lt;uploadId&gt;.xlsx.</summary>
        [HttpGet("uploads/{uploadId}")]
        [Produces(XlsxContentType, "application/json")]
        public async Task<IActionResult> Upload(string uploadId, CancellationToken ct)
        {
            RequireRole("Planner or admin only", Roles.Planner, Roles.Admin);
            var g = (await _history.UploadsAsync(null, null, ct)).Items.FirstOrDefault(x => x.UploadId == uploadId)
                    ?? throw new NotFoundException("No such upload");
            var sheets = new List<HistoryDatasetSheet>();
            foreach (var f in g.Files)
            {
                if (Str(f, "action") == "clear" || !(f["stored"] is JsonValue sv && sv.GetValueKind() == JsonValueKind.True)) continue;
                sheets.Add(new HistoryDatasetSheet(Str(f, "key"), await RowsAsync(Str(f, "key"), Str(f, "version"), ct)));
            }
            if (sheets.Count == 0) throw new NotFoundException("The files of this upload are no longer stored");
            return File(_excel.WriteDatasets(sheets, single: false), XlsxContentType, uploadId + ".xlsx");
        }

        /// <summary>"⬇ Excel" on one file of an upload: [&lt;uploadId&gt;_]&lt;KEY&gt;.xlsx.</summary>
        [HttpGet("data/{key}")]
        [Produces(XlsxContentType, "application/json")]
        public async Task<IActionResult> DataVersion(string key, [FromQuery] string? version, [FromQuery] string? uploadId, CancellationToken ct)
        {
            RequireRole("Planner or admin only", Roles.Planner, Roles.Admin);
            if (!PlatformCore.KeyRe.IsMatch(key)) throw new AppValidationException("Bad dataset key");
            if (string.IsNullOrEmpty(version)) throw new AppValidationException("Missing version");
            var rows = await RowsAsync(key, version, ct);
            var name = (string.IsNullOrEmpty(uploadId) ? "" : uploadId + "_") + key.ToUpperInvariant() + ".xlsx";
            return File(_excel.WriteDatasets(new[] { new HistoryDatasetSheet(key, rows) }, single: true), XlsxContentType, name);
        }

        /// <summary>The "rows" array of a stored version ({"rows":[…], "meta":{…}}, gzip JSON).</summary>
        private async Task<JsonElement> RowsAsync(string key, string version, CancellationToken ct)
        {
            var gz = await _data.GetPayloadGzipAsync(key, version, ct);
            await using var z = new GZipStream(new MemoryStream(gz), CompressionMode.Decompress);
            using var doc = await JsonDocument.ParseAsync(z, cancellationToken: ct);
            return doc.RootElement.ValueKind == JsonValueKind.Object && doc.RootElement.TryGetProperty("rows", out var rows)
                ? rows.Clone()
                : JsonDocument.Parse("[]").RootElement.Clone();
        }

        private static string Str(JsonObject? o, string key) => o?[key] is JsonValue v
            ? v.GetValueKind() switch
            {
                JsonValueKind.String => v.GetValue<string>(),
                JsonValueKind.Number => v.ToJsonString(),
                _ => ""
            }
            : "";
    }
}
