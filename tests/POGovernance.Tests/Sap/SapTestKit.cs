using System.Text.Json;
using System.Text.Json.Nodes;
using Application.Interfaces.V1.Sap;
using Application.Services.V1.Sap;
using DecisionEngine.Model;
using Domain.Entities.AppSettings;
using Domain.Entities.Sap;
using Domain.Entities.Store;
using Domain.Exceptions;
using Microsoft.Extensions.Options;
using POGovernance.Tests.Platform;
using POGovernance.Tests.Tool1;

namespace POGovernance.Tests.Sap
{
    /// <summary>In-memory <see cref="ISapRepository"/> with the PRO_SAP_* semantics the services rely on.</summary>
    public sealed class InMemorySapRepository : ISapRepository
    {
        public bool IsAvailable = true;
        public bool Available => IsAvailable;
        public string? UnavailableReason => IsAvailable ? null : "SAP interface requires SQL Server storage";

        public readonly List<SapTechUser> Users = new();
        public readonly List<(SapDecisionLogRow Row, List<SapDecisionItemRow> Items)> Decisions = new();
        public readonly List<SapEventRow> Events = new();
        public readonly List<SapReasonCodeRow> Codes = new()
        {
            new() { CodeType = "EXCEPTION", Code = "NEWL", Text = "New launch - no sales history", SortOrder = 10 },
            new() { CodeType = "EXCEPTION", Code = "OTHR", Text = "Other (note mandatory)", SortOrder = 60 },
            new() { CodeType = "EXCEPTION", Code = "OLD", Text = "Inactive", Active = false, SortOrder = 70 },
        };
        public SapEventStatus Status = new();
        public long Seq;
        public int LoginOkCalls;
        private readonly object _gate = new();

        private SapTechUser User(string name) => Users.FirstOrDefault(u => string.Equals(u.UserName, name, StringComparison.OrdinalIgnoreCase))
                                                 ?? throw new SapStoreException("PRO_SAP_TECH_USER", 50404, "Unknown technical user.");

        public Task<SapTechUser?> GetTechUserAsync(string userName, CancellationToken ct = default) =>
            Task.FromResult(Users.FirstOrDefault(u => string.Equals(u.UserName, userName, StringComparison.OrdinalIgnoreCase)));
        public Task<List<SapTechUser>> ListTechUsersAsync(CancellationToken ct = default) => Task.FromResult(Users.ToList());
        public Task<SapTechUser> AddTechUserAsync(string userName, string sourceSystem, string? description, string passwordHash, string by, CancellationToken ct = default)
        {
            if (Users.Any(u => string.Equals(u.UserName, userName, StringComparison.OrdinalIgnoreCase)))
                throw new SapStoreException("PRO_SAP_TECH_USER", 50409, "exists");
            var u = new SapTechUser { Id = Users.Count + 1, UserName = userName, SourceSystem = sourceSystem, Description = description, PasswordHash = passwordHash, Active = true, CreatedBy = by };
            Users.Add(u);
            return Task.FromResult(u);
        }
        public Task<SapTechUser> SetTechUserPasswordAsync(string userName, string passwordHash, string by, CancellationToken ct = default)
        {
            var u = User(userName); u.PasswordHash = passwordHash; u.FailedCount = 0; u.LockedUntil = null; return Task.FromResult(u);
        }
        public Task<SapTechUser> SetTechUserActiveAsync(string userName, bool active, string by, CancellationToken ct = default)
        {
            var u = User(userName); u.Active = active; if (active) { u.FailedCount = 0; u.LockedUntil = null; } return Task.FromResult(u);
        }
        public Task<SapTechUser> UnlockTechUserAsync(string userName, CancellationToken ct = default)
        {
            var u = User(userName); u.FailedCount = 0; u.LockedUntil = null; return Task.FromResult(u);
        }
        public Task LoginOkAsync(string userName, string ip, CancellationToken ct = default)
        {
            LoginOkCalls++;
            var u = User(userName); u.FailedCount = 0; u.LockedUntil = null; u.LastCallIp = ip; u.LastCallAt = DateTimeOffset.Now; return Task.CompletedTask;
        }
        public Task<SapTechUser?> LoginFailAsync(string userName, int maxFailed, int lockoutMinutes, CancellationToken ct = default)
        {
            var u = Users.FirstOrDefault(x => x.UserName == userName);
            if (u != null)
            {
                u.FailedCount++;
                if (u.FailedCount >= maxFailed) u.LockedUntil = Clock.Now.AddMinutes(lockoutMinutes);
            }
            return Task.FromResult(u);
        }
        public FakeAppClock Clock = new() { TodayValue = "2026-10-21" };

