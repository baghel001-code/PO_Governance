using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using Application.Interfaces.V1;
using Application.Interfaces.V1.Sap;
using Application.Services.V1.Platform;
using DecisionEngine.Model;
using DecisionEngine.Sap;
using Domain.Entities.AppSettings;
using Domain.Entities.Sap;
using Domain.Exceptions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Application.Services.V1.Sap
{
    /// <summary>
    /// POST /api/sap/v1/decisions (spec v1.4 §6):
    /// <list type="number">
    /// <item>parse + validate (400 INVALID_JSON / VALIDATION_FAILED);</item>
    /// <item>idempotency: the same request_id with the same body (canonical JSON, SHA-256) returns the stored answer —
    /// same decision_id; a different body → 409 REQUEST_ID_CONFLICT;</item>
    /// <item>decide every item with the Tool 1 rules (<see cref="ISapDecisionEngine"/>) on the current data snapshot and
    /// today's Tool 1 ledger (read only), map to ALLOW / REDUCE / WATCH / HOLD (<see cref="SapVerdictMap"/>);
    /// 422 CANNOT_DECIDE when items cannot be decided (unknown article …), 503 DATA_NOT_LOADED without the data files;</item>
    /// <item>decision_id D-yyyyMMdd-000001 from SAP_DECISION_SEQ, data_delayed / data_as_of from the stock upload time and the
    /// event-feed lag, rules_version from SapInterface:RulesVersion;</item>
    /// <item>everything after the parse runs under SapInterface:DecisionTimeoutMs — past it the answer is 503 DECISION_TIMEOUT
    /// (SAP: NOT_CHECKED);</item>
    /// <item>every call is logged (SAP_DECISION_LOG + items; 4xx / 503 too) with its duration.</item>
    /// </list>
    /// Verdicts still use the uploaded files + the same-day Tool 1 ledger: the live open-PO / stock position from /events is
    /// a pending development (handover §8 items 6, 7, 9).
    /// </summary>
    public sealed class SapDecisionService : ISapDecisionService
    {
        private readonly ISapRepository _repo;
        private readonly IDatasetCache _cache;
        private readonly IStore _store;
        private readonly IAppClock _clock;
        private readonly ISapDecisionEngine _engine;
        private readonly ISapEventService _events;
        private readonly SapInterfaceSettings _cfg;
        private readonly ILogger<SapDecisionService> _log;

        public SapDecisionService(ISapRepository repo, IDatasetCache cache, IStore store, IAppClock clock,
                                  ISapDecisionEngine engine, ISapEventService events, IOptions<SapInterfaceSettings> cfg, ILogger<SapDecisionService> log)
        {
            _repo = repo; _cache = cache; _store = store; _clock = clock; _engine = engine; _events = events;
            _cfg = cfg.Value; _log = log;
        }

        public async Task<SapCallResult> DecideAsync(string body, SapCallContext caller, CancellationToken ct = default)
        {
            var sw = Stopwatch.StartNew();
            SapDecisionRequest? req = null;
            string? hash = null;
            try
            {
                if (!_repo.Available)
                    throw new SapApiException(503, SapErrorCodes.StorageNotSupported, _repo.UnavailableReason ?? "SAP interface requires SQL Server storage");
                try
                {
                    hash = SapJson.Sha256Hex(SapJson.Canonical(body));
                    req = SapJson.Deserialize<SapDecisionRequest>(body);
                }
                catch (JsonException ex)
                {
                    throw new SapApiException(400, SapErrorCodes.InvalidJson, "Invalid JSON: " + ex.Message);
                }
                SapRequestValidator.ApplyDefaults(req, hash, _clock.Today, caller.SourceSystem);
                var problem = SapRequestValidator.Validate(req);
                if (problem != null) throw new SapApiException(400, SapErrorCodes.ValidationFailed, problem);

                var timeout = Math.Max(100, _cfg.DecisionTimeoutMs);
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                var marks = new System.Text.StringBuilder();
                var work = DecideCoreAsync(body, req!, hash, caller, sw, marks, cts.Token);
                var finished = await Task.WhenAny(work, Task.Delay(timeout, ct));
                if (finished != work)
                {
                    string reached;
                    lock (marks) reached = marks.ToString().Trim();
                    _log.LogWarning("SAP decision request {RequestId} timed out after {Timeout} ms; steps reached: [{Phases}]", req!.RequestId, timeout, reached);
                    cts.Cancel();
                    _ = work.ContinueWith(t => _ = t.Exception, TaskScheduler.Default);   // observe a late failure
                    throw new SapApiException(503, SapErrorCodes.DecisionTimeout,
                        "No decision within " + timeout + " ms - continue as NOT_CHECKED");
                }
                var res = await work;
                res.DurationMs = (int)sw.ElapsedMilliseconds;
                _log.LogInformation("SAP decision {DecisionStatus} request {RequestId} PO {PoNumber} mode {Mode} by {TechUser} in {DurationMs} ms{Replay} [{Phases}]",
                    res.Status, req!.RequestId, req.PurchaseOrder?.PoNumber, req.Mode, caller.TechUser, res.DurationMs, res.Replayed ? " (idempotent replay)" : "",
                    res.Phases);
                return res;
            }
            catch (Exception ex) when (!IsCancelled(ex, ct))
            {
                var (status, code, message) = ex switch
                {
                    SapApiException s => (s.Status, s.ErrorCode, s.Message),
                    _ => Map(ex)
                };
                if (status >= 500) _log.LogWarning(ex is SapApiException ? null : ex, "SAP decision {Status} {Code}: {Message} (request {RequestId}, trace {TraceId})",
                                                    status, code, message, req?.RequestId, caller.TraceId);
                else _log.LogInformation("SAP decision {Status} {Code}: {Message} (request {RequestId}, trace {TraceId})", status, code, message, req?.RequestId, caller.TraceId);
                var result = new SapCallResult
                {
                    Status = status, ErrorCode = code, Json = SapJson.Error(code, message, caller.TraceId), DurationMs = (int)sw.ElapsedMilliseconds
                };
                if (code == SapErrorCodes.DecisionTimeout)
                    _ = LogErrorAsync(req, body, hash, caller, status, code, message, result.DurationMs, null);   // answer now, log behind
                else if (code != SapErrorCodes.StorageNotSupported && code != SapErrorCodes.DatabaseUnavailable && !ct.IsCancellationRequested)
                    result.LogId = await LogErrorAsync(req, body, hash, caller, status, code, message, result.DurationMs, null);
                return result;
            }
        }

        private static bool IsCancelled(Exception ex, CancellationToken ct) => ex is OperationCanceledException && ct.IsCancellationRequested;

        private static (int, string, string) Map(Exception ex) =>
            IsDbError(ex)
                ? (503, SapErrorCodes.DatabaseUnavailable, "The database is not reachable right now - continue as NOT_CHECKED")
                : (503, SapErrorCodes.InternalError, "Server error: " + ex.Message);

        private static bool IsDbError(Exception ex)
        {
            for (var x = ex; x != null; x = x.InnerException)
            {
                var n = x.GetType().FullName ?? "";
                if (n.Contains("SqlException") || n.Contains("Npgsql") || x is System.Net.Sockets.SocketException || x is TimeoutException) return true;
            }
            return false;
        }

        private async Task<SapCallResult> DecideCoreAsync(string body, SapDecisionRequest req, string hash, SapCallContext caller, Stopwatch sw,
                                                          System.Text.StringBuilder marks, CancellationToken ct)
        {
            // 1. idempotency
            void Mark(string what) { lock (marks) marks.Append(what).Append('=').Append(sw.ElapsedMilliseconds).Append(' '); }
            var existing = await _repo.GetDecisionByRequestAsync(req.RequestId!, ct);
            Mark("lookup");
            if (existing != null) return Replay(existing, hash, req.RequestId!, caller);

            // 2. data: snapshot, today's Tool 1 ledger (read only), manifest (upload times), feed status
            var today = _clock.Today;
            // not cancelled by the decision timeout: a cold load (first call after a start or an upload) runs to the end,
            // so the calls after it find the snapshot ready instead of timing out the same way again
            var data = await _cache.GetCurrentAsync(CancellationToken.None);
            // read-only snapshot of today's Tool 1 requests: no server-wide write lock (that would serialise parallel SAP calls);
            // SQL Server READ_COMMITTED_SNAPSHOT gives a consistent read. SAP decisions are never written into this ledger.
            var (ledger, _) = await _store.ReadRequestsAsync(today, false, ct);
            var manifest = await _store.GetManifestAsync(ct);
            var feed = await _events.StatusAsync(ct);
            Mark("data");

            // 3. decide (CPU: off the request thread so the timeout can answer)
            var items = req.PurchaseOrder!.Items!.Select(i => new SapItemInput
            {
                PoItem = i.PoItem!.Value, Article = i.Article ?? "", GenericArticle = i.GenericArticle, Colour = i.Colour, OrderQty = i.OrderQty!.Value,
                Uom = i.Uom, DeliveryDate = i.DeliveryDate, NetValue = i.NetValue
            }).ToList();
            var options = Options();
            var result = await Task.Run(() => _engine.Decide(data, ledger, today, items, options), ct);
            ct.ThrowIfCancellationRequested();
            Mark("decide");

            if (result.MissingFiles.Count > 0)
                throw new SapApiException(503, SapErrorCodes.DataNotLoaded,
                    "The tool's data files are not loaded (" + string.Join(", ", result.MissingFiles.Select(k => DatasetKeys.Labels.GetValueOrDefault(k, k))) +
                    ") - continue as NOT_CHECKED");
            if (result.Rejected)
            {
                var msg = result.RejectMessage ?? "The request cannot be decided";
                var logId = await LogErrorAsync(req, body, hash, caller, 422, SapErrorCodes.CannotDecide, msg, (int)sw.ElapsedMilliseconds, result);
                return new SapCallResult { Status = 422, ErrorCode = SapErrorCodes.CannotDecide, Json = SapJson.Error(SapErrorCodes.CannotDecide, msg, caller.TraceId), LogId = logId };
            }

            // 4. the answer
            var now = _clock.Now;
            var seq = await _repo.NextDecisionSeqAsync(ct);
            var decisionId = "D-" + now.ToString("yyyyMMdd", CultureInfo.InvariantCulture) + "-" + seq.ToString("D6", CultureInfo.InvariantCulture);
            var lag = Math.Max(0, feed.LagSeconds);
            var response = new SapDecisionResponse
            {
                DecisionId = decisionId,
                RequestId = req.RequestId!,
                Mode = req.Mode!,
                OverallVerdict = result.OverallVerdict,
                VerdictCode = result.VerdictCode,
                RouteTo = result.RouteTo,
                BmHeadRequired = result.BmHeadRequired,
                SummaryText = result.SummaryText,
                RulesVersion = RulesVersion(data),
                DataDelayed = lag > _cfg.EventsLagLimitSeconds,
                DataAsOf = new SapDataAsOf { BwSnapshot = BwSnapshot(manifest), EventsLagSeconds = lag },
                DecidedAt = SapJson.Timestamp(now),
                Items = result.Items.Select(i => new SapDecisionResponseItem
                {
                    PoItem = i.PoItem, Verdict = i.Verdict, VerdictCode = i.VerdictCode, MaxQty = i.Verdict == SapVerdicts.Reduce ? i.MaxQty : null,
                    ReasonCode = i.ReasonCode, ReasonText = i.ReasonText, RuleId = i.RuleId,
                    Metrics = new SapDecisionMetrics
                    {
                        PlanQty = i.Metrics.PlanQty, StockQty = i.Metrics.StockQty, OpenPoQty = i.Metrics.OpenPoQty,
                        HeadroomQty = i.Metrics.HeadroomQty, CoverDays = i.Metrics.CoverDays, SellThroughPct = i.Metrics.SellThroughPct
                    }
                }).ToList()
            };
            var json = SapJson.Serialize(response);

            // 5. log (unique per request_id: a concurrent identical call may have stored its answer first)
            var row = LogRow(req, body, hash, caller, 200, null, null, (int)sw.ElapsedMilliseconds);
            row.DecisionId = decisionId;
            row.OverallVerdict = response.OverallVerdict;
            row.VerdictCode = response.VerdictCode;
            row.RouteTo = response.RouteTo;
            row.BmHeadRequired = response.BmHeadRequired;
            row.DataDelayed = response.DataDelayed;
            row.EventsLagSeconds = lag;
            row.RulesVersion = response.RulesVersion;
            row.DecidedAt = now;
            row.ResponseJson = json;
            try
            {
                Mark("answer");
                // last point to give up; the write itself is never cancelled half-way (a cancelled command can leave its
                // transaction - and its locks - open on the pooled connection until the connection is reused)
                ct.ThrowIfCancellationRequested();
                var id = await _repo.SaveDecisionAsync(row, ItemRows(result), CancellationToken.None);
                Mark("saved");
                return new SapCallResult { Status = 200, Json = json, LogId = id, Phases = marks.ToString().Trim() };
            }
            catch (SapRequestIdTakenException)
            {
                var stored = await _repo.GetDecisionByRequestAsync(req.RequestId!, ct);
                if (stored == null) throw;
                return Replay(stored, hash, req.RequestId!, caller);
            }
        }

        private long _warmData = -1, _warmParams = -1;

        public async Task<long?> WarmUpAsync(CancellationToken ct = default)
        {
            var sw = Stopwatch.StartNew();
            var data = await _cache.GetCurrentAsync(ct);
            if (data.DataVersion == _warmData && data.ParamsVersion == _warmParams) return null;
            var row = data.F1?.FirstOrDefault(r => r.Col("Generic Material", "GENERIC MATERIAL").Trim().Length > 0);
            if (row != null)
            {
                var today = _clock.Today;
                var (ledger, _) = await _store.ReadRequestsAsync(today, false, ct);
                var now = _clock.Now;
                var item = new SapItemInput
                {
                    PoItem = 10, Article = "", GenericArticle = row.Col("Generic Material", "GENERIC MATERIAL").Trim(),
                    Colour = row.Col("Color", "Colour", "COLOR", "COLOUR").Trim(), OrderQty = 1,
                    DeliveryDate = new DateTime(now.Year, now.Month, 1).AddMonths(1).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
                };
                await Task.Run(() => _engine.Decide(data, ledger, today, new[] { item }, Options()), ct);
            }
            _warmData = data.DataVersion;
            _warmParams = data.ParamsVersion;
            return sw.ElapsedMilliseconds;
        }

        private static SapCallResult Replay(SapDecisionLogRow existing, string hash, string requestId, SapCallContext caller)
        {
            if (string.Equals(existing.BodyHash, hash, StringComparison.OrdinalIgnoreCase) && existing.ResponseJson != null)
                return new SapCallResult { Status = 200, Json = existing.ResponseJson, LogId = existing.Id, Replayed = true };
            throw new SapApiException(409, SapErrorCodes.RequestIdConflict,
                "request_id " + requestId + " was already used with a different body (decision " + existing.DecisionId + ") - generate a new request_id");
        }

        private SapDecisionOptions Options() => new()
        {
            BlockVerdict = _cfg.BlockVerdict,
            UnknownItemPolicy = _cfg.UnknownItemPolicy,
            BmHeadRule = new SapBmHeadRule { Enabled = _cfg.BmHead.Enabled, MinNetValue = _cfg.BmHead.MinNetValue, IncludeWatch = _cfg.BmHead.IncludeWatch }
        };

        private string RulesVersion(EngineData data) =>
            string.IsNullOrWhiteSpace(_cfg.RulesVersion) ? "R-P" + data.ParamsVersion.ToString(CultureInfo.InvariantCulture) : _cfg.RulesVersion.Trim();

        /// <summary>The upload time of the stock file (F1) — the base of the day until the BW 04:00 feed is built.</summary>
        public static string? BwSnapshot(Domain.Entities.Store.Manifest m)
        {
            if (!m.Datasets.TryGetValue(DatasetKeys.F1, out var e) || e == null) return null;
            var at = !string.IsNullOrEmpty(e.RestoredAt) && string.CompareOrdinal(e.RestoredAt, e.UpdatedAt ?? "") > 0 ? e.RestoredAt : e.UpdatedAt;
            if (string.IsNullOrWhiteSpace(at)) return null;
            return DateTimeOffset.TryParse(at, CultureInfo.InvariantCulture, DateTimeStyles.None, out var t) ? SapJson.Timestamp(t) : at;
        }

        private SapDecisionLogRow LogRow(SapDecisionRequest? req, string body, string? hash, SapCallContext caller, int status, string? code, string? message, int ms)
        {
            DateTimeOffset? requestedAt = SapJson.TryParseTimestamp(req?.RequestedAt, out var ra) ? ra : null;
            return new SapDecisionLogRow
            {
                RequestId = Clip(req?.RequestId, 80) ?? "",
                BodyHash = hash,
                Mode = Clip(req?.Mode, 10),
                SourceSystem = Clip(req?.SourceSystem, 10),
                Client = Clip(req?.Client, 10),
                PoNumber = Clip(req?.PurchaseOrder?.PoNumber, 20),
                BuyerUser = Clip(req?.PurchaseOrder?.BuyerUser, 64),
                Division = Clip(req?.PurchaseOrder?.Division, 40),
                HttpStatus = status,
                ErrorCode = code,
                ErrorMessage = Clip(message, 500),
                ItemCount = req?.PurchaseOrder?.Items?.Count ?? 0,
                DurationMs = ms,
                TechUser = caller.TechUser,
                ClientIp = caller.ClientIp,
                IsTest = caller.IsTest,
                TraceId = caller.TraceId,
                RequestedAt = requestedAt,
                DecidedAt = _clock.Now,
                RequestJson = body.Length > 1_000_000 ? body[..1_000_000] : body
            };
        }

        private async Task<long?> LogErrorAsync(SapDecisionRequest? req, string body, string? hash, SapCallContext caller, int status, string code,
                                                string message, int ms, SapDecisionResult? result)
        {
            try
            {
                var row = LogRow(req, body, hash, caller, status, code, message, ms);
                return await _repo.SaveDecisionAsync(row, result == null ? Array.Empty<SapDecisionItemRow>() : ItemRows(result), CancellationToken.None);
            }
            catch (Exception ex)
            {
                _log.LogWarning("Could not log the SAP decision answer {Status} {Code}: {Error}", status, code, ex.Message);
                return null;
            }
        }

        private static List<SapDecisionItemRow> ItemRows(SapDecisionResult r) => r.Items.Select(i => new SapDecisionItemRow
        {
            PoItem = i.PoItem, Article = Clip(i.Article, 40), GenericArticle = Clip(i.GenericArticle, 40), Gm = Clip(i.Gm, 40), GmMatch = i.GmMatch,
            ColourSent = Clip(i.ColourSent, 60), Colour = Clip(i.Colour, 60), ColourMatch = i.ColourMatch, DeliveryMonth = i.Month,
            OrderQty = i.OrderQty, GroupQty = i.GroupQty,
            Verdict = i.Decided || i.Path != null ? i.Verdict : null, VerdictCode = i.Decided || i.Path != null ? i.VerdictCode : null,
            MaxQty = i.Verdict == SapVerdicts.Reduce ? i.MaxQty : null, ReasonCode = i.ReasonCode.Length > 0 ? i.ReasonCode : null,
            RuleId = i.RuleId.Length > 0 ? i.RuleId : null, ReasonText = i.ReasonText.Length > 0 ? i.ReasonText : i.Problem,
            Engine = i.Engine.Length > 0 ? i.Engine : null, EngineDecision = i.EngineDecision.Length > 0 ? i.EngineDecision : null,
            EngineReason = i.EngineReason.Length > 0 ? i.EngineReason : i.Problem,
            MetricsJson = SapJson.Serialize(new SapDecisionMetrics
            {
                PlanQty = i.Metrics.PlanQty, StockQty = i.Metrics.StockQty, OpenPoQty = i.Metrics.OpenPoQty, HeadroomQty = i.Metrics.HeadroomQty,
                CoverDays = i.Metrics.CoverDays, SellThroughPct = i.Metrics.SellThroughPct
            })
        }).ToList();

        private static string? Clip(string? s, int max) => s == null ? null : s.Length <= max ? s : s[..max];
    }
}
