using System.Text.Json;
using Application.Interfaces.V1;
using Application.Interfaces.V1.Sap;
using DecisionEngine.Sap;
using Domain.Entities.AppSettings;
using Domain.Entities.Sap;
using Domain.Exceptions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Application.Services.V1.Sap
{
    /// <summary>
    /// POST /api/sap/v1/mra-status: SAP sends every change of an MRA during the day (created, discarded, rejected,
    /// self-rejected …). Required per line: mra_no, qty, status; generic_article + colour pick the line of an MRA with
    /// several lines; every other field is kept when not sent. The whole batch is checked first (any wrong line → 400,
    /// nothing stored), then applied in order in one transaction (PRO_SAP_MRA_STATUS APPLY): CREATED / UPDATED /
    /// UNCHANGED / STALE (older changed_at than stored) / AMBIGUOUS (several lines, no article / colour). Safe to resend.
    /// The morning file in Data Setup is merged into the lines (IMraService.UploadAsync).
    /// </summary>
    public sealed class SapMraService : ISapMraService
    {
        private readonly IMraRepository _repo;
        private readonly IAppClock _clock;
        private readonly SapInterfaceSettings _cfg;
        private readonly ILogger<SapMraService> _log;

        public SapMraService(IMraRepository repo, IAppClock clock, IOptions<SapInterfaceSettings> cfg, ILogger<SapMraService> log)
        {
            _repo = repo;
            _clock = clock;
            _cfg = cfg.Value;
            _log = log;
        }

        private static string? Check(string? s, int max, string name, int i) =>
            s != null && s.Trim().Length > max ? $"mras[{i}].{name} must be at most {max} characters" : null;

        /// <summary>The lines to store, or the first problem.</summary>
        public static (List<MraLine>? Lines, string? Problem) Validate(SapMraStatusRequest? r, IReadOnlyCollection<string> allowed)
        {
            if (r == null) return (null, "The body must be a JSON object");
            if (r.Mras == null || r.Mras.Count == 0) return (null, "mras must be a non-empty array");
            var lines = new List<MraLine>();
            for (var i = 0; i < r.Mras.Count; i++)
            {
                var m = r.Mras[i];
                if (m == null) return (null, $"mras[{i}] must be an object");
                if (string.IsNullOrWhiteSpace(m.MraNo)) return (null, $"mras[{i}].mra_no is required");
                if (m.Qty == null) return (null, $"mras[{i}].qty is required");
                if (m.Qty < 0) return (null, $"mras[{i}].qty must not be negative");
                if (m.Qty > 1_000_000_000m) return (null, $"mras[{i}].qty is too large");
                if (string.IsNullOrWhiteSpace(m.Status)) return (null, $"mras[{i}].status is required");
                var status = MraStatuses.Normalize(m.Status, allowed);
                if (status == null) return (null, $"mras[{i}].status '{m.Status}' is not one of {string.Join(", ", allowed)}");
                var p = Check(m.MraNo, 20, "mra_no", i) ?? Check(m.GenericArticle, 40, "generic_article", i) ?? Check(m.Colour, 40, "colour", i)
                        ?? Check(m.RequestId, 80, "request_id", i) ?? Check(m.GenericArtDesc, 200, "generic_art_desc", i)
                        ?? Check(m.VendorCode, 20, "vendor_code", i) ?? Check(m.VendorName, 200, "vendor_name", i)
                        ?? Check(m.BrandType, 40, "brand_type", i) ?? Check(m.McCode, 20, "mc_code", i) ?? Check(m.McName, 100, "mc_name", i)
                        ?? Check(m.PlanningFlag, 20, "planning_flag", i) ?? Check(m.Fit, 100, "fit", i) ?? Check(m.Design, 100, "design", i)
                        ?? Check(m.Style, 100, "style", i) ?? Check(m.Fabric, 100, "fabric", i) ?? Check(m.ProductMaterial, 100, "product_material", i)
                        ?? Check(m.ApprovalLevel, 30, "approval_level", i) ?? Check(m.Remark, 500, "remark", i);
                if (p != null) return (null, p);
                DateTimeOffset? changed = null;
                if (!string.IsNullOrWhiteSpace(m.ChangedAt))
                {
                    if (!SapJson.TryParseTimestamp(m.ChangedAt, out var t)) return (null, $"mras[{i}].changed_at must be an ISO 8601 timestamp with offset");
                    changed = t;
                }
                DateTime? delivery = null;
                if (!string.IsNullOrWhiteSpace(m.DeliveryDate))
                {
                    if (!SapInputNormalizer.TryParseDate(m.DeliveryDate, out var d)) return (null, $"mras[{i}].delivery_date must be a date YYYY-MM-DD");
                    delivery = d.ToDateTime(TimeOnly.MinValue);
                }
                static string? T(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
                lines.Add(new MraLine
                {
                    MraNo = m.MraNo!.Trim(), Qty = m.Qty.Value, Status = status, GenericArticle = T(m.GenericArticle) ?? "", Colour = T(m.Colour) ?? "",
                    ChangedAt = changed, RequestId = T(m.RequestId), GenericArtDesc = T(m.GenericArtDesc), VendorCode = T(m.VendorCode),
                    VendorName = T(m.VendorName), BrandType = T(m.BrandType), McCode = T(m.McCode), McName = T(m.McName), Mrp = m.Mrp,
                    PlanningFlag = T(m.PlanningFlag), Fit = T(m.Fit), Design = T(m.Design), Style = T(m.Style), Fabric = T(m.Fabric),
                    ProductMaterial = T(m.ProductMaterial), PoValue = m.PoValue, DeliveryDate = delivery, ApprovalLevel = T(m.ApprovalLevel),
                    Remark = T(m.Remark), Source = "SAP"
                });
            }
            return (lines, null);
        }

        public async Task<SapCallResult> ReceiveAsync(string body, SapCallContext caller, CancellationToken ct = default)
        {
            if (!_repo.Available)
                throw new SapApiException(503, SapErrorCodes.StorageNotSupported, _repo.UnavailableReason ?? "SAP interface requires SQL Server storage");
            SapMraStatusRequest? req;
            try
            {
                req = SapJson.Deserialize<SapMraStatusRequest>(body);
            }
            catch (JsonException ex)
            {
                throw new SapApiException(400, SapErrorCodes.InvalidJson, "Invalid JSON: " + ex.Message);
            }
            var max = Math.Max(1, _cfg.MaxMraLinesPerBatch);
            if (req?.Mras is { Count: var n } && n > max)
                throw new SapApiException(413, SapErrorCodes.BatchTooLarge, $"{n} MRA lines in one batch - at most {max}");
            var (lines, problem) = Validate(req, _cfg.MraStatuses);
            if (problem != null) throw new SapApiException(400, SapErrorCodes.ValidationFailed, problem);

            var batchId = "MRA-" + _clock.Now.ToString("yyyyMMdd-HHmmss") + "-" + Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(2)).ToLowerInvariant();
            // one transaction, never cancelled half-way (see SapDecisionService)
            var rows = await _repo.ApplyAsync(lines!, caller.TechUser, caller.SourceSystem, batchId, caller.TraceId, CancellationToken.None);
            var answer = new SapMraStatusResponse
            {
                BatchId = batchId, Received = lines!.Count, ReceivedAt = SapJson.Timestamp(_clock.Now),
                Results = rows.Select(r => new SapMraLineResult
                {
                    Index = r.Idx, MraNo = r.MraNo, GenericArticle = r.GenericArticle.Length == 0 ? null : r.GenericArticle,
                    Colour = r.Colour.Length == 0 ? null : r.Colour, Result = r.Result, PreviousStatus = r.OldStatus, Status = r.Status, Qty = r.Qty
                }).ToList()
            };
            answer.Created = rows.Count(r => r.Result == MraApplyResults.Created);
            answer.Updated = rows.Count(r => r.Result == MraApplyResults.Updated);
            answer.Unchanged = rows.Count(r => r.Result == MraApplyResults.Unchanged);
            answer.Stale = rows.Count(r => r.Result == MraApplyResults.Stale);
            answer.Ambiguous = rows.Count(r => r.Result == MraApplyResults.Ambiguous);
            _log.LogInformation("SAP MRA status batch {BatchId}: {Received} line(s) - created {Created}, updated {Updated}, unchanged {Unchanged}, " +
                                "stale {Stale}, ambiguous {Ambiguous} (by {TechUser})",
                batchId, answer.Received, answer.Created, answer.Updated, answer.Unchanged, answer.Stale, answer.Ambiguous, caller.TechUser);
            if (answer.Ambiguous > 0)
                _log.LogWarning("SAP MRA status batch {BatchId}: {Count} line(s) not applied - the MRA has several lines; send generic_article and colour",
                    batchId, answer.Ambiguous);
            return new SapCallResult { Status = 200, Json = SapJson.Serialize(answer) };
        }
    }

    /// <summary>Data Setup → MRA status (portal): the morning file and the views.</summary>
    public sealed class MraService : IMraService
    {
        private readonly IMraRepository _repo;
        private readonly Interfaces.V1.Ingest.IWorkbookReader _reader;
        private readonly IAppClock _clock;
        private readonly IAuditService _audit;
        private readonly SapInterfaceSettings _cfg;
        private readonly ILogger<MraService> _log;

        public MraService(IMraRepository repo, Interfaces.V1.Ingest.IWorkbookReader reader, IAppClock clock, IAuditService audit,
                          IOptions<SapInterfaceSettings> cfg, ILogger<MraService> log)
        {
            _repo = repo;
            _reader = reader;
            _clock = clock;
            _audit = audit;
            _cfg = cfg.Value;
            _log = log;
        }

        private void RequireAvailable()
        {
            if (!_repo.Available) throw new ServiceUnavailableException(_repo.UnavailableReason ?? "MRA status requires SQL Server storage");
        }

        public async Task<MraUploadResponse> UploadAsync(DTOs.Ingest.IngestUpload file, Domain.Records.AppUserContext user, CancellationToken ct = default)
        {
            RequireAvailable();
            var resp = new MraUploadResponse { FileName = file.FileName };
            DTOs.Ingest.WorkbookData wb;
            try
            {
                wb = _reader.Read(file.Content, file.FileName);
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                resp.Errors.Add("The file could not be read as Excel or CSV: " + e.Message);
                return resp;
            }
            var parsed = MraWorkbookParser.Parse(wb, _cfg.MraStatuses);
            resp.Sheet = parsed.Sheet;
            resp.SkippedEmpty = parsed.SkippedEmpty;
            foreach (var (h, f) in parsed.Columns) resp.Columns[h] = f;
            resp.Errors.AddRange(parsed.Errors);
            if (parsed.ErrorCount > parsed.Errors.Count) resp.Errors.Add($"… and {parsed.ErrorCount - parsed.Errors.Count} more");
            if (!parsed.Ok)
            {
                _log.LogWarning("MRA file {File} refused ({Count} problem(s)) - the stored MRA data is unchanged", file.FileName, parsed.ErrorCount);
                return resp;
            }
            var uploadId = "MRAUPL-" + _clock.Now.ToString("yyyyMMdd-HHmmss");
            var c = await _repo.UploadAsync(parsed.Lines, uploadId, file.FileName, user.AuditName, ct);
            resp.Ok = true;
            resp.UploadId = uploadId;
            resp.Rows = c.Rows;
            resp.Created = c.Created;
            resp.Updated = c.Updated;
            resp.Unchanged = c.Unchanged;
            resp.Ambiguous = c.Ambiguous;
            foreach (var (idx, mra) in c.AmbiguousLines.Take(50))
            {
                var row = idx >= 0 && idx < parsed.RowNumbers.Count ? parsed.RowNumbers[idx] : 0;
                resp.Warnings.Add($"{(row > 0 ? "Row " + row + ": " : "")}MRA {mra} has several lines in the tool - give Generic Art Cd and Colour; this line was skipped");
            }
            if (c.Ambiguous > resp.Warnings.Count) resp.Warnings.Add($"… and {c.Ambiguous - resp.Warnings.Count} more");
            resp.ByStatus = parsed.Lines.GroupBy(l => l.Status).OrderBy(g => g.Key)
                .Select(g => new MraStatusCount { Status = g.Key, Lines = g.Count(), Qty = g.Sum(l => l.Qty) }).ToList();
            await _audit.AppendAsync(user.AuditName, "mra-upload", $"{uploadId} {file.FileName} rows={c.Rows} created={c.Created} updated={c.Updated} unchanged={c.Unchanged} skipped={c.Ambiguous}", ct);
            return resp;
        }

        public async Task<MraSummaryResponse> SummaryAsync(CancellationToken ct = default)
        {
            var resp = new MraSummaryResponse { Statuses = _cfg.MraStatuses.ToList(), Available = _repo.Available, UnavailableReason = _repo.UnavailableReason };
            if (!_repo.Available) return resp;
            var now = _clock.Now;
            resp.Summary = await _repo.SummaryAsync(new DateTimeOffset(now.Date, now.Offset), ct);
            return resp;
        }

        public Task<MraListResponse> ListAsync(MraListReq req, CancellationToken ct = default)
        {
            RequireAvailable();
            req.Status = string.IsNullOrWhiteSpace(req.Status) ? null : MraStatuses.Normalize(req.Status, _cfg.MraStatuses) ?? req.Status.Trim().ToUpperInvariant();
            req.Search = string.IsNullOrWhiteSpace(req.Search) ? null : req.Search.Trim();
            req.Page = Math.Max(1, req.Page);
            req.PageSize = Math.Clamp(req.PageSize <= 0 ? 100 : req.PageSize, 1, 1000);
            return _repo.ListAsync(req, ct);
        }

        public async Task<MraHistoryResponse> HistoryAsync(string? mraNo, CancellationToken ct = default)
        {
            RequireAvailable();
            if (string.IsNullOrWhiteSpace(mraNo)) throw new AppValidationException("MRA No is required");
            var no = mraNo.Trim();
            return new MraHistoryResponse { MraNo = no, Items = await _repo.HistoryAsync(no, ct) };
        }
    }
}