        public Task<SapDecisionLogRow?> GetDecisionByRequestAsync(string requestId, CancellationToken ct = default)
        {
            lock (_gate) return Task.FromResult(Decisions.Select(d => d.Row).FirstOrDefault(r => r.RequestId == requestId && r.HttpStatus == 200));
        }
        public Task<long> NextDecisionSeqAsync(CancellationToken ct = default) => Task.FromResult(Interlocked.Increment(ref Seq));
        public Task<long> SaveDecisionAsync(SapDecisionLogRow row, IReadOnlyList<SapDecisionItemRow> items, CancellationToken ct = default)
        {
            lock (_gate)
            {
                if (row.HttpStatus == 200 && Decisions.Any(d => d.Row.RequestId == row.RequestId && d.Row.HttpStatus == 200))
                    throw new SapRequestIdTakenException(row.RequestId);
                row.Id = Decisions.Count + 1;
                Decisions.Add((row, items.ToList()));
                return Task.FromResult(row.Id);
            }
        }
        public Task<SapDecisionListResponse> ListDecisionsAsync(SapDecisionListReq req, int tzOffsetMinutes, CancellationToken ct = default) =>
            Task.FromResult(new SapDecisionListResponse { Items = Decisions.Select(d => d.Row).Reverse().ToList(), Total = Decisions.Count });
        public Task<SapDecisionDetailResponse> GetDecisionAsync(long id, CancellationToken ct = default)
        {
            var d = Decisions.FirstOrDefault(x => x.Row.Id == id);
            return Task.FromResult(new SapDecisionDetailResponse { Decision = d.Row, Items = d.Items ?? new() });
        }
        public Task<SapDecisionStats> DecisionStatsAsync(string today, int tzOffsetMinutes, CancellationToken ct = default) =>
            Task.FromResult(new SapDecisionStats { Total = Decisions.Count });

        public readonly List<SapDocumentRow> Documents = new();

        public Task<bool> SetViewTokenAsync(long logId, string token, DateTimeOffset expiresAt, string responseJson, CancellationToken ct = default)
        {
            lock (_gate)
            {
                var row = Decisions.Select(d => d.Row).First(r => r.Id == logId);
                if (row.ViewToken != null) return Task.FromResult(false);
                row.ViewToken = token; row.ViewExpiresAt = expiresAt; row.ResponseJson = responseJson;
                return Task.FromResult(true);
            }
        }

        public Task<SapDecisionDetailResponse?> GetDecisionByViewTokenAsync(string token, CancellationToken ct = default)
        {
            var d = Decisions.FirstOrDefault(x => x.Row.ViewToken == token);
            return Task.FromResult(d.Row == null ? null : new SapDecisionDetailResponse
            {
                Decision = d.Row, Items = d.Items, Documents = Documents.Where(x => x.DecisionLogId == d.Row.Id).OrderByDescending(x => x.IsCurrent).ToList()
            });
        }

        public Task<SapDocumentSaveResult> SaveDocumentAsync(SapDocumentInsert doc, CancellationToken ct = default)
        {
            lock (_gate)
            {
                var log = Decisions.Select(x => x.Row).FirstOrDefault(r => r.RequestId == doc.RequestId && r.HttpStatus == 200);
                if (log == null) return Task.FromResult(new SapDocumentSaveResult { NotFound = true, Message = "No answered decision" });
                var cur = Documents.FirstOrDefault(x => x.RequestId == doc.RequestId && x.IsCurrent);
                if (cur != null && cur.DocumentNo == doc.DocumentNo)
                    return Task.FromResult(new SapDocumentSaveResult { Result = "UNCHANGED", DecisionId = log.DecisionId, ReceivedAt = cur.ReceivedAt });
                if (cur != null && !doc.Replace)
                    return Task.FromResult(new SapDocumentSaveResult { Conflict = true, CurrentDocumentNo = cur.DocumentNo, DecisionId = log.DecisionId,
                                                                       Message = "request_id " + doc.RequestId + " already has document " + cur.DocumentNo });
                var now = Clock.Now;
                if (cur != null) { cur.IsCurrent = false; cur.ReplacedAt = now; }
                Documents.Add(new SapDocumentRow
                {
                    Id = Documents.Count + 1, DecisionLogId = log.Id, RequestId = doc.RequestId, DecisionId = log.DecisionId, DocumentNo = doc.DocumentNo,
                    DocumentType = doc.DocumentType ?? "MRN", PoNumber = doc.PoNumber ?? log.PoNumber, DocumentDate = doc.DocumentDate, Note = doc.Note,
                    IsCurrent = true, TechUser = doc.TechUser, SourceSystem = doc.SourceSystem, ReceivedAt = now
                });
                return Task.FromResult(new SapDocumentSaveResult { Result = cur == null ? "STORED" : "REPLACED", DecisionId = log.DecisionId, ReceivedAt = now });
            }
        }

