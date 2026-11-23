using System.Text.Json.Nodes;
using Application.DTOs.Platform;
using Application.Interfaces.V1;
using DecisionEngine.Model;
using Domain.Constants;
using Domain.Entities.Store;
using Domain.Exceptions;
using Domain.Records;

namespace Application.Services.V1.Platform
{
    /// <summary>Watch List approvals — server.py _approval_items, api_GET/POST_approvals, pending_count.</summary>
    public sealed class ApprovalService : IApprovalService
    {
        private readonly IStore _store;
        private readonly IAppClock _clock;
        private readonly IAuditService _audit;

        public ApprovalService(IStore store, IAppClock clock, IAuditService audit)
        {
            _store = store;
            _clock = clock;
            _audit = audit;
        }

        /// <summary>Requests needing / having had an approval in the last <paramref name="days"/> request days.</summary>
        private async Task<List<JsonObject>> ApprovalItemsAsync(int days, CancellationToken ct)
        {
            var all = await _store.LogDaysAsync(LogStreams.Requests, ct);
            // Python names[-days:] (days <= 0: [-0:] = everything, negative = drop the first |days|)
            var names = days > 0 ? all.Skip(Math.Max(0, all.Count - days)).ToList()
                      : days == 0 ? all : all.Skip(-days).ToList();
            var byDay = await _store.ReadLogsAsync(LogStreams.Requests, names, ct);
            var outp = new List<JsonObject>();
            foreach (var name in names)
            {
                var (reqs, _) = PlatformCore.Replay(byDay.TryGetValue(name, out var l) ? l : new List<JsonObject>());
                outp.AddRange(reqs.Where(r => r.GetTruthy("approval")));
            }
            return outp;
        }

        private static JsonObject Appr(JsonObject r) => r.GetObj("approval") ?? new JsonObject();

        /// <summary>Multi concept rule Step 3: an exception (20–30% band) — only the B&amp;M Head (its approver) or an admin decides it.</summary>
        private static bool IsException(JsonObject r) => r.GetStr("decision") == Decisions.Exception;

        /// <summary>Whether <paramref name="me"/> may decide (and so sees) the approval of <paramref name="r"/>.</summary>
        private static bool CanDecide(JsonObject r, AppUserContext me) =>
            Appr(r).GetStrList("approvers").Contains(me.Email) ||
            (IsException(r) ? me.Has(Roles.Admin) : me.Has(Roles.Admin, Roles.Planner));

        public async Task<ApprovalsResponse> GetAsync(int days, AppUserContext me, CancellationToken ct = default)
        {
            List<JsonObject> items;
            using (await _store.LockAsync(ct)) items = await ApprovalItemsAsync(days, ct);
            // planners and admins see every Watch List approval; an exception only its B&M Head and the admins
            var mine = items.Where(r => CanDecide(r, me)).ToList();
            // your own requests are approved by someone else (category head / admin / planner)
            var pending = mine.Where(r => Appr(r).GetStr("status") == ApprovalStatus.Pending && r.GetStrOrEmpty("requestedByEmail") != me.Email)
                              .OrderBy(r => r.GetStrOrEmpty("savedAt"), StringComparer.Ordinal).ToList();
            var decided = mine.Where(r => Appr(r).GetStr("status") != ApprovalStatus.Pending)
                              .OrderByDescending(r => Appr(r).GetStrOrEmpty("at"), StringComparer.Ordinal).Take(300).ToList();
            return new ApprovalsResponse { Pending = pending, Decided = decided };
        }

        public async Task<int> PendingCountAsync(AppUserContext me, CancellationToken ct = default)
        {
            var staff = PlatformCore.IsStaff(me);
            if (!(staff || me.Has(Roles.CatHead))) return 0;
            var n = 0;
            foreach (var r in await ApprovalItemsAsync(30, ct))
            {
                var a = Appr(r);
                if (a.GetStr("status") == ApprovalStatus.Pending && r.GetStrOrEmpty("requestedByEmail") != me.Email && CanDecide(r, me))
                    n++;
            }
            return n;
        }

        public async Task<SeqResponse> DecideAsync(string reqId, JsonObject body, AppUserContext me, CancellationToken ct = default)
        {
            var rid = reqId ?? "";
            var status = body.GetStr("decision");
            if (!PyJson.IsString(body.Get("decision")) || (status != ApprovalStatus.Approved && status != ApprovalStatus.Rejected))
                throw new AppValidationException("decision must be approved or rejected");
            var m = PlatformCore.ReqIdRe.Match(rid);
            if (!m.Success) throw new AppValidationException("Bad request id");
            var date = $"{m.Groups[1].Value}-{m.Groups[2].Value}-{m.Groups[3].Value}";
            long? qty = null;
            string comment;
            int seq;
            using (await _store.LockAsync(ct))
            {
                var (reqs, s) = await _store.ReadRequestsAsync(date, false, ct);
                seq = s;
                var r = reqs.FirstOrDefault(x => x.GetStr("reqId") == rid);
                if (r == null || !r.GetTruthy("approval")) throw new NotFoundException("Request not found or doesn't need approval");
                var a = Appr(r);
                if (a.GetStr("status") != ApprovalStatus.Pending)
                {
                    var who = a.GetTruthy("byName") ? PyJson.Str(a["byName"]) : PyJson.Str(a.Get("by"));
                    throw new ConflictException($"Already {PyJson.Str(a.Get("status"))} by {who}");
                }
                var approvers = a.GetStrList("approvers");
                if (r.GetStrOrEmpty("requestedByEmail") == me.Email)
                    throw new ForbiddenException("You cannot approve or reject your own request");
                if (!CanDecide(r, me))
                    throw new ForbiddenException(IsException(r)
                        ? $"This exception is waiting for the B&M Head ({(approvers.Count > 0 ? string.Join(", ", approvers) : "an admin")})"
                        : $"This request is waiting for {(approvers.Count > 0 ? string.Join(", ", approvers) : "an admin")}");
                if (status == ApprovalStatus.Approved)
                {
                    var q = body.Get("qty");
                    if (q == null || (q is JsonValue qv && qv.GetValueKind() == System.Text.Json.JsonValueKind.Null) || PyJson.Str(q).Trim().Length == 0)
                        throw new AppValidationException("Enter the quantity to approve");
                    qty = PlatformCore.PyInt(q);
                    var requested = r.GetTruthy("qty") ? PlatformCore.PyInt(r["qty"]) : 0;
                    if (qty < 0 || qty > requested)
                        throw new AppValidationException($"Approved qty must be between 0 and the requested {PyJson.Str(r.Get("qty"))}");
                }
                comment = PlatformCore.Trunc(PyJson.StrOrEmpty(body.Get("comment")), 500);
                if (status == ApprovalStatus.Rejected && comment.Trim().Length == 0)
                    throw new AppValidationException("Please add a reason when rejecting");
                seq += 1;
                var rec = new JsonObject
                {
                    ["type"] = RequestEventTypes.Approval, ["seq"] = seq, ["reqId"] = rid, ["status"] = status,
                    ["qty"] = qty, ["comment"] = comment, ["by"] = me.Email, ["byName"] = me.Name, ["at"] = _clock.NowIso
                };
                await _store.AppendLogAsync(LogStreams.Requests, rec, date, ct);
            }
            await _audit.AppendAsync(me.AuditName, "approval", $"{rid} {status} qty={(qty?.ToString() ?? "None")} {comment}", ct);
            return new SeqResponse { Seq = seq };
        }
    }
}
