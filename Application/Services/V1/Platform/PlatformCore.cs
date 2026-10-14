using System.Globalization;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Application.DTOs.Platform;
using Application.Interfaces.V1;
using Domain.Constants;
using Domain.Entities.AppSettings;
using Domain.Entities.Store;
using Domain.Exceptions;
using Domain.Records;

namespace Application.Services.V1.Platform
{
    /// <summary>
    /// Shared helpers of the platform services — the module-level functions of server.py
    /// (manifest, _replay, _apply_approval, _next_number, new_batch, _new_id, req_key, visible_to …).
    /// </summary>
    public static class PlatformCore
    {
        public static readonly Regex KeyRe = new(@"^[A-Za-z0-9_]{1,40}$", RegexOptions.Compiled);
        public static readonly Regex DateRe = new(@"^\d{4}-\d{2}-\d{2}$", RegexOptions.Compiled);
        public static readonly Regex VerRe = new(@"^[A-Za-z0-9_-]{1,60}$", RegexOptions.Compiled);
        public static readonly Regex UploadIdRe = new(@"^UPL-\d{8}-\d{4}$", RegexOptions.Compiled);
        public static readonly Regex ReqIdRe = new(@"^REQ-(\d{4})(\d{2})(\d{2})-\d{4}$", RegexOptions.Compiled);

        private static readonly JsonSerializerOptions ManifestOptions = new(PyJson.Options)
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };

        // ── time ────────────────────────────────────────────────────

        /// <summary>Python time.time().</summary>
        public static double Epoch(this IAppClock clock) => clock.Now.ToUnixTimeMilliseconds() / 1000.0;

