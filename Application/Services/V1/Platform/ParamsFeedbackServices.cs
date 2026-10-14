using System.Text.Json.Nodes;
using Application.DTOs.Platform;
using Application.Interfaces.V1;
using Domain.Constants;
using Domain.Entities.Store;
using Domain.Exceptions;
using Domain.Records;

namespace Application.Services.V1.Platform
{
    /// <summary>Planner parameters — server.py api_GET/PUT_params.</summary>
    public sealed class ParamsService : IParamsService
    {
        private static readonly string[] AuditKeys = { "T1", "T2", "D", "mix", "N", "wof", "peer", "strBuffer", "borderTolerance", "seasonCurrent", "seasonFallback" };
        private readonly IStore _store;
        private readonly IAuditService _audit;
        private readonly IDatasetCache _cache;

        public ParamsService(IStore store, IAuditService audit, IDatasetCache cache)
        {
            _store = store;
            _audit = audit;
            _cache = cache;
        }

        public async Task<JsonNode> GetAsync(CancellationToken ct = default)
        {
            var p = await _store.GetDocAsync(DocNames.Params, ct);
            return PyJson.Truthy(p) ? p! : new JsonObject();
        }

        public async Task<ParamsSaveResponse> PutAsync(JsonNode? body, AppUserContext me, CancellationToken ct = default)
        {
            if (!PlatformCore.IsStaff(me)) throw new ForbiddenException("Only planners and admins can do this");
            if (body is not JsonObject p) throw new AppValidationException("Params must be an object");
            Manifest m;
            using (await _store.LockAsync(ct))
            {
                await _store.PutDocAsync(DocNames.Params, p.DeepClone(), ct);
                m = await _store.GetManifestAsync(ct);
                m.ParamsVersion += 1;
                await _store.SaveManifestAsync(m, ct);
            }
            _cache.Invalidate();
            var sel = new JsonObject();
            foreach (var k in AuditKeys) sel[k] = p.Get(k)?.DeepClone();
            await _audit.AppendAsync(me.AuditName, "params", PyJson.Dumps(sel, true), ct);
            return new ParamsSaveResponse { ParamsVersion = m.ParamsVersion };
        }
    }

    /// <summary>User feedback — server.py _feedback_items, api_GET/POST_feedback.</summary>
    public sealed class FeedbackService : IFeedbackService
    {
        private static readonly string[] Statuses = { "new", "in progress", "closed" };
        private readonly IStore _store;
        private readonly IAppClock _clock;
        private readonly IAuditService _audit;

        public FeedbackService(IStore store, IAppClock clock, IAuditService audit)
        {
            _store = store;
            _clock = clock;
            _audit = audit;
        }

        private async Task<List<JsonObject>> ItemsLockedAsync(CancellationToken ct)
        {
            var items = new List<JsonObject>();
            var byId = new Dictionary<string, JsonObject>();
            foreach (var rec in await _store.ReadLogAsync(LogStreams.Feedback, null, ct))
            {
                var type = rec.GetStr("type");
                if (type == "feedback")
                {
                    var it = rec.DeepClone().AsObject();
                    it.Remove("type");
                    if (!it.ContainsKey("status")) it["status"] = "new";
                    items.Add(it);
                    byId[it.GetStr("id") ?? ""] = it;
                }
                else if (type == "status" && byId.TryGetValue(rec.GetStr("id") ?? "", out var it))
                {
                    if (rec.GetTruthy("status")) it["status"] = rec["status"]!.DeepClone();
                    it["note"] = rec.ContainsKey("note") ? rec["note"]?.DeepClone() : (it.ContainsKey("note") ? it["note"]?.DeepClone() : "");
                    it["updatedBy"] = rec.GetTruthy("byName") ? rec["byName"]!.DeepClone() : rec.Get("by")?.DeepClone();
                    it["updatedAt"] = rec.Get("at")?.DeepClone();
                }
            }
            return items.OrderByDescending(x => x.GetStrOrEmpty("at"), StringComparer.Ordinal).ToList();
        }

        public async Task<ItemsResponse> ListAsync(bool mine, AppUserContext me, CancellationToken ct = default)
        {
            List<JsonObject> items;
            using (await _store.LockAsync(ct)) items = await ItemsLockedAsync(ct);
            if (mine || !me.IsAdmin) items = items.Where(x => x.GetStr("by") == me.Email).ToList();
            return new ItemsResponse { Items = items };
        }

        public async Task<FeedbackCreatedResponse> CreateAsync(JsonObject body, AppUserContext me, CancellationToken ct = default)
        {
            var msg = PyJson.StrOrEmpty(body.Get("message")).Trim();
            if (msg.Length == 0) throw new AppValidationException("Please write your feedback");
            var rn = body.Get("rating");
            int? rating = null;
            if (PyJson.IsNumber(rn) || PyJson.IsBool(rn))   // Python: isinstance(rating, (int, float)) — bool is an int
            {
                var r = (long)Math.Truncate(PyJson.AsDouble(rn));
                if (r is >= 1 and <= 5) rating = (int)r;
            }
            string fid;
            using (await _store.LockAsync(ct))
            {
                fid = await PlatformCore.NextNumberAsync(_store, _clock, LogStreams.Feedback, "FB", ct);
                var rec = new JsonObject
                {
                    ["type"] = "feedback", ["id"] = fid, ["at"] = _clock.NowIso, ["by"] = me.Email, ["byName"] = me.Name,
                    ["roles"] = new JsonArray(me.Roles.Select(x => (JsonNode?)x).ToArray()),
                    ["kind"] = PlatformCore.Trunc(PyJson.StrOrEmpty(body.Get("kind")), 40),
                    ["screen"] = PlatformCore.Trunc(PyJson.StrOrEmpty(body.Get("screen")), 60),
                    ["article"] = PlatformCore.Trunc(PyJson.StrOrEmpty(body.Get("article")), 120),
                    ["message"] = PlatformCore.Trunc(msg, 2000),
                    ["rating"] = rating,
                    ["view"] = PlatformCore.Trunc(PyJson.StrOrEmpty(body.Get("view")), 20),
                    ["status"] = "new"
                };
                await _store.AppendLogAsync(LogStreams.Feedback, rec, null, ct);
            }
            await _audit.AppendAsync(me.AuditName, "feedback", fid, ct);
            return new FeedbackCreatedResponse { Id = fid };
        }

        public async Task SetStatusAsync(string id, JsonObject body, AppUserContext me, CancellationToken ct = default)
        {
            if (!me.Has(Roles.Admin)) throw new ForbiddenException("Admin only");
            var status = PyJson.StrOrEmpty(body.Get("status"));
            if (!Statuses.Contains(status)) throw new AppValidationException("status must be new, in progress or closed");
            using (await _store.LockAsync(ct))
            {
                if (!(await ItemsLockedAsync(ct)).Any(x => x.GetStr("id") == id)) throw new NotFoundException("No such feedback");
                await _store.AppendLogAsync(LogStreams.Feedback, new JsonObject
                {
                    ["type"] = "status", ["id"] = id, ["status"] = status,
                    ["note"] = PlatformCore.Trunc(PyJson.StrOrEmpty(body.Get("note")), 1000),
                    ["by"] = me.Email, ["byName"] = me.Name, ["at"] = _clock.NowIso
                }, null, ct);
            }
            await _audit.AppendAsync(me.AuditName, "feedback-status", $"{id} {status}", ct);
        }
    }
}
