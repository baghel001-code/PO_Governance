using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Application.Interfaces.V1;
using Application.Interfaces.V1.Ingest;
using Application.Interfaces.V1.Sap;
using Application.Services.V1.Platform;
using DecisionEngine.Model;
using DecisionEngine.Mra;
using DecisionEngine.Results.Mra;
using Domain.Entities.AppSettings;
using Domain.Entities.Sap;
using Domain.Entities.Store;
using Domain.Exceptions;
using Domain.Records;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Application.Services.V1.Sap
{
    /// <summary>JSON of the stored messages and file records (PascalCase, nulls left out).</summary>
    public static class MraStoreJson
    {
        public static readonly JsonSerializerOptions Options = new() { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };
        public static string Serialize<T>(T v) => JsonSerializer.Serialize(v, Options);
        public static T? Deserialize<T>(string? s) => string.IsNullOrEmpty(s) ? default : JsonSerializer.Deserialize<T>(s, Options);
    }

    /// <summary>Receiving live messages (SAP or the admin test sender): store every message, then replay.</summary>
    internal static class MraFeedIntake
    {
        /// <summary>One message {…} or a batch {"messages": […]}.</summary>
        public static List<JsonNode?> Messages(JsonNode? body) =>
            body is JsonObject o && o["messages"] is JsonArray arr ? arr.ToList() : new List<JsonNode?> { body };

        public static MraFeedRow Row(string kind, JsonNode? raw)
        {
            var rawJson = raw?.ToJsonString() ?? "null";
            if (kind == "mra")
            {
                var (m, err) = MraMessage.Parse(raw);
                return new MraFeedRow(kind, rawJson, m == null ? null : MraStoreJson.Serialize(m), err);
            }
            var (p, perr) = PoMessage.Parse(raw);
            return new MraFeedRow(kind, rawJson, p == null ? null : MraStoreJson.Serialize(p), perr);
        }

        public static async Task<List<MraFeedAnswer>> StoreAndReplayAsync(IMraRepository repo, IMraCapacitySource source, IAppClock clock, string kind,
            List<JsonNode?> msgs, string by, string? sourceSystem, string? traceId, CancellationToken ct)
        {
            var rows = msgs.Select(m => Row(kind, m)).ToList();
            // stored as received, never cancelled half-way
            var seqs = await repo.AddFeedAsync(rows, by, sourceSystem, traceId, clock.Now, CancellationToken.None);
            // v7: while today's MRA file is not uploaded (IsDailyDataUpload false) the messages wait in the pending table
            source.Invalidate();
            var stored = await repo.StateAsync(clock.Today, ct);
            var st = MraReplay.Run(stored.File, stored.Feed);
            var bySeq = st.Results.ToDictionary(r => r.Seq);
            var answers = new List<MraFeedAnswer>();
            for (var i = 0; i < rows.Count; i++)
            {
                MraFeedResult? r = null;
                if (!seqs[i].Pending) bySeq.TryGetValue(seqs[i].Seq, out r);
                answers.Add(new MraFeedAnswer
                {
                    Seq = seqs[i].Seq,
                    Outcome = rows[i].Error != null ? MraFeedOutcomes.Parked : seqs[i].Pending ? MraFeedOutcomes.Pending : r?.Outcome,
                    Reason = rows[i].Error ?? (seqs[i].Pending ? MraFeedOutcomes.PendingReason : r?.Reason),
                    Mra = kind == "mra" ? MraStoreJson.Deserialize<MraMessage>(rows[i].MsgJson)?.Mra : MraStoreJson.Deserialize<PoMessage>(rows[i].MsgJson)?.Mra,
                    Po = kind == "po" ? MraStoreJson.Deserialize<PoMessage>(rows[i].MsgJson)?.Po : null
                });
            }
            return answers;
        }
    }

    /// <summary>SAP answer of /mra-events and /po-events (snake_case).</summary>
    public sealed class SapMraFeedResponse
    {
        [JsonPropertyName("batch_id")] public string BatchId { get; set; } = "";
        [JsonPropertyName("received")] public int Received { get; set; }
        [JsonPropertyName("applied")] public int Applied { get; set; }
        [JsonPropertyName("no_change")] public int NoChange { get; set; }
        [JsonPropertyName("duplicate")] public int Duplicate { get; set; }
        [JsonPropertyName("older")] public int Older { get; set; }
        [JsonPropertyName("parked")] public int Parked { get; set; }
        [JsonPropertyName("stored")] public int Stored { get; set; }
        /// <summary>v7: kept in the pending table until today's MRA file is uploaded.</summary>
        [JsonPropertyName("pending")] public int Pending { get; set; }
        [JsonPropertyName("results")] public List<SapMraFeedResult> Results { get; set; } = new();
        [JsonPropertyName("received_at")] public string ReceivedAt { get; set; } = "";
    }

    public sealed class SapMraFeedResult
    {
        [JsonPropertyName("index")] public int Index { get; set; }
        [JsonPropertyName("seq")] public long Seq { get; set; }
        [JsonPropertyName("outcome")] public string? Outcome { get; set; }
        [JsonPropertyName("reason")] public string? Reason { get; set; }
        [JsonPropertyName("mra_no")] public string? MraNo { get; set; }
        [JsonPropertyName("po_no")] public string? PoNo { get; set; }
    }

    /// <summary>
    /// POST /api/sap/v1/mra-events and /po-events (v1.1.7 SAP-MRA-INTERFACE.txt §2): one message or {"messages": [...]}
    /// (max <see cref="SapInterfaceSettings.MaxMraMessagesPerBatch"/>). Every message is stored as received, also a bad one
    /// ("parked"); the answer gives one result per message in the same order: applied | no change | duplicate | older |
    /// parked | stored (PO message).
    /// </summary>
    public sealed class SapMraFeedService : ISapMraFeedService
    {
        private readonly IMraRepository _repo;
        private readonly IMraCapacitySource _source;
        private readonly IAppClock _clock;
        private readonly SapInterfaceSettings _cfg;
        private readonly ILogger<SapMraFeedService> _log;

        public SapMraFeedService(IMraRepository repo, IMraCapacitySource source, IAppClock clock, IOptions<SapInterfaceSettings> cfg, ILogger<SapMraFeedService> log)
        {
            _repo = repo; _source = source; _clock = clock; _cfg = cfg.Value; _log = log;
        }

        public async Task<SapCallResult> ReceiveAsync(string kind, string body, SapCallContext caller, CancellationToken ct = default)
        {
            if (!_repo.Available)
                throw new SapApiException(503, SapErrorCodes.StorageNotSupported, _repo.UnavailableReason ?? "SAP interface requires SQL Server storage");
            JsonNode? node;
            try
            {
                node = JsonNode.Parse(body);
            }
            catch (JsonException ex)
            {
                throw new SapApiException(400, SapErrorCodes.InvalidJson, "Invalid JSON: " + ex.Message);
            }
            var msgs = MraFeedIntake.Messages(node);
            if (msgs.Count == 0) throw new SapApiException(400, SapErrorCodes.ValidationFailed, "messages must not be empty");
            var max = Math.Max(1, _cfg.MaxMraMessagesPerBatch);
            if (msgs.Count > max) throw new SapApiException(413, SapErrorCodes.BatchTooLarge, $"{msgs.Count} messages in one call - at most {max}");
            var answers = await MraFeedIntake.StoreAndReplayAsync(_repo, _source, _clock, kind, msgs, caller.TechUser, caller.SourceSystem, caller.TraceId, ct);
            var resp = new SapMraFeedResponse
            {
                BatchId = (kind == "mra" ? "MRAEV-" : "POEV-") + _clock.Now.ToString("yyyyMMdd-HHmmss") + "-" +
                          Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(2)).ToLowerInvariant(),
                Received = answers.Count, ReceivedAt = SapJson.Timestamp(_clock.Now),
                Results = answers.Select((a, i) => new SapMraFeedResult { Index = i, Seq = a.Seq, Outcome = a.Outcome, Reason = a.Reason, MraNo = a.Mra, PoNo = a.Po }).ToList()
            };
            resp.Applied = answers.Count(a => a.Outcome == MraFeedOutcomes.Applied);
            resp.NoChange = answers.Count(a => a.Outcome == MraFeedOutcomes.NoChange);
            resp.Duplicate = answers.Count(a => a.Outcome == MraFeedOutcomes.Duplicate);
            resp.Older = answers.Count(a => a.Outcome == MraFeedOutcomes.Older);
            resp.Parked = answers.Count(a => a.Outcome == MraFeedOutcomes.Parked);
            resp.Stored = answers.Count(a => a.Outcome == MraFeedOutcomes.Stored);
            resp.Pending = answers.Count(a => a.Outcome == MraFeedOutcomes.Pending);
            _log.LogInformation("SAP {Kind} events {BatchId}: {Received} message(s) - applied {Applied}, no change {NoChange}, duplicate {Duplicate}, " +
                                "older {Older}, parked {Parked}, stored {Stored}, pending {Pending} (by {TechUser})",
                kind.ToUpperInvariant(), resp.BatchId, resp.Received, resp.Applied, resp.NoChange, resp.Duplicate, resp.Older, resp.Parked, resp.Stored,
                resp.Pending, caller.TechUser);
            if (resp.Parked > 0) _log.LogWarning("SAP {Kind} events {BatchId}: {Count} message(s) set aside (parked) - see the SAP Feed page", kind, resp.BatchId, resp.Parked);
            return new SapCallResult { Status = 200, Json = SapJson.Serialize(resp) };
        }
    }

    /// <summary>
    /// Replayed MRA position, cached per (data version, params version, stamp, day). The stamp is read on every call
    /// (one small query), so a message from SAP counts in the very next New PO request.
    /// </summary>
    public sealed class MraCapacitySource : IMraCapacitySource
    {
        private readonly IMraRepository _repo;
        private readonly IAppClock _clock;
        private readonly IAccountsService _acc;
        private readonly SemaphoreSlim _gate = new(1, 1);
        private (string Key, MraStoredState Stored, MraCapacity Cap)? _cached;

        public MraCapacitySource(IMraRepository repo, IAppClock clock, IAccountsService acc)
        {
            _repo = repo; _clock = clock; _acc = acc;
        }

        public void Invalidate() => _cached = null;

        public async Task<MraCapacity?> GetAsync(EngineData data, CancellationToken ct = default) =>
            (await GetFullAsync(data, ct))?.Capacity;

        public async Task<(MraStoredState Stored, MraCapacity Capacity)?> GetFullAsync(EngineData data, CancellationToken ct = default)
        {
            if (!_repo.Available) return null;
            var stamp = await _repo.StampAsync(_clock.Today, ct);
            var today = _clock.Today;
            var sw = MraSwitches.From(data.Params);
            var key = $"{data.DataVersion}/{data.ParamsVersion}/{stamp}/{today}/{sw.LivePo}";
            var c = _cached;
            if (c != null && c.Value.Key == key) return (c.Value.Stored, c.Value.Cap);
            await _gate.WaitAsync(ct);
            try
            {
                c = _cached;
                if (c != null && c.Value.Key == key) return (c.Value.Stored, c.Value.Cap);
                var stored = await _repo.StateAsync(today, ct);
                var owners = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var u in await _acc.GetUsersAsync(ct))
                    if (!string.IsNullOrEmpty(u.Email)) owners[u.Email] = string.IsNullOrEmpty(u.Name) ? u.Email : u.Name;
                var cap = MraCapacity.Build(data, MraReplay.Run(stored.File, stored.Feed), today, sw.LivePo, owners);
                _cached = (key, stored, cap);
                return (stored, cap);
            }
            finally
            {
                _gate.Release();
            }
        }
    }

    /// <summary>Data Setup → MRA file, MRA Queues and SAP Feed pages (server.py /api/mra/*).</summary>
    public sealed class MraService : IMraService
    {
        private const int LogRows = 1500;
        private readonly IMraRepository _repo;
        private readonly IMraCapacitySource _source;
        private readonly IWorkbookReader _reader;
        private readonly IDatasetCache _cache;
        private readonly IStore _store;
        private readonly IAccountsService _acc;
        private readonly IAppClock _clock;
        private readonly IAuditService _audit;
        private readonly SapInterfaceSettings _sap;
        private readonly PoGovSettings _cfg;
        private readonly ILogger<MraService> _log;

        public MraService(IMraRepository repo, IMraCapacitySource source, IWorkbookReader reader, IDatasetCache cache, IStore store, IAccountsService acc,
                          IAppClock clock, IAuditService audit, IOptions<SapInterfaceSettings> sap, IOptions<PoGovSettings> cfg, ILogger<MraService> log)
        {
            _repo = repo; _source = source; _reader = reader; _cache = cache; _store = store; _acc = acc; _clock = clock; _audit = audit;
            _sap = sap.Value; _cfg = cfg.Value; _log = log;
        }

        private void RequireAvailable()
        {
            if (!_repo.Available) throw new ServiceUnavailableException(_repo.UnavailableReason ?? "SAP MRAs require SQL Server storage");
        }

        public async Task<MraUploadResponse> UploadAsync(DTOs.Ingest.IngestUpload file, AppUserContext user, CancellationToken ct = default)
        {
            if (!PlatformCore.IsStaff(user)) throw new ForbiddenException("Only planners and admins can upload the MRA file");
            RequireAvailable();
            var resp = new MraUploadResponse { FileName = file.FileName };
            MraWorkbookParser.Result p;
            try
            {
                p = MraWorkbookParser.Parse(_reader.Read(file.Content, file.FileName));
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                resp.Error = "The file could not be read as Excel: " + e.Message + " Nothing was changed.";
                return resp;
            }
            if (p.Error != null) { resp.Error = p.Error; return resp; }
            if (p.Mras.Count == 0) { resp.Error = "No MRA could be read from the item-lines sheet. Nothing was changed."; return resp; }

            // decisions SAP sent earlier (live) are kept when the new file does not carry them
            var before = await _repo.StateAsync(_clock.Today, ct);
            var prev = MraReplay.Run(before.File, before.Feed).Mras;
            foreach (var (k, rec) in p.Mras)
                if (prev.TryGetValue(k, out var old) && rec.DecisionInfo?.Decision == null && old.Decision.Decision != null)
                {
                    rec.DecisionInfo = old.Decision.Clone();
                    resp.DecisionsKept++;
                }

            resp.ItemSheet = p.ItemSheet; resp.HeaderSheet = p.HeaderSheet; resp.Mras = p.Mras.Count;
            foreach (var r in p.Mras.Values) resp.ByLevel[r.Level] = (resp.ByLevel.TryGetValue(r.Level, out var n) ? n : 0) + 1;
            resp.RowsItem = p.RowsItem; resp.RowsHeader = p.RowsHeader; resp.Skipped = p.Skipped; resp.Mixed = p.Mixed;
            resp.HeaderOnly = p.HeaderOnly; resp.ItemOnly = p.ItemOnly; resp.LevelDiff = p.LevelDiff; resp.Invalid = p.Invalid;
            resp.Decisions = p.Mras.Values.Count(x => x.DecisionInfo?.Decision != null);
            resp.UploadId = "MRAUPL-" + _clock.Now.ToString("yyyyMMdd-HHmmss");
            resp.DataVersion = resp.UploadId;
            // v7: the SAP changes received before the file (pending table) are moved onto it in the same transaction
            resp.MovedFromPending = await _repo.UploadAsync(p.Mras, resp.UploadId, _clock.Today, file.FileName, user.AuditName, MraStoreJson.Serialize(resp), ct);
            if (resp.MovedFromPending > 0)
                resp.Warnings.Insert(0, $"{resp.MovedFromPending} SAP change(s) received before this file were applied on top of it");
            _source.Invalidate();
            resp.Ok = true;

            if (p.Skipped > 0) resp.Warnings.Add($"{p.Skipped} item row(s) skipped (MRA No, article, colour, date, level or qty missing)");
            if (p.Mixed > 0) resp.Warnings.Add($"{p.Mixed} item row(s) with a different article / colour / date / level than the first line of the same MRA (first line used)");
            if (p.HeaderOnly > 0) resp.Warnings.Add($"{p.HeaderOnly} MRA(s) only in the header sheet (no item lines – not counted)");
            if (p.ItemOnly > 0) resp.Warnings.Add($"{p.ItemOnly} MRA(s) only in the item sheet (no PO details)");
            if (p.LevelDiff > 0) resp.Warnings.Add($"{p.LevelDiff} MRA(s) with a different level in the two sheets (item sheet used)");
            if (p.Invalid > 0) resp.Warnings.Add($"{p.Invalid} MRA(s) left out: level, article, colour, delivery date or qty not readable");

            var full = await _source.GetFullAsync(await _cache.GetCurrentAsync(ct), ct);
            if (full is { } f)
            {
                var counted = f.Capacity.Rows.Where(x => x.C.Counted).ToList();
                resp.CountedMras = counted.Count;
                resp.CountedQty = counted.Sum(x => x.M.Qty);
            }
            await _audit.AppendAsync(user.AuditName, "mra-upload",
                $"{file.FileName} mras={resp.Mras} levels={string.Join(",", resp.ByLevel.Select(kv => kv.Key + ":" + kv.Value))}", ct);
            _log.LogInformation("MRA file {File} loaded by {User}: {Mras} MRAs, {Counted} counted ({Qty} pcs)", file.FileName, user.AuditName, resp.Mras, resp.CountedMras, resp.CountedQty);
            return resp;
        }

        public async Task<MraStateResponse> StateAsync(AppUserContext user, CancellationToken ct = default)
        {
            var data = await _cache.GetCurrentAsync(ct);
            var sw = MraSwitches.From(data.Params);
            var resp = new MraStateResponse { Today = _clock.Today, Switches = sw, ApiEnabled = _sap.Enabled && _repo.Available };
            var full = await _source.GetFullAsync(data, ct);
            if (full is not { } f)
            {
                resp.Available = false;
                resp.UnavailableReason = _repo.UnavailableReason ?? "SAP MRAs require SQL Server storage";
                return resp;
            }
            var (stored, cap) = f;
            resp.Snapshot = stored.Snapshot;
            resp.Control = stored.Control;
            resp.Stamp = await _repo.StampAsync(_clock.Today, ct);
            resp.Restricted = !PlatformCore.IsStaff(user) && _cfg.RestrictToOwnMcs;
            HashSet<string>? mine = null;
            var scope = resp.Restricted ? await _acc.ScopeIndexAsync(ct) : null;
            if (resp.Restricted) mine = new HashSet<string>(scope!.KeysOf(user.Email), StringComparer.Ordinal);
            bool OkInfo(MraArticleInfo? i) => mine == null || (i != null && mine.Contains(string.Join("|", PlatformCore.NKey(i.Brand), PlatformCore.NKey(i.McCd), PlatformCore.NKey(i.Mrp))));

            // v8: each MRA linked to the tool's own decision (SAP does not send it back)
            var decisions = MraDecisionLinker.Index(await DecisionsSafeAsync(ct));
            foreach (var (no, m, c) in cap.Rows)
                if (OkInfo(c.Info))
                {
                    var td = MraDecisionLinker.Link(m, no, c.Mon, decisions);
                    resp.Rows.Add(new MraRowDto { No = no, M = m, C = c, ToolDecision = td, DecGroup = td == null ? "Not sent" : MraCapacity.DecisionGroup(td.Decision) });
                }
            var visibleMras = new HashSet<string>(resp.Rows.Select(r => r.No), StringComparer.Ordinal);
            var effects = cap.PoEffects.ToDictionary(e => e.Po, StringComparer.Ordinal);
            foreach (var (po, ps) in cap.State.Pos)
            {
                var ok = mine == null || OkInfo(ps.Ga != null ? cap.Article(ps.Ga) : null) || (ps.Mra != null && visibleMras.Contains(ps.Mra));
                if (ok) resp.Pos.Add(new MraPoRowDto { Po = po, State = ps, Effect = effects.TryGetValue(po, out var e) ? e : null });
            }

            var results = cap.State.Results.ToDictionary(r => r.Seq);
            var log = stored.Feed.Select(ev => (ev, results.TryGetValue(ev.Seq, out var r) ? r : null)).ToList();
            var eff = cap.Effects(log);
            for (var i = Math.Max(0, log.Count - LogRows); i < log.Count; i++)
            {
                var (ev, r) = log[i];
                if (mine != null)
                {
                    if (ev.Error != null) continue;
                    var keep = (ev.Mra != null && visibleMras.Contains(ev.Mra.Mra)) || (ev.Po != null && resp.Pos.Any(p => p.Po == ev.Po.Po));
                    if (!keep) continue;
                }
                resp.Log.Add(new MraLogRowDto
                {
                    Seq = ev.Seq, At = ev.At, By = ev.By, Kind = ev.Kind, Mra = ev.Mra, Po = ev.Po, Raw = mine == null ? ev.Raw : null, Error = ev.Error,
                    Outcome = ev.Error != null ? MraFeedOutcomes.Parked : r?.Outcome, Reason = ev.Error ?? r?.Reason,
                    EffectText = eff[i].Text, EffectCls = eff[i].Cls
                });
            }
            // v7: SAP changes waiting for today's MRA file (not applied yet)
            foreach (var ev in stored.Pending)
            {
                if (mine != null && (ev.Error != null || !((ev.Mra != null && visibleMras.Contains(ev.Mra.Mra)) || (ev.Po?.Mra != null && visibleMras.Contains(ev.Po.Mra)))))
                    continue;
                resp.Pending.Add(new MraLogRowDto
                {
                    Seq = ev.Seq, At = ev.At, By = ev.By, Kind = ev.Kind, Mra = ev.Mra, Po = ev.Po, Raw = mine == null ? ev.Raw : null, Error = ev.Error,
                    Outcome = ev.Error != null ? MraFeedOutcomes.Parked : MraFeedOutcomes.Pending, Reason = ev.Error ?? MraFeedOutcomes.PendingReason,
                    EffectText = ev.Error != null ? "set aside" : "waits for today's MRA file", EffectCls = "zr"
                });
            }
            foreach (var (email, name) in await OwnersAsync(scope, mine, ct)) resp.Owners[email] = name;
            return resp;
        }

        private async Task<Dictionary<string, string>> OwnersAsync(Application.DTOs.Platform.ScopeIndex? scope, HashSet<string>? mine, CancellationToken ct)
        {
            var users = (await _acc.GetUsersAsync(ct)).ToDictionary(u => u.Email, StringComparer.OrdinalIgnoreCase);
            var o = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (mine != null && scope != null)
            {
                foreach (var (e, d) in scope.People)
                    if (d.Keys.Overlaps(mine)) o[e] = users.TryGetValue(e, out var u) && !string.IsNullOrEmpty(u.Name) ? u.Name : e;
            }
            else foreach (var u in users.Values) o[u.Email] = string.IsNullOrEmpty(u.Name) ? u.Email : u.Name;
            return o;
        }

        public async Task<MraSwitchesResponse> SetSwitchesAsync(MraSwitchesReq req, AppUserContext user, CancellationToken ct = default)
        {
            if (!user.IsAdmin) throw new ForbiddenException("Only the administrator can change these switches");
            if (req.ToolReqCapacity == null && req.LivePo == null) throw new AppValidationException("Send ToolReqCapacity and / or LivePo");
            MraSwitches before, after;
            Manifest m;
            using (await _store.LockAsync(ct))
            {
                var p = await _store.GetDocAsync(DocNames.Params, ct) as JsonObject ?? new JsonObject();
                before = Read(p);
                if (req.ToolReqCapacity != null) p[MraSwitches.ToolReqKey] = req.ToolReqCapacity.Value;
                if (req.LivePo != null) p[MraSwitches.LivePoKey] = req.LivePo.Value;
                after = Read(p);
                await _store.PutDocAsync(DocNames.Params, p, ct);
                m = await _store.GetManifestAsync(ct);
                m.ParamsVersion += 1;
                await _store.SaveManifestAsync(m, ct);
            }
            _cache.Invalidate();
            _source.Invalidate();
            static string On(bool b) => b ? "ON" : "OFF";
            await _audit.AppendAsync(user.AuditName, "switches",
                $"Tool requests use capacity {On(before.ToolReqCapacity)} -> {On(after.ToolReqCapacity)}; Live PO changes {On(before.LivePo)} -> {On(after.LivePo)}", ct);
            return new MraSwitchesResponse { Switches = after, ParamsVersion = m.ParamsVersion };
        }

        /// <summary>Switches as stored in the params document.</summary>
        public static MraSwitches Read(JsonObject? p) => new()
        {
            ToolReqCapacity = p?[MraSwitches.ToolReqKey] is not JsonValue t || !(t.GetValueKind() == JsonValueKind.False),
            LivePo = p?[MraSwitches.LivePoKey] is JsonValue l && l.GetValueKind() == JsonValueKind.True
        };

        /// <summary>Decision items SAP stored an MRA number for (POST /documents); none (logged) when the lookup fails.</summary>
        private async Task<List<MraToolDecision>> DecisionsSafeAsync(CancellationToken ct)
        {
            try
            {
                return await _repo.DecisionsAsync(_clock.Now.Date.AddDays(-DecisionDays), ct);
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                _log.LogWarning(e, "MRA Queues: the tool's decisions could not be read - shown without them");
                return new List<MraToolDecision>();
            }
        }

        /// <summary>How far back a saved decision is linked to an MRA.</summary>
        public const int DecisionDays = 180;

        public async Task<MraDecisionDetail> DecisionAsync(string mraNo, AppUserContext user, CancellationToken ct = default)
        {
            var no = MraNorm.NoZeros((mraNo ?? "").Trim());
            var st = await StateAsync(user, ct);                       // the user's own view (buyers / category heads: own MCs)
            var row = st.Rows.FirstOrDefault(r => r.No == no) ?? throw new NotFoundException("MRA " + no + " not found");
            var res = new MraDecisionDetail { MraNo = no, Decision = row.ToolDecision };
            if (row.ToolDecision is { HasWorking: true } td && await _repo.WorkingAsync(td.ItemId, ct) is { Length: > 0 } json)
                res.Working = JsonNode.Parse(json) as JsonObject;
            return res;
        }

        public async Task<MraFeedResponse> TestSendAsync(MraTestSendReq req, AppUserContext user, CancellationToken ct = default)
        {
            if (!user.IsAdmin) throw new ForbiddenException("Only the SAP feed or the administrator can send changes");
            RequireAvailable();
            var kind = req.Kind is "po" ? "po" : "mra";
            JsonNode? node;
            try
            {
                node = JsonNode.Parse(req.Json ?? "");
            }
            catch (JsonException ex)
            {
                throw new AppValidationException("Not valid JSON: " + ex.Message);
            }
            var msgs = MraFeedIntake.Messages(node);
            if (msgs.Count == 0 || msgs.Count > Math.Max(1, _sap.MaxMraMessagesPerBatch))
                throw new AppValidationException($"Send 1 to {_sap.MaxMraMessagesPerBatch} messages");
            var answers = await MraFeedIntake.StoreAndReplayAsync(_repo, _source, _clock, kind, msgs, user.AuditName, "TEST", null, ct);
            _log.LogInformation("SAP feed test: {Count} {Kind} message(s) from {User}", msgs.Count, kind, user.AuditName);
            return new MraFeedResponse { Results = answers };
        }
    }
}