        public Task<SapDocumentReportResponse> DocumentReportAsync(SapDocumentReportReq req, int tzOffsetMinutes, CancellationToken ct = default)
        {
            var rows = Decisions.Where(d => d.Row.HttpStatus == 200 && (req.IncludeTests || !d.Row.IsTest)).Select(d =>
            {
                var doc = Documents.FirstOrDefault(x => x.DecisionLogId == d.Row.Id && x.IsCurrent);
                return new SapDocumentReportRow
                {
                    Id = d.Row.Id, DecisionId = d.Row.DecisionId, RequestId = d.Row.RequestId, PoNumber = d.Row.PoNumber, OverallVerdict = d.Row.OverallVerdict,
                    ItemCount = d.Row.ItemCount, OrderQty = d.Items.Sum(i => i.OrderQty), DecidedAt = d.Row.DecidedAt, Mode = d.Row.Mode,
                    DocumentNo = doc?.DocumentNo, DocumentType = doc?.DocumentType, DocumentDate = doc?.DocumentDate, DocumentReceivedAt = doc?.ReceivedAt
                };
            }).Where(r => req.HasDocument switch { "yes" => r.DocumentNo != null, "no" => r.DocumentNo == null, _ => true }).Reverse().ToList();
            return Task.FromResult(new SapDocumentReportResponse { Items = rows, Total = rows.Count });
        }

        public Task<List<SapEventSaveResult>> SaveEventsAsync(IReadOnlyList<SapEventInsert> events, string? queue, string? sourceSystem, string? client,
                                                              string? techUser, string batchId, CancellationToken ct = default)
        {
            var res = new List<SapEventSaveResult>();
            var last = Events.Where(e => e.Queue == queue && e.Status == "ACCEPTED").Select(e => e.Seq).Max();
            foreach (var e in events)
            {
                var existing = Events.FirstOrDefault(x => x.MsgId == e.MsgId);
                if (existing != null) { existing.DupCount++; res.Add(new SapEventSaveResult { MsgId = e.MsgId, Result = "DUPLICATE" }); continue; }
                var late = e.Status == "ACCEPTED" && last != null && e.Seq <= last;
                Events.Add(new SapEventRow
                {
                    Id = Events.Count + 1, MsgId = e.MsgId, Seq = e.Seq, Queue = queue, EventType = e.EventType, Status = e.Status, ErrorCode = e.ErrorCode,
                    ErrorMessage = e.ErrorMessage, SeqStatus = late ? "LATE" : "IN_ORDER", PayloadJson = e.PayloadJson, PoNumber = e.PoNumber,
                    DecisionId = e.DecisionId, BatchId = batchId, TechUser = techUser
                });
                if (e.Status == "ACCEPTED" && e.Seq != null && (last == null || e.Seq > last)) last = e.Seq;
                res.Add(new SapEventSaveResult { MsgId = e.MsgId, Result = e.Status });
            }
            return Task.FromResult(res);
        }
        public Task<SapEventStatus> EventStatusAsync(string today, int tzOffsetMinutes, CancellationToken ct = default) => Task.FromResult(Status);
        public Task<SapEventListResponse> ListEventsAsync(SapEventListReq req, int tzOffsetMinutes, CancellationToken ct = default) =>
            Task.FromResult(new SapEventListResponse { Items = Events.ToList(), Total = Events.Count });
        public Task<SapEventRow?> GetEventAsync(long id, CancellationToken ct = default) => Task.FromResult(Events.FirstOrDefault(e => e.Id == id));

