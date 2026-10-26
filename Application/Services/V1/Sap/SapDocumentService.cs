using System.Text.Json;
using Application.Interfaces.V1;
using Application.Interfaces.V1.Sap;
using DecisionEngine.Sap;
using Domain.Entities.Sap;
using Domain.Exceptions;
using Microsoft.Extensions.Logging;

namespace Application.Services.V1.Sap
{
    /// <summary>
    /// POST /api/sap/v1/documents: SAP posts the final document number (MRN) of a PO that was decided, against its
    /// request_id. Stored in SAP_DECISION_DOCUMENTS (one current document per request_id): the same document again answers
    /// UNCHANGED (safe to resend), another one 409 DOCUMENT_CONFLICT unless <c>replace = true</c> (the old one is kept as
    /// history). A request_id without an answered decision is 404 REQUEST_NOT_FOUND. Feeds the PO document report.
    /// </summary>
    public sealed class SapDocumentService : ISapDocumentService
    {
        private readonly ISapRepository _repo;
        private readonly IAppClock _clock;
        private readonly ILogger<SapDocumentService> _log;

        public SapDocumentService(ISapRepository repo, IAppClock clock, ILogger<SapDocumentService> log)
        {
            _repo = repo;
            _clock = clock;
            _log = log;
        }

        /// <summary>Null when valid, else the first problem.</summary>
        public static string? Validate(SapDocumentRequest? r)
        {
            if (r == null) return "The body must be a JSON object";
            if (string.IsNullOrWhiteSpace(r.RequestId)) return "request_id is required";
            if (r.RequestId.Trim().Length > 80) return "request_id must be at most 80 characters";
            if (string.IsNullOrWhiteSpace(r.DocumentNo)) return "document_no is required";
            if (r.DocumentNo.Trim().Length > 40) return "document_no must be at most 40 characters";
            if (r.DocumentType != null && r.DocumentType.Trim().Length > 20) return "document_type must be at most 20 characters";
            if (r.PoNumber != null && r.PoNumber.Trim().Length > 20) return "po_number must be at most 20 characters";
            if (!string.IsNullOrWhiteSpace(r.DocumentDate) && !SapInputNormalizer.TryParseDate(r.DocumentDate, out _))
                return "document_date must be a date YYYY-MM-DD";
            if (r.Note != null && r.Note.Length > 500) return "note must be at most 500 characters";
            return null;
        }

        public async Task<SapCallResult> ReceiveAsync(string body, SapCallContext caller, CancellationToken ct = default)
        {
            if (!_repo.Available)
                throw new SapApiException(503, SapErrorCodes.StorageNotSupported, _repo.UnavailableReason ?? "SAP interface requires SQL Server storage");
            SapDocumentRequest? req;
            try
            {
                req = SapJson.Deserialize<SapDocumentRequest>(body);
            }
            catch (JsonException ex)
            {
                throw new SapApiException(400, SapErrorCodes.InvalidJson, "Invalid JSON: " + ex.Message);
            }
            var problem = Validate(req);
            if (problem != null) throw new SapApiException(400, SapErrorCodes.ValidationFailed, problem);

            var doc = new SapDocumentInsert
            {
                RequestId = req!.RequestId!.Trim(), DocumentNo = req.DocumentNo!.Trim(),
                DocumentType = string.IsNullOrWhiteSpace(req.DocumentType) ? "MRN" : req.DocumentType.Trim().ToUpperInvariant(),
                PoNumber = string.IsNullOrWhiteSpace(req.PoNumber) ? null : req.PoNumber.Trim(),
                DocumentDate = string.IsNullOrWhiteSpace(req.DocumentDate) ? null : req.DocumentDate.Trim(), Note = req.Note, Replace = req.Replace,
                TechUser = caller.TechUser, SourceSystem = caller.SourceSystem, ClientIp = caller.ClientIp, TraceId = caller.TraceId,
                RequestJson = body.Length > 100_000 ? body[..100_000] : body
            };
            // one short transaction, never cancelled half-way (see SapDecisionService)
            var res = await _repo.SaveDocumentAsync(doc, CancellationToken.None);
            if (res.NotFound)
                throw new SapApiException(404, SapErrorCodes.RequestNotFound,
                    "No answered decision for request_id " + doc.RequestId + " - post the document with the request_id of a /decisions call that answered 200");
            if (res.Conflict)
                throw new SapApiException(409, SapErrorCodes.DocumentConflict, res.Message ?? "Another document is already stored for this request_id");

            _log.LogInformation("SAP document {Type} {DocumentNo} for request {RequestId} (decision {DecisionId}): {Result} (by {TechUser})",
                doc.DocumentType, doc.DocumentNo, doc.RequestId, res.DecisionId, res.Result, caller.TechUser);
            var answer = new SapDocumentResponse
            {
                RequestId = doc.RequestId, DecisionId = res.DecisionId, DocumentNo = doc.DocumentNo, DocumentType = doc.DocumentType!,
                Status = res.Result, ReceivedAt = SapJson.Timestamp(res.ReceivedAt.ToOffset(_clock.Now.Offset))
            };
            return new SapCallResult { Status = 200, Json = SapJson.Serialize(answer) };
        }
    }
}
