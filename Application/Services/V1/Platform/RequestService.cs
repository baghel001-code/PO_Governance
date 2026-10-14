using System.Text.Json.Nodes;
using Application.DTOs.Platform;
using Application.Interfaces.V1;
using Domain.Constants;
using Domain.Entities.AppSettings;
using Domain.Entities.Store;
using Domain.Exceptions;
using Domain.Records;
using Microsoft.Extensions.Options;

namespace Application.Services.V1.Platform
{
    /// <summary>Tool 1 request log — server.py api_GET/POST/DELETE_requests.</summary>
    public sealed class RequestService : IRequestService
    {
        private readonly IStore _store;
        private readonly IAppClock _clock;
        private readonly IAccountsService _acc;
        private readonly IAuditService _audit;
        private readonly PoGovSettings _cfg;

        public RequestService(IStore store, IAppClock clock, IAccountsService acc, IAuditService audit, IOptions<PoGovSettings> cfg)
        {
            _store = store;
            _clock = clock;
            _acc = acc;
            _audit = audit;
            _cfg = cfg.Value;
        }

        private string Date(string? date)
        {
            var d = string.IsNullOrEmpty(date) ? _clock.Today : date;
            if (!PlatformCore.DateRe.IsMatch(d)) throw new AppValidationException("date must be YYYY-MM-DD");
            return d;
        }

        public async Task<(List<JsonObject> Requests, int Seq)> GetDayAsync(string date, CancellationToken ct = default)
        {
            using (await _store.LockAsync(ct))
                return await _store.ReadRequestsAsync(date, false, ct);
        }

        public async Task<int> GetSeqAsync(string date, CancellationToken ct = default) => (await GetDayAsync(date, ct)).Seq;

        public async Task<RequestsDayResponse> GetDayForUserAsync(string? date, AppUserContext me, CancellationToken ct = default)
        {
            var d = Date(date);
            var (reqs, seq) = await GetDayAsync(d, ct);
            if (!PlatformCore.IsStaff(me) && _cfg.RestrictToOwnMcs)
            {
                var scope = await _acc.ScopeIndexAsync(ct);
                reqs = reqs.Where(r => PlatformCore.VisibleTo(me, r, scope, _cfg)).ToList();
            }
            return new RequestsDayResponse { Date = d, Seq = seq, Requests = reqs, Today = _clock.Today };
        }

        public async Task<JsonObject> SaveAsync(JsonObject r, int seenSeq, bool force, AppUserContext me, CancellationToken ct = default)
        {
            if (!me.Has(Roles.Buyer, Roles.CatHead, Roles.Planner, Roles.Admin)) throw new ForbiddenException("Not allowed");
            if (r == null) throw new AppValidationException("Missing request");
            var date = _clock.Today;   // server date, not the PC clock
            r["reqDate"] = date;
            // Buyers / category heads may only raise requests on the Brand x MC x MRP mapped to them
            // in the SCR products file (admins and planners: any MC).
            if (!PlatformCore.IsStaff(me) && _cfg.RestrictToOwnMcs)
            {
                var i = r.GetObj("info");
                if (i.GetTruthy("mcCd"))
                {
                    var scope = await _acc.ScopeIndexAsync(ct);
                    if (!scope.KeysOf(me.Email).Contains(PlatformCore.ReqKey(r)))
                        throw new ForbiddenException("This Brand / MC / MRP is not mapped to you in the SCR products file. Ask the administrator.");
                }
            }
            int seq;
            using (await _store.LockAsync(ct))
            {
                var (reqs, s) = await _store.ReadRequestsAsync(date, false, ct);
                seq = s;
                if (!force && r.GetTruthy("colourKey"))
                {
                    // Anyone else approve against this exact colour since the client last looked?
                    // Then its headroom check is stale.
                    var ck = r["colourKey"];
                    var newer = reqs.Where(x => x.GetLong("seq", 0) > seenSeq && JsonNode.DeepEquals(x.Get("colourKey"), ck)).ToList();
                    if (newer.Count > 0)
                    {
                        var by = string.Join(", ", newer.Select(x => x.ContainsKey("requestedBy") ? PyJson.Str(x["requestedBy"]) : "?")
                                                        .Distinct().OrderBy(x => x, StringComparer.Ordinal));
                        throw new ConflictException($"{by} saved a request for this colour since you last checked — please validate again.",
                            new RequestConflict { ByOthers = by, Seq = seq });
                    }
                }
                seq += 1;
                r["seq"] = seq;
                r["id"] = reqs.Count + 1;
                r["reqId"] = $"REQ-{date.Replace("-", "")}-{seq:0000}";
                r["requestedBy"] = me.Name;
                r["requestedByEmail"] = me.Email;
                r["savedAt"] = _clock.NowIso;
                r.Remove("approval");
                if (r.GetStr("decision") == Decisions.WatchList)
                {
                    var scope = await _acc.ScopeIndexAsync(ct);
                    var key = PlatformCore.ReqKey(r);
                    var approvers = scope.Approvers.TryGetValue(key, out var l) ? l : new List<string>();
                    var names = new JsonArray();
                    foreach (var e in approvers)
                        if (await _acc.GetUserAsync(e, ct) is { } u) names.Add(u.Name);
                    r["approval"] = new JsonObject
                    {
                        ["status"] = ApprovalStatus.Pending,
                        ["approvers"] = new JsonArray(approvers.Select(x => (JsonNode?)x).ToArray()),
                        ["approverNames"] = names
                    };
                }
                await _store.AppendLogAsync(LogStreams.Requests,
                    new JsonObject { ["type"] = RequestEventTypes.Request, ["seq"] = seq, ["request"] = r.DeepClone() }, date, ct);
            }
            await _audit.AppendAsync(PyJson.Str(r["requestedBy"]), "request",
                $"{PyJson.Str(r.Get("gm"))} {PyJson.Str(r.Get("colour"))} {PyJson.Str(r.Get("month"))} qty={PyJson.Str(r.Get("qty"))} -> {PyJson.Str(r.Get("decision"))} approved={PyJson.Str(r.Get("finalApprovedQty"))}", ct);
            return r;
        }

        public async Task<SeqResponse> ClearDayAsync(string? date, AppUserContext me, CancellationToken ct = default)
        {
            if (!PlatformCore.IsStaff(me)) throw new ForbiddenException("Only planners and admins can do this");
            var d = Date(date);
            int seq;
            using (await _store.LockAsync(ct))
            {
                (_, seq) = await _store.ReadRequestsAsync(d, false, ct);
                seq += 1;
                await _store.AppendLogAsync(LogStreams.Requests,
                    new JsonObject { ["type"] = RequestEventTypes.Clear, ["seq"] = seq, ["by"] = me.AuditName, ["at"] = _clock.NowIso }, d, ct);
            }
            await _audit.AppendAsync(me.AuditName, "clear-requests", d, ct);
            return new SeqResponse { Seq = seq };
        }
    }
}
