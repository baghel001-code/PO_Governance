using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Application.Interfaces.V1;
using Application.Interfaces.V1.Sap;
using Domain.Entities.AppSettings;
using Domain.Entities.Sap;
using Domain.Exceptions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Application.Services.V1.Sap
{
    /// <summary>
    /// POST /api/sap/v1/events (spec v1.4 §5). The envelope is checked (400 for a malformed batch, 413 above
    /// SapInterface:MaxEventsPerBatch events); then every event is validated on its own — a bad event is <b>parked</b>
    /// (stored with its error, listed in <c>parked</c>, logged as a warning for IT) and never blocks the queue. The batch is
    /// stored in one transaction (PRO_SAP_EVENTS_SAVE) and the answer (200) is sent only after the commit:
    /// <c>accepted</c> / <c>duplicates</c> (msg_id already stored — nothing counted twice) / <c>parked</c>. Events with a
    /// seq not above the last accepted seq of the queue are stored and flagged LATE.
    /// <para>
    /// Pending (handover §8 items 6, 7, 9): applying the events to a live open-PO / stock position. Until those tables
    /// exist every event is stored with Applied = 0 and the verdicts use the uploaded files + the same-day Tool 1 ledger.
    /// </para>
    /// </summary>
    public sealed class SapEventService : ISapEventService
    {
        private readonly ISapRepository _repo;
        private readonly IAppClock _clock;
        private readonly SapInterfaceSettings _cfg;
        private readonly ILogger<SapEventService> _log;
        private readonly SemaphoreSlim _statusGate = new(1, 1);
        private SapEventStatus? _status;
        private DateTime _statusAt;
        private static readonly TimeSpan StatusTtl = TimeSpan.FromSeconds(2);

        public SapEventService(ISapRepository repo, IAppClock clock, IOptions<SapInterfaceSettings> cfg, ILogger<SapEventService> log)
        {
            _repo = repo; _clock = clock; _cfg = cfg.Value; _log = log;
        }

        private int TzOffset => (int)_clock.Now.Offset.TotalMinutes;

        public async Task<SapEventStatus> StatusAsync(CancellationToken ct = default)
        {
            var s = _status;
            if (s != null && DateTime.UtcNow - _statusAt < StatusTtl) return s;
            await _statusGate.WaitAsync(ct);
            try
            {
                if (_status != null && DateTime.UtcNow - _statusAt < StatusTtl) return _status;
                _status = await _repo.EventStatusAsync(_clock.Today, TzOffset, ct);
                _statusAt = DateTime.UtcNow;
                return _status;
            }
            finally { _statusGate.Release(); }
        }

        public async Task<SapCallResult> ReceiveAsync(string body, SapCallContext caller, CancellationToken ct = default)
        {
            if (!_repo.Available)
                throw new SapApiException(503, SapErrorCodes.StorageNotSupported, _repo.UnavailableReason ?? "SAP interface requires SQL Server storage");
            if (System.Text.Encoding.UTF8.GetByteCount(body) > _cfg.MaxBodyBytes)
                throw new SapApiException(413, SapErrorCodes.BatchTooLarge, "Batch larger than " + _cfg.MaxBodyBytes / (1024 * 1024) + " MB - split and resend");

            SapEventBatch? batch;
            try
            {
                batch = SapJson.Deserialize<SapEventBatch>(body);
            }
            catch (JsonException ex)
            {
                throw new SapApiException(400, SapErrorCodes.InvalidJson, "Invalid JSON: " + ex.Message);
            }
            if (batch == null) throw new SapApiException(400, SapErrorCodes.ValidationFailed, "The body must be a JSON object");
            if (string.IsNullOrWhiteSpace(batch.SourceSystem)) throw new SapApiException(400, SapErrorCodes.ValidationFailed, "source_system is required");
            if (string.IsNullOrWhiteSpace(batch.Client)) throw new SapApiException(400, SapErrorCodes.ValidationFailed, "client is required");
            if (batch.Events == null) throw new SapApiException(400, SapErrorCodes.ValidationFailed, "events is required");
            if (batch.Events.Count == 0) throw new SapApiException(400, SapErrorCodes.ValidationFailed, "events must contain at least one event");
            if (batch.Events.Count > _cfg.MaxEventsPerBatch)
                throw new SapApiException(413, SapErrorCodes.BatchTooLarge, "Batch of " + batch.Events.Count + " events is larger than " + _cfg.MaxEventsPerBatch + " - split and resend");
            if (batch.SentAt != null && !SapJson.TryParseTimestamp(batch.SentAt, out _))
                throw new SapApiException(400, SapErrorCodes.ValidationFailed, "sent_at must be an ISO 8601 timestamp with offset");

            var batchId = "B-" + _clock.Now.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture) + "-" + Guid.NewGuid().ToString("N")[..8];
            var inserts = new List<SapEventInsert>();
            for (var i = 0; i < batch.Events.Count; i++)
                inserts.Add(Normalise(batch.Events[i], i, batchId));

            var results = await _repo.SaveEventsAsync(inserts, Clip(batch.Queue, 64), Clip(batch.SourceSystem, 10), Clip(batch.Client, 10), caller.TechUser, batchId,
                CancellationToken.None);   // one transaction, never cancelled half-way (see SapDecisionService)
            _status = null;   // the feed status changed

            var answer = new SapEventBatchResult { ReceivedAt = SapJson.Timestamp(_clock.Now) };
            for (var i = 0; i < results.Count; i++)
            {
                var r = results[i];
                switch (r.Result)
                {
                    case "ACCEPTED": answer.Accepted.Add(r.MsgId); break;
                    case "DUPLICATE": answer.Duplicates.Add(r.MsgId); break;
                    default:
                        var ins = inserts[i];
                        answer.Parked.Add(new SapParkedEvent { MsgId = r.MsgId, ErrorCode = ins.ErrorCode ?? "INVALID_EVENT", Message = ins.ErrorMessage ?? "" });
                        _log.LogWarning("SAP event PARKED {MsgId} ({EventType}): {Error} - batch {BatchId}, queue {Queue}", r.MsgId, ins.EventType, ins.ErrorMessage, batchId, batch.Queue);
                        break;
                }
            }
            _log.LogInformation("SAP events batch {BatchId} from {Source}/{Client} queue {Queue}: {Accepted} accepted, {Duplicates} duplicate(s), {Parked} parked (by {TechUser})",
                batchId, batch.SourceSystem, batch.Client, batch.Queue, answer.Accepted.Count, answer.Duplicates.Count, answer.Parked.Count, caller.TechUser);
            return new SapCallResult { Status = 200, Json = SapJson.Serialize(answer) };
        }

        /// <summary>One event → the row to store (ACCEPTED, or PARKED with the first validation problem).</summary>
        public static SapEventInsert Normalise(JsonObject? node, int index, string batchId)
        {
            var payload = node?.ToJsonString() ?? "null";
            var ins = new SapEventInsert { PayloadJson = node == null ? "{}" : payload };
            SapEvent? e = null;
            string? problem;
            if (node == null) problem = "The event must be a JSON object";
            else
            {
                try
                {
                    e = node.Deserialize<SapEvent>(SapJson.Options);
                    problem = e == null ? "The event must be a JSON object" : SapRequestValidator.Validate(e);
                }
                catch (JsonException ex)
                {
                    problem = "Invalid field value: " + ex.Message;
                    // keep what can be read for the monitoring screen
                    e = new SapEvent
                    {
                        MsgId = (node["msg_id"] as JsonValue)?.TryGetValue<string>(out var m) == true ? m : null,
                        EventType = (node["event_type"] as JsonValue)?.TryGetValue<string>(out var t) == true ? t : null
                    };
                }
            }
            var msgId = e?.MsgId?.Trim();
            ins.MsgId = string.IsNullOrEmpty(msgId) ? "NOID-" + batchId + "-" + (index + 1).ToString(CultureInfo.InvariantCulture)
                      : msgId.Length > 100 ? msgId[..100] : msgId;
            ins.Seq = e?.Seq;
            ins.EventType = e?.EventType is { Length: <= 30 } et ? et : e?.EventType?[..30];
            ins.OccurredAt = SapJson.TryParseTimestamp(e?.OccurredAt, out var occ) ? occ.ToString("yyyy-MM-dd'T'HH:mm:ss.fffffffzzz", CultureInfo.InvariantCulture) : null;
            ins.SapUser = e?.SapUser;
            ins.PoNumber = e?.PurchaseOrder?.PoNumber ?? e?.GoodsMovement?.PoNumber ?? e?.GovernanceOutcome?.PoNumber;
            ins.PoItem = e?.GoodsMovement?.PoItem ?? e?.GovernanceOutcome?.PoItem;
            ins.MaterialDoc = e?.GoodsMovement?.MaterialDoc;
            ins.DecisionId = e?.GovernanceOutcome?.DecisionId;
            if (problem != null)
            {
                ins.Status = "PARKED";
                ins.ErrorCode = SapErrorCodes.ValidationFailed;
                ins.ErrorMessage = problem.Length > 500 ? problem[..500] : problem;
            }
            return ins;
        }

        private static string? Clip(string? s, int max) => s == null ? null : s.Length <= max ? s : s[..max];
    }
}