        public Task<(List<SapReasonCodeRow> Codes, string Version)> ActiveExceptionCodesAsync(int tzOffsetMinutes, CancellationToken ct = default) =>
            Task.FromResult((Codes.Where(c => c.CodeType == "EXCEPTION" && c.Active).OrderBy(c => c.SortOrder).ToList(), "2026-10-01"));
        public Task<List<SapReasonCodeRow>> ListReasonCodesAsync(string? codeType, CancellationToken ct = default) =>
            Task.FromResult(Codes.Where(c => codeType == null || c.CodeType == codeType).ToList());
        public Task<SapReasonCodeRow> SaveReasonCodeAsync(SapReasonCodeSaveReq req, string by, CancellationToken ct = default)
        {
            var row = Codes.FirstOrDefault(c => c.CodeType == "EXCEPTION" && c.Code == req.Code);
            if (row == null)
                Codes.Add(row = new SapReasonCodeRow
                {
                    CodeType = "EXCEPTION", Code = req.Code!,
                    SortOrder = req.SortOrder > 0 ? req.SortOrder : Codes.Where(c => c.CodeType == "EXCEPTION").Max(c => c.SortOrder) + 10   // as PRO_SAP_REASON_CODES SAVE
                });
            row.Text = req.Text!; row.Active = req.Active; row.UpdatedBy = by;
            return Task.FromResult(row);
        }
    }

    /// <summary>Feed status for the decision service.</summary>
    public sealed class FakeEventStatus : ISapEventService
    {
        public SapEventStatus Status = new();
        public Task<SapCallResult> ReceiveAsync(string body, SapCallContext caller, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<SapEventStatus> StatusAsync(CancellationToken ct = default) => Task.FromResult(Status);
    }

    public static class SapKit
    {
        public const string Today = "2026-10-21";

        /// <summary>The Tool 1 parity fixture (all data files, seasons AW26 / SS26).</summary>
        public static EngineData Data => Tool1Fixture.Data(Tool1Fixture.Expected["scenarios"]![0]!["name"]!.GetValue<string>());

        public static SapCallContext Caller => new("sap_ecq", "ECQ", "10.10.20.15", "T-000001");

        public static IOptions<SapInterfaceSettings> Options(Action<SapInterfaceSettings>? change = null)
        {
            var s = new SapInterfaceSettings { EnforceIpAllowList = true, AllowedIps = new() { "127.0.0.1" }, DecisionTimeoutMs = 30000 };   // tests run in parallel: no accidental timeouts
            change?.Invoke(s);
            return Microsoft.Extensions.Options.Options.Create(s);
        }

        /// <summary>A FakeStore holding a manifest with the stock file's upload time (data_as_of.bw_snapshot).</summary>
        public static FakeStore Store()
        {
            var st = new FakeStore();
            var m = new Manifest { DataVersion = 1, ParamsVersion = 1 };
            m.Datasets["f1"] = new DatasetEntry { Version = "1", Rows = 60, UpdatedAt = "2026-10-21T04:15:00+05:30" };
            st.Docs[DocNames.Manifest] = JsonSerializer.SerializeToNode(m)!;
            return st;
        }

        public static JsonObject Item(int poItem, string gm, string? colour, double qty, string? date = "2026-11-15") => new()
        {
            ["po_item"] = poItem, ["article"] = "0000000" + gm + "01", ["generic_article"] = "00000000" + gm, ["colour"] = colour,
            ["merch_category"] = "MC1021", ["season"] = "AW26", ["order_qty"] = qty, ["uom"] = "PC", ["net_value"] = qty * 349, ["delivery_date"] = date
        };

        /// <summary>The YAML DecisionRequest example with the given items.</summary>
        public static JsonObject Request(string requestId, params JsonObject[] items) => new()
        {
            ["request_id"] = requestId, ["mode"] = "SHADOW", ["source_system"] = "ECP", ["client"] = "300", ["otb_check"] = "PASSED",
            ["depth_check"] = "PASSED", ["requested_at"] = "2026-10-21T11:50:00+05:30",
            ["purchase_order"] = new JsonObject
            {
                ["po_number"] = "4500099001", ["doc_type"] = "NB", ["vendor"] = "0000201345", ["purch_org"] = "1000", ["purch_group"] = "B07",
                ["buyer_user"] = "BUYER07", ["buyer_email"] = "ravi.k@vishalwholesale.co.in", ["division"] = "MENS", ["currency"] = "INR",
                ["items"] = new JsonArray(items.Select(i => (JsonNode)i).ToArray())
            }
        };
    }
}