        /// <summary>server.py _new_id(): yyyyMMdd-HHmmss-xxxx (low 16 bits of the epoch milliseconds).</summary>
        public static string NewId(IAppClock clock)
        {
            var now = clock.Now;
            return now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) + "-" +
                   (now.ToUnixTimeMilliseconds() & 0xFFFF).ToString("x4", CultureInfo.InvariantCulture);
        }

        /// <summary>ISO string → epoch seconds (naive = business time zone offset of <paramref name="clock"/>).</summary>
        public static double ParseIsoEpoch(string iso, IAppClock clock)
        {
            if (DateTimeOffset.TryParse(iso, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dto))
            {
                var hasOffset = Regex.IsMatch(iso, @"(Z|[+-]\d{2}:?\d{2})$");
                if (!hasOffset) dto = new DateTimeOffset(dto.DateTime, clock.Now.Offset);
                return dto.ToUnixTimeMilliseconds() / 1000.0;
            }
            return 0;
        }

        // ── manifest ────────────────────────────────────────────────

        /// <summary>server.py manifest(): the manifest document or an empty one.</summary>
        public static async Task<Manifest> GetManifestAsync(this IStore store, CancellationToken ct = default)
        {
            var node = await store.GetDocAsync(DocNames.Manifest, ct);
            if (node is not JsonObject o || o.Count == 0) return new Manifest();
            var m = o.Deserialize<Manifest>(PyJson.Options) ?? new Manifest();
            m.Datasets ??= new Dictionary<string, DatasetEntry>();
            return m;
        }

        public static Task SaveManifestAsync(this IStore store, Manifest m, CancellationToken ct = default) =>
            store.PutDocAsync(DocNames.Manifest, JsonSerializer.SerializeToNode(m, ManifestOptions)!, ct);

        public static JsonObject EntryToObject(DatasetEntry e) => (JsonObject)JsonSerializer.SerializeToNode(e, ManifestOptions)!;

        // ── requests ────────────────────────────────────────────────

        /// <summary>
        /// server.py _replay: replays a day's request events. A "clear" event empties the live ledger
        /// (cleared requests are marked and, with includeCleared, still returned first).
        /// </summary>
        public static (List<JsonObject> Requests, int Seq) Replay(IEnumerable<JsonObject> recs, bool includeCleared = false)
        {
            var reqs = new List<JsonObject>();
            var cleared = new List<JsonObject>();
            long seq = 0;
            foreach (var rec in recs)
            {
                seq = Math.Max(seq, rec.GetLong("seq", 0));
                var type = rec.GetStr("type");
                if (type == RequestEventTypes.Clear)
                {
                    foreach (var r in reqs)
                    {
                        r["cleared"] = new JsonObject { ["by"] = rec.Get("by")?.DeepClone(), ["at"] = rec.Get("at")?.DeepClone() };
                        if (r.GetObj("approval") is { } a && a.GetStr("status") == ApprovalStatus.Pending)
                            a["status"] = ApprovalStatus.Cleared;
                    }
                    cleared.AddRange(reqs);
                    reqs = new List<JsonObject>();
                }
                else if (type == RequestEventTypes.Request)
                {
                    if (rec.Get("request") is JsonObject r) reqs.Add(r);
                }
                else if (type == RequestEventTypes.Approval)
                {
                    var rid = rec.GetStr("reqId");
                    foreach (var r in reqs)
                        if (r.GetStr("reqId") == rid) ApplyApproval(r, rec);
                }
            }
            return (includeCleared ? cleared.Concat(reqs).ToList() : reqs, (int)seq);
        }

        /// <summary>server.py _apply_approval.</summary>
        public static void ApplyApproval(JsonObject r, JsonObject rec)
        {
            if (r.Get("approval") is not JsonObject a)
            {
                a = new JsonObject();
                r["approval"] = a;
            }
            var status = rec.GetStr("status");
            a["status"] = status;
            a["by"] = rec.Get("by")?.DeepClone();
            a["byName"] = rec.Get("byName")?.DeepClone();
            a["at"] = rec.Get("at")?.DeepClone();
            a["comment"] = rec.ContainsKey("comment") ? rec["comment"]?.DeepClone() : "";
            if (status == ApprovalStatus.Approved)
            {
                a["qty"] = rec.Get("qty")?.DeepClone();
                r["finalApprovedQty"] = rec.Get("qty")?.DeepClone();
            }
            else if (status == ApprovalStatus.Rejected)
            {
                a["qty"] = 0;
                r["finalApprovedQty"] = 0;
            }
        }

        public static async Task<(List<JsonObject> Requests, int Seq)> ReadRequestsAsync(this IStore store, string date, bool includeCleared = false, CancellationToken ct = default) =>
            Replay(await store.ReadLogAsync(LogStreams.Requests, date, ct), includeCleared);

        /// <summary>accounts.py nkey (page _n()): trim, drop a trailing ".0", lowercase.</summary>
        public static string NKey(string? s)
        {
            s = (s ?? "").Trim();
            if (s.EndsWith(".0", StringComparison.Ordinal)) s = s[..^2];
            return s.ToLowerInvariant();
        }

        /// <summary>nkey of a JSON value (None → "").</summary>
        public static string NKey(JsonNode? v) => NKey(v == null || (v is JsonValue jv && jv.GetValueKind() == JsonValueKind.Null) ? "" : PyJson.Str(v));

        /// <summary>nkey of a row cell (None → "").</summary>
        public static string NKeyCell(object? v) => NKey(v == null ? "" : PyJson.Str(v));

        /// <summary>server.py req_key: "brand|mc|mrp" of a request's info.</summary>
        public static string ReqKey(JsonObject r)
        {
            var i = r.GetObj("info");
            return string.Join("|", NKey(i.Get("brand")), NKey(i.Get("mcCd")), NKey(i.Get("mrp")));
        }

        public static bool IsStaff(AppUserContext? u) => u != null && u.Has(Roles.Admin, Roles.Planner);

        /// <summary>server.py visible_to: buyers / category heads see their own requests plus those on their MCs.</summary>
        public static bool VisibleTo(AppUserContext u, JsonObject r, ScopeIndex scope, PoGovSettings cfg)
        {
            if (IsStaff(u) || !cfg.RestrictToOwnMcs) return true;
            if ((r.GetStrOrEmpty("requestedByEmail")) == u.Email) return true;
            var a = r.GetObj("approval");
            if (a.GetStrList("approvers").Contains(u.Email)) return true;
            return scope.KeysOf(u.Email).Contains(ReqKey(r));
        }

        // ── ids / batches ───────────────────────────────────────────

        /// <summary>server.py _next_number: next running number PREFIX-YYYYMMDD-0001 (per day; 9001+ are back-filled IDs).</summary>
        public static async Task<string> NextNumberAsync(IStore store, IAppClock clock, string stream, string prefix, CancellationToken ct = default) =>
            NextNumber(await store.ReadLogAsync(stream, null, ct), clock.Now.ToString("yyyyMMdd", CultureInfo.InvariantCulture), prefix);

        public static string NextNumber(IEnumerable<JsonObject> entries, string day, string prefix)
        {
            var tag = prefix + "-" + day + "-";
            var n = 0;
            foreach (var e in entries)
                foreach (var kv in e)
                {
                    var s = PyJson.AsString(kv.Value);
                    if (s == null || !s.StartsWith(tag, StringComparison.Ordinal)) continue;
                    var rest = s[tag.Length..];
                    var num = rest.Length > 4 ? rest[..4] : rest;
                    if (int.TryParse(num, NumberStyles.AllowLeadingWhite | NumberStyles.AllowTrailingWhite | NumberStyles.AllowLeadingSign,
                            CultureInfo.InvariantCulture, out var k) && k < 9000)
                        n = Math.Max(n, k);
                }
            return tag + (n + 1).ToString("0000", CultureInfo.InvariantCulture);
        }

        /// <summary>server.py new_batch body (the caller holds the store lock).</summary>
        public static async Task<string> NewBatchLockedAsync(IStore store, IAppClock clock, string by, string source, IEnumerable<string>? files, string note, CancellationToken ct)
        {
            var uid = await NextNumberAsync(store, clock, LogStreams.Batches, "UPL", ct);
            var batch = new UploadBatch
            {
                UploadId = uid, At = clock.NowIso, By = by, Source = source,
                SourceFiles = (files ?? Enumerable.Empty<string>()).Take(50).ToList(),
                Note = Trunc(note ?? "", 200)
            };
            await store.AppendLogAsync(LogStreams.Batches, PyJson.ToObject(batch), null, ct);
            return uid;
        }

        // ── misc ────────────────────────────────────────────────────

        /// <summary>server.py _in_range on the first 10 chars of an ISO time.</summary>
        public static bool InRange(string? iso, string? frm, string? to)
        {
            var s = iso ?? "";
            var d = s.Length > 10 ? s[..10] : s;
            return (string.IsNullOrEmpty(frm) || string.CompareOrdinal(d, frm) >= 0) && (string.IsNullOrEmpty(to) || string.CompareOrdinal(d, to) <= 0);
        }

        /// <summary>server.py Handler._range validation.</summary>
        public static void CheckRange(string? frm, string? to)
        {
            foreach (var d in new[] { frm, to })
                if (!string.IsNullOrEmpty(d) && !DateRe.IsMatch(d))
                    throw new AppValidationException("from/to must be YYYY-MM-DD");
        }

        public static string Trunc(string s, int n) => s.Length > n ? s[..n] : s;

        /// <summary>Python int(x) for a JSON value (ValueError → 400 with Python's wording).</summary>
        public static long PyInt(JsonNode? n)
        {
            if (n == null) throw new AppValidationException("int() argument must be a string, a bytes-like object or a real number, not 'NoneType'");
            if (PyJson.IsNumber(n)) return (long)Math.Truncate(PyJson.AsDouble(n));
            if (PyJson.IsBool(n)) return PyJson.AsDouble(n) != 0 ? 1 : 0;
            var s = PyJson.AsString(n);
            if (s != null && long.TryParse(s.Trim().Replace("_", ""), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var l)) return l;
            throw new AppValidationException($"invalid literal for int() with base 10: '{s ?? n.ToJsonString()}'");
        }
    }
}
