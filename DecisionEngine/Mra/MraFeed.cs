using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace DecisionEngine.Mra
{
    // ════════════════════════════════════════════════════════════════════════════════════════════
    // SAP MRA (Merchandise Request) and PO feed — port of the v1.1.7 Python module mra.py.
    // Pure functions: the morning MRA file (snapshot) + every live message received (in arrival
    // order) are replayed into the current state. What counts against capacity is decided by
    // MraCapacity (port of app/mra.js). Rules agreed with the business on 4–5 Nov 2026:
    //   - the morning file (previous day's close) replaces the MRA list; live changes received on or
    //     after the upload day are applied on top, latest per MRA No;
    //   - counted: levels 1, 2, 4 while the delivery date has not passed; level 5 only when reached by a
    //     LIVE change (until the next morning's files) and its PO is not sent as unreleased;
    //   - level 8 → 1 (exception approved by Commercial) counts from that moment;
    //   - PO messages are stored always; they change capacity only with the admin switch "Live PO changes".
    // ════════════════════════════════════════════════════════════════════════════════════════════

    /// <summary>MRA levels (SAP workflow) and the normalisers of mra.py.</summary>
    public static class MraNorm
    {
        public static readonly IReadOnlyDictionary<string, string> Levels = new Dictionary<string, string>
        {
            ["1"] = "First Level", ["2"] = "Second Level", ["3"] = "Third Level", ["4"] = "Fourth Level", ["5"] = "final",
            ["6"] = "Discarded", ["7"] = "Self Reject", ["8"] = "Forced Approved", ["9"] = "Draft", ["X"] = "Reject",
        };
        public static readonly string[] LevelOrder = { "1", "2", "3", "4", "5", "6", "7", "8", "9", "X" };

        private static readonly Dictionary<string, string> Desc = BuildDesc();
        private static Dictionary<string, string> BuildDesc()
        {
            var d = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var (k, v) in Levels) d[LettersOnly(v)] = k;
            foreach (var (k, v) in new[] { ("final", "5"), ("finallevel", "5"), ("reject", "X"), ("rejected", "X"), ("selfreject", "7"),
                                           ("forcedapproved", "8"), ("forceapproved", "8"), ("discarded", "6"), ("draft", "9") }) d[k] = v;
            return d;
        }

        private static string LettersOnly(string s) => Regex.Replace(s.ToLowerInvariant(), "[^a-z]", "");
        private static readonly Regex WholeNumber = new(@"^\d+(\.0+)?$", RegexOptions.Compiled);

        /// <summary>'1', 1, '01', 'First Level', ' first level ', 'x', 'Reject' → '1' / 'X'; unknown → null.</summary>
        public static string? Level(string? v)
        {
            var s = (v ?? "").Trim();
            if (s.Length == 0) return null;
            if (WholeNumber.IsMatch(s))
            {
                var n = ((long)double.Parse(s, CultureInfo.InvariantCulture)).ToString(CultureInfo.InvariantCulture);
                return Levels.ContainsKey(n) ? n : null;
            }
            if (s.ToUpperInvariant() == "X") return "X";
            return Desc.TryGetValue(LettersOnly(s), out var k) ? k : null;
        }

        /// <summary>'3' / 'Released' → '3'; '1', '2', 'Unreleased', 'Created' → '1' / '2'; closed / deleted → 'C'; '' → null.</summary>
        public static string? PoStatus(string? v)
        {
            var s = (v ?? "").Trim().ToLowerInvariant();
            if (s.Length == 0) return null;
            if (WholeNumber.IsMatch(s))
            {
                var n = ((long)double.Parse(s, CultureInfo.InvariantCulture)).ToString(CultureInfo.InvariantCulture);
                return n is "1" or "2" or "3" ? n : null;
            }
            return s switch
            {
                "released" or "release" or "rel" => "3",
                "unreleased" or "un-released" or "not released" or "notreleased" or "revoked" or "release revoked" => "2",
                "created" or "new" => "1",
                "closed" or "short closed" or "shortclosed" or "deleted" or "cancelled" or "canceled" => "C",
                _ => null
            };
        }

        /// <summary>Generic article: digits only, last 10 (SAP sends 18 with leading zeros).</summary>
        public static string Ga(string? v)
        {
            var s = Regex.Replace(v ?? "", @"\D", "");
            return s.Length > 10 ? s[^10..] : s;
        }

        public static string Colour(string? v) => (v ?? "").Trim().ToUpperInvariant();

        /// <summary>'2026-11-02', '2026-11-02T00:00:00', '02.11.2026', '02/11/2026', 20261102 → '2026-11-02'; else ''.</summary>
        public static string Date(string? v)
        {
            var s = (v ?? "").Trim();
            if (s.Length == 0) return "";
            int y, mo, d;
            var m = Regex.Match(s, @"^(\d{4})-(\d{1,2})-(\d{1,2})");
            if (m.Success) (y, mo, d) = (int.Parse(m.Groups[1].Value), int.Parse(m.Groups[2].Value), int.Parse(m.Groups[3].Value));
            else
            {
                m = Regex.Match(s, @"^(\d{1,2})[./-](\d{1,2})[./-](\d{4})$");
                if (m.Success) (d, mo, y) = (int.Parse(m.Groups[1].Value), int.Parse(m.Groups[2].Value), int.Parse(m.Groups[3].Value));
                else
                {
                    m = Regex.Match(s, @"^(\d{4})(\d{2})(\d{2})$");
                    if (!m.Success) return "";
                    (y, mo, d) = (int.Parse(m.Groups[1].Value), int.Parse(m.Groups[2].Value), int.Parse(m.Groups[3].Value));
                }
            }
            if (y < 1 || mo < 1 || mo > 12 || d < 1 || d > DateTime.DaysInMonth(y, mo)) return "";
            return $"{y:D4}-{mo:D2}-{d:D2}";
        }

        /// <summary>Pieces: '6,000' → 6000 (rounded half to even, as Python round); negative / not a number → null.</summary>
        public static long? Qty(string? v)
        {
            var s = (v ?? "").Replace(",", "").Trim();
            if (!double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var q) || double.IsNaN(q) || double.IsInfinity(q)) return null;
            return q >= 0 ? (long)Math.Round(q, MidpointRounding.ToEven) : null;
        }

        private static readonly Regex NoRe = new("^[0-9A-Za-z_-]{1,30}$", RegexOptions.Compiled);
        /// <summary>MRA / PO numbers: letters, digits, - and _ only (they are shown on the page).</summary>
        public static bool ValidNo(string? v) => NoRe.IsMatch(v ?? "");

        /// <summary>Leading zeros dropped ("0000900201" → "900201", "000" → "0").</summary>
        public static string NoZeros(string v) { var t = v.TrimStart('0'); return t.Length == 0 ? "0" : t; }

        /// <summary>occurred_at → comparable instant ('…Z' / '+05:30' honoured; no zone = server local time). Null if not readable.</summary>
        public static DateTimeOffset? Ts(string? v)
        {
            var s = (v ?? "").Trim();
            if (!Regex.IsMatch(s, @"^\d{4}-\d{2}-\d{2}([T ]\d{2}:\d{2}(:\d{2}(\.\d+)?)?)?([Zz]|[+-]\d{2}:?\d{2})?$")) return null;
            return DateTimeOffset.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var d) ? d : null;
        }

        /// <summary>True only when both times are readable and <paramref name="newer"/> is before <paramref name="current"/>.</summary>
        public static bool Older(string? newer, string? current)
        {
            var a = Ts(newer); var b = Ts(current);
            return a != null && b != null && a.Value < b.Value;
        }
    }

    /// <summary>The tool decision SAP / .NET sends with an MRA (shown only; never used to count).</summary>
    public sealed class MraDecisionInfo
    {
        public string? Decision { get; set; }
        public string? DecisionReason { get; set; }
        public string? DecisionId { get; set; }
        public string? DecisionAt { get; set; }
        public long? DecisionMaxPerm { get; set; }
        public string? DecisionUrl { get; set; }
        /// <summary>The working JSON of the decision (max 60 KB).</summary>
        public JsonObject? Working { get; set; }

        public bool Any => Decision != null || DecisionReason != null || DecisionId != null || DecisionAt != null
                           || DecisionMaxPerm != null || DecisionUrl != null || Working != null;

        /// <summary>Copies every field that is set in <paramref name="src"/> (kept until a new value is sent).</summary>
        public void MergeFrom(MraDecisionInfo? src)
        {
            if (src == null) return;
            if (src.Decision != null) Decision = src.Decision;
            if (src.DecisionReason != null) DecisionReason = src.DecisionReason;
            if (src.DecisionId != null) DecisionId = src.DecisionId;
            if (src.DecisionAt != null) DecisionAt = src.DecisionAt;
            if (src.DecisionMaxPerm != null) DecisionMaxPerm = src.DecisionMaxPerm;
            if (src.DecisionUrl != null) DecisionUrl = src.DecisionUrl;
            if (src.Working != null) Working = (JsonObject)src.Working.DeepClone();
        }

        public MraDecisionInfo Clone() { var c = new MraDecisionInfo(); c.MergeFrom(this); return c; }

        /// <summary>mra.py decision_fields: what was sent (names not case sensitive, '_' / spaces ignored).</summary>
        public static MraDecisionInfo FromPick(Func<string[], JsonNode?> pick)
        {
            var o = new MraDecisionInfo();
            string? S(params string[] n) => MraMsgReader.Text(pick(n));
            var d = S("decision", "tool_decision", "verdict", "tool_verdict");
            if (d != null) o.Decision = Cut(d.Trim(), 60);
            var v = S("decision_reason", "verdict_reason", "reason");
            if (v != null) o.DecisionReason = Cut(v.Trim(), 1000);
            v = S("decision_id", "verdict_id", "decision_no", "request_id");
            if (v != null) o.DecisionId = Cut(v.Trim(), 60);
            v = S("decision_at", "decided_at", "verdict_at");
            if (v != null) o.DecisionAt = Cut(v.Trim(), 40);
            v = S("max_permissible", "max_permissible_qty", "maxpermissible");
            if (v != null && MraNorm.Qty(v) is long q) o.DecisionMaxPerm = q;
            v = S("decision_url", "verdict_url");
            if (v != null && Regex.IsMatch(v.Trim(), @"^https?://[^\s""'<>]{1,500}$")) o.DecisionUrl = v.Trim();
            if (pick(new[] { "working", "decision_working" }) is JsonObject w && w.ToJsonString().Length <= 60000)
                o.Working = (JsonObject)w.DeepClone();
            return o;
        }

        private static string Cut(string s, int n) => s.Length > n ? s[..n] : s;
    }

    /// <summary>Field access on a received JSON message, as mra.py <c>_pick</c>.</summary>
    public static class MraMsgReader
    {
        private static string Key(string k) => Regex.Replace(k.ToLowerInvariant(), "[^a-z0-9]", "");

        /// <summary>The first of <paramref name="names"/> present with a value that is not null / "".</summary>
        public static JsonNode? Pick(JsonObject msg, params string[] names)
        {
            var low = new Dictionary<string, JsonNode?>(StringComparer.Ordinal);
            foreach (var (k, v) in msg) low.TryAdd(Key(k), v);
            foreach (var n in names)
                if (low.TryGetValue(Key(n), out var v) && v != null && !(v is JsonValue jv && jv.TryGetValue<string>(out var s) && s.Length == 0))
                    return v;
            return null;
        }

        /// <summary>A JSON value as text (strings as they are, numbers / booleans as written); objects / arrays → their JSON.</summary>
        public static string? Text(JsonNode? n)
        {
            if (n == null) return null;
            if (n is JsonValue v)
            {
                if (v.TryGetValue<string>(out var s)) return s;
                if (v.GetValueKind() is JsonValueKind.True) return "True";
                if (v.GetValueKind() is JsonValueKind.False) return "False";
                return v.ToJsonString();
            }
            return n.ToJsonString();
        }
    }

    /// <summary>A live MRA change from SAP, normalised (mra.py parse_mra_message).</summary>
    public sealed class MraMessage
    {
        public string Mra { get; set; } = "";
        public string Level { get; set; } = "";
        public string RawLevel { get; set; } = "";
        public string? Ga { get; set; }
        public string? Colour { get; set; }
        public long? Qty { get; set; }
        /// <summary>Delivery date yyyy-MM-dd.</summary>
        public string? Dd { get; set; }
        public string? PoNo { get; set; }
        public string? PoStatus { get; set; }
        public string? OccurredAt { get; set; }
        /// <summary>Who made the change in SAP (user ID / name), optional.</summary>
        public string? ActionBy { get; set; }
        /// <summary>Free text sent with the change (e.g. why it was rejected), optional.</summary>
        public string? Remark { get; set; }
        public string? MsgId { get; set; }
        public MraDecisionInfo? DecisionInfo { get; set; }

        public static (MraMessage? Msg, string? Error) Parse(JsonNode? raw)
        {
            if (raw is not JsonObject msg) return (null, "message is not an object");
            string? T(params string[] n) => MraMsgReader.Text(MraMsgReader.Pick(msg, n));
            var mra = (T("mra_no", "mra", "mra_number", "document_number", "doc_no", "document_no") ?? "").Trim();
            if (mra.Length == 0) return (null, "MRA number missing");
            var rawLevel = T("level", "level_code", "level_desc", "level_description", "short_description");
            var level = MraNorm.Level(rawLevel);
            if (level == null) return (null, $"level not recognised: {Repr(rawLevel)}");
            if (!MraNorm.ValidNo(mra)) return (null, $"MRA number not valid: {Repr(Cut(mra, 40))}");
            var actionBy = (T("action_by", "changed_by", "action_user") ?? "").Trim();
            var remark = (T("remark", "remarks", "comment") ?? "").Trim();
            var o = new MraMessage
            {
                Mra = MraNorm.NoZeros(mra), Level = level, RawLevel = Cut(rawLevel ?? "", 40),
                ActionBy = actionBy.Length > 0 ? Cut(actionBy, 80) : null, Remark = remark.Length > 0 ? Cut(remark, 500) : null
            };
            var ga = MraNorm.Ga(T("generic_article", "generic_material", "generic", "ga"));
            if (ga.Length > 0) o.Ga = ga;
            var col = MraNorm.Colour(T("colour", "color"));
            if (col.Length > 0) o.Colour = col;
            var q = T("qty", "quantity", "total_po_qty", "total_qty");
            if (q != null)
            {
                if (MraNorm.Qty(q) is not long qq) return (null, $"qty not a number: {Repr(q)}");
                o.Qty = qq;
            }
            var dd = T("delivery_date", "del_date", "deldate");
            if (dd != null)
            {
                var d = MraNorm.Date(dd);
                if (d.Length == 0) return (null, $"delivery date not recognised: {Repr(dd)}");
                o.Dd = d;
            }
            var po = (T("po_no", "po_number", "purchase_order") ?? "").Trim();
            if (po.Length > 0)
            {
                if (!MraNorm.ValidNo(po)) return (null, $"PO number not valid: {Repr(Cut(po, 40))}");
                o.PoNo = MraNorm.NoZeros(po);
            }
            var ps = T("po_status", "release_status", "status");
            if (ps != null && MraNorm.PoStatus(ps) is { } st) o.PoStatus = st;
            var at = T("occurred_at", "changed_at", "timestamp", "event_time");
            if (!string.IsNullOrEmpty(at)) o.OccurredAt = at;
            var mid = T("msg_id", "message_id", "id");
            if (!string.IsNullOrEmpty(mid)) o.MsgId = mid;
            var dec = MraDecisionInfo.FromPick(n => MraMsgReader.Pick(msg, n));
            if (dec.Any) o.DecisionInfo = dec;
            return (o, null);
        }

        internal static string Repr(string? s) => s == null ? "None" : "'" + s + "'";
        internal static string Cut(string s, int n) => s.Length > n ? s[..n] : s;
    }

    /// <summary>A live PO change from SAP, normalised (mra.py parse_po_message).</summary>
    public sealed class PoMessage
    {
        public string Po { get; set; } = "";
        /// <summary>'1' / '2' / '3' / 'C' (closed / deleted).</summary>
        public string Status { get; set; } = "";
        public string RawStatus { get; set; } = "";
        public string? Mra { get; set; }
        public string? Ga { get; set; }
        public string? Colour { get; set; }
        public long? Qty { get; set; }
        public string? Dd { get; set; }
        public string? OccurredAt { get; set; }
        public string? MsgId { get; set; }

        public static (PoMessage? Msg, string? Error) Parse(JsonNode? raw)
        {
            if (raw is not JsonObject msg) return (null, "message is not an object");
            string? T(params string[] n) => MraMsgReader.Text(MraMsgReader.Pick(msg, n));
            var po = (T("po_no", "po_number", "purchase_order", "po") ?? "").Trim();
            if (po.Length == 0) return (null, "PO number missing");
            if (!MraNorm.ValidNo(po)) return (null, $"PO number not valid: {MraMessage.Repr(MraMessage.Cut(po, 40))}");
            var rawSt = T("po_status", "release_status", "status");
            var st = MraNorm.PoStatus(rawSt);
            if (st == null) return (null, $"PO status not recognised: {MraMessage.Repr(rawSt)}");
            var o = new PoMessage { Po = MraNorm.NoZeros(po), Status = st, RawStatus = MraMessage.Cut(rawSt ?? "", 40) };
            var mra = (T("mra_no", "mra", "document_number") ?? "").Trim();
            if (mra.Length > 0)
            {
                if (!MraNorm.ValidNo(mra)) return (null, $"MRA number not valid: {MraMessage.Repr(MraMessage.Cut(mra, 40))}");
                o.Mra = MraNorm.NoZeros(mra);
            }
            var ga = MraNorm.Ga(T("generic_article", "generic_material", "generic", "ga"));
            if (ga.Length > 0) o.Ga = ga;
            var col = MraNorm.Colour(T("colour", "color"));
            if (col.Length > 0) o.Colour = col;
            var q = T("qty", "open_qty", "pending_qty", "quantity");
            if (q != null)
            {
                if (MraNorm.Qty(q) is not long qq) return (null, $"qty not a number: {MraMessage.Repr(q)}");
                o.Qty = qq;
            }
            var dd = T("delivery_date", "del_date");
            if (dd != null)
            {
                var d = MraNorm.Date(dd);
                if (d.Length == 0) return (null, $"delivery date not recognised: {MraMessage.Repr(dd)}");
                o.Dd = d;
            }
            var at = T("occurred_at", "changed_at", "timestamp");
            if (!string.IsNullOrEmpty(at)) o.OccurredAt = at;
            var mid = T("msg_id", "message_id", "id");
            if (!string.IsNullOrEmpty(mid)) o.MsgId = mid;
            return (o, null);
        }
    }

    /// <summary>A PO of an MRA in the morning file (header sheet "Purchase order" / "Status" / "Po Date").</summary>
    public sealed class MraFilePo
    {
        public string Po { get; set; } = "";
        public string? Status { get; set; }
        public string? PoDate { get; set; }
    }

    /// <summary>One MRA of the morning file (all item lines / sites of the MRA added up).</summary>
    public sealed class MraFileRecord
    {
        public string Ga { get; set; } = "";
        public string Colour { get; set; } = "";
        public long Qty { get; set; }
        public string Dd { get; set; } = "";
        public string Level { get; set; } = "";
        public List<MraFilePo> Pos { get; set; } = new();
        public string? CreatedBy { get; set; }
        public string? CreatedById { get; set; }
        public string? CreatedOn { get; set; }
        public string? Inspiration { get; set; }
        public string? GaDesc { get; set; }
        public int Lines { get; set; }
        public MraDecisionInfo? DecisionInfo { get; set; }
    }

    /// <summary>One stored feed record: a live message as received (also a bad one).</summary>
    public sealed class MraFeedEvent
    {
        public long Seq { get; set; }
        public DateTimeOffset At { get; set; }
        /// <summary>"mra" or "po".</summary>
        public string Kind { get; set; } = "mra";
        public string? By { get; set; }
        public string? Error { get; set; }
        public MraMessage? Mra { get; set; }
        public PoMessage? Po { get; set; }
        /// <summary>The message as received (JSON text).</summary>
        public string? Raw { get; set; }
    }

    public sealed class MraHistoryStep
    {
        /// <summary>Who made the change in SAP (action_by of the MRA message).</summary>
        public string? By { get; set; }
        /// <summary>Remark sent with the change.</summary>
        public string? Remark { get; set; }
        public DateTimeOffset At { get; set; }
        public string? From { get; set; }
        public string To { get; set; } = "";
        public long? Qty { get; set; }
        public long Seq { get; set; }
    }

    /// <summary>Current state of one MRA after the replay.</summary>
    public sealed class MraState
    {
        public string Ga { get; set; } = "";
        public string Colour { get; set; } = "";
        public long Qty { get; set; }
        public string Dd { get; set; } = "";
        /// <summary>Null only for an MRA created live without a level yet (never kept: a message always has a level).</summary>
        public string? Level { get; set; }
        /// <summary>"file" (morning file) or "live" (changed by a live message).</summary>
        public string Src { get; set; } = "file";
        public long? FileQty { get; set; }
        /// <summary>Level 5 reached by a LIVE change (counted until the next morning's files).</summary>
        public bool L5Live { get; set; }
        /// <summary>Was at level 8 (Forced Approved) — 8 → 1 is the exception approved by Commercial.</summary>
        public bool Was8 { get; set; }
        public bool CreatedLive { get; set; }
        public List<MraHistoryStep> History { get; set; } = new();
        public string? PoNo { get; set; }
        public string? PoStatus { get; set; }
        public string? PoSrc { get; set; }
        /// <summary>PO status sent WITH the MRA change (decides whether a live level 5 counts).</summary>
        public string? MraPoStatus { get; set; }
        public DateTimeOffset? ChangedAt { get; set; }
        /// <summary>Who made the last live change in SAP (action_by; kept until a message sends another).</summary>
        public string? ActionBy { get; set; }
        /// <summary>Remark of the last live change that had one.</summary>
        public string? Remark { get; set; }
        public string? OccurredAt { get; set; }
        public List<MraFilePo> Pos { get; set; } = new();
        public string? CreatedBy { get; set; }
        public string? CreatedById { get; set; }
        public string? CreatedOn { get; set; }
        public string? Inspiration { get; set; }
        public string? GaDesc { get; set; }
        public int Lines { get; set; }
        public MraDecisionInfo Decision { get; set; } = new();
    }

    public sealed class PoState
    {
        public string? Mra { get; set; }
        public string? Ga { get; set; }
        public string? Colour { get; set; }
        public long? Qty { get; set; }
        public string? Dd { get; set; }
        public string? OccurredAt { get; set; }
        public string? Status { get; set; }
        public DateTimeOffset? ChangedAt { get; set; }
        public List<MraHistoryStep> History { get; set; } = new();
    }

    /// <summary>What happened to one feed message.</summary>
    public sealed class MraFeedResult
    {
        public long Seq { get; set; }
        /// <summary>applied | no change | duplicate | older | parked | stored (PO message).</summary>
        public string Outcome { get; set; } = "";
        public string? Reason { get; set; }
        public string? Mra { get; set; }
        public string? Po { get; set; }
        public string? From { get; set; }
        public string? To { get; set; }
    }

    public sealed class MraReplayResult
    {
        public Dictionary<string, MraState> Mras { get; } = new(StringComparer.Ordinal);
        public Dictionary<string, PoState> Pos { get; } = new(StringComparer.Ordinal);
        public List<MraFeedResult> Results { get; } = new();
    }

    public static class MraFeedOutcomes
    {
        public const string Applied = "applied", NoChange = "no change", Duplicate = "duplicate", Older = "older", Parked = "parked", Stored = "stored";
        /// <summary>v7: stored in the pending table — applied when today's MRA file is uploaded.</summary>
        public const string Pending = "pending";
        public const string PendingReason = "stored; applied when today's MRA file is uploaded";
    }

    /// <summary>mra.py replay(): the morning file + live messages in arrival order → current state.</summary>
    public static class MraReplay
    {
        public static MraReplayResult Run(IReadOnlyDictionary<string, MraFileRecord>? snapshot, IEnumerable<MraFeedEvent>? events)
        {
            var res = new MraReplayResult();
            foreach (var (k, r) in snapshot ?? new Dictionary<string, MraFileRecord>())
            {
                var s = new MraState
                {
                    Ga = r.Ga, Colour = r.Colour, Qty = r.Qty, Dd = r.Dd, Level = r.Level, Src = "file", FileQty = r.Qty,
                    L5Live = false, Was8 = r.Level == "8", Pos = r.Pos.Select(p => new MraFilePo { Po = p.Po, Status = p.Status, PoDate = p.PoDate }).ToList(),
                    CreatedBy = r.CreatedBy, CreatedById = r.CreatedById, CreatedOn = r.CreatedOn, Inspiration = r.Inspiration, GaDesc = r.GaDesc,
                    Lines = r.Lines, Decision = r.DecisionInfo?.Clone() ?? new MraDecisionInfo()
                };
                var withPo = r.Pos.Where(p => !string.IsNullOrEmpty(p.Po)).ToList();
                if (r.Pos.Count > 0)
                {
                    s.PoNo = string.Join(", ", withPo.Select(p => p.Po.TrimStart('0')));
                    var sts = r.Pos.Select(p => p.Status).Where(x => !string.IsNullOrEmpty(x)).ToList();
                    s.PoStatus = sts.Distinct().Count() == 1 ? sts[0] : sts.Count > 0 ? "mixed" : null;
                    s.PoSrc = "file";
                }
                res.Mras[k] = s;
            }
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var ev in events ?? Array.Empty<MraFeedEvent>())
            {
                var isMra = ev.Kind == "mra";
                if (ev.Error != null || (isMra ? ev.Mra == null : ev.Po == null))
                {
                    res.Results.Add(new MraFeedResult { Seq = ev.Seq, Outcome = MraFeedOutcomes.Parked, Reason = ev.Error ?? "empty message" });
                    continue;
                }
                var mid = isMra ? ev.Mra!.MsgId : ev.Po!.MsgId;
                if (!string.IsNullOrEmpty(mid))
                {
                    // duplicate = same msg_id for the same MRA (PO) number: a msg_id SAP reuses for another MRA / PO is applied
                    var num = isMra ? ev.Mra!.Mra : ev.Po!.Po;
                    if (!seen.Add(ev.Kind + "\u0001" + num + "\u0001" + mid))
                    {
                        res.Results.Add(new MraFeedResult { Seq = ev.Seq, Outcome = MraFeedOutcomes.Duplicate,
                            Reason = "same message id received before for " + (isMra ? "MRA " : "PO ") + num });
                        continue;
                    }
                }
                if (isMra) ApplyMra(res, ev, ev.Mra!);
                else ApplyPo(res, ev, ev.Po!);
            }
            return res;
        }

        private static void ApplyMra(MraReplayResult res, MraFeedEvent ev, MraMessage m)
        {
            if (!res.Mras.TryGetValue(m.Mra, out var cur))
            {
                var miss = new List<string>();
                if (m.Ga == null) miss.Add("ga");
                if (m.Colour == null) miss.Add("colour");
                if (m.Qty == null) miss.Add("qty");
                if (m.Dd == null) miss.Add("dd");
                if (miss.Count > 0)
                {
                    res.Results.Add(new MraFeedResult { Seq = ev.Seq, Outcome = MraFeedOutcomes.Parked, Reason = "new MRA without " + string.Join(", ", miss) });
                    return;
                }
                cur = new MraState { Ga = m.Ga!, Colour = m.Colour!, Qty = m.Qty!.Value, Dd = m.Dd!, Level = null, Src = "live", CreatedLive = true };
                res.Mras[m.Mra] = cur;
            }
            if (m.OccurredAt != null && cur.OccurredAt != null && MraNorm.Older(m.OccurredAt, cur.OccurredAt))
            {
                res.Results.Add(new MraFeedResult { Seq = ev.Seq, Outcome = MraFeedOutcomes.Older, Reason = "older than the change already applied" });
                return;
            }
            var before = (cur.Level, cur.Qty, cur.Ga, cur.Colour, cur.Dd, cur.MraPoStatus, cur.Decision.Decision, cur.Decision.DecisionId);
            if (m.Ga != null) cur.Ga = m.Ga;
            if (m.Colour != null) cur.Colour = m.Colour;
            if (m.Qty != null) cur.Qty = m.Qty.Value;
            if (m.Dd != null) cur.Dd = m.Dd;
            var prev = cur.Level;
            cur.Level = m.Level;
            if (m.Level == "8") cur.Was8 = true;
            if (m.Level == "5" && prev != "5") cur.L5Live = true;
            if (m.Level != "5") cur.L5Live = false;
            if (m.PoNo != null) { cur.PoNo = m.PoNo; cur.PoSrc = "live"; }
            if (m.PoStatus != null) { cur.PoStatus = m.PoStatus; cur.MraPoStatus = m.PoStatus; cur.PoSrc = "live"; }
            cur.Src = "live";
            cur.ChangedAt = ev.At;
            if (m.OccurredAt != null) cur.OccurredAt = m.OccurredAt;
            cur.Decision.MergeFrom(m.DecisionInfo);
            var after = (cur.Level, cur.Qty, cur.Ga, cur.Colour, cur.Dd, cur.MraPoStatus, cur.Decision.Decision, cur.Decision.DecisionId);
            if (m.ActionBy != null) cur.ActionBy = m.ActionBy;
            if (m.Remark != null) cur.Remark = m.Remark;
            cur.History.Add(new MraHistoryStep { At = ev.At, From = prev, To = m.Level, Qty = cur.Qty, Seq = ev.Seq, By = m.ActionBy, Remark = m.Remark });
            res.Results.Add(new MraFeedResult
            {
                Seq = ev.Seq, Outcome = after != before ? MraFeedOutcomes.Applied : MraFeedOutcomes.NoChange, Mra = m.Mra, From = prev, To = m.Level
            });
        }

        private static void ApplyPo(MraReplayResult res, MraFeedEvent ev, PoMessage m)
        {
            res.Pos.TryGetValue(m.Po, out var cur);
            if (m.OccurredAt != null && cur?.OccurredAt != null && MraNorm.Older(m.OccurredAt, cur.OccurredAt))
            {
                res.Results.Add(new MraFeedResult { Seq = ev.Seq, Outcome = MraFeedOutcomes.Older, Reason = "older than the change already applied" });
                return;
            }
            if (cur == null) res.Pos[m.Po] = cur = new PoState();
            var prev = cur.Status;
            if (m.Mra != null) cur.Mra = m.Mra;
            if (m.Ga != null) cur.Ga = m.Ga;
            if (m.Colour != null) cur.Colour = m.Colour;
            if (m.Qty != null) cur.Qty = m.Qty;
            if (m.Dd != null) cur.Dd = m.Dd;
            if (m.OccurredAt != null) cur.OccurredAt = m.OccurredAt;
            cur.Status = m.Status;
            cur.ChangedAt = ev.At;
            cur.History.Add(new MraHistoryStep { At = ev.At, From = prev, To = m.Status, Qty = cur.Qty, Seq = ev.Seq });
            if (m.Mra != null && res.Mras.TryGetValue(m.Mra, out var mr))
            {
                mr.PoNo = m.Po; mr.PoStatus = m.Status; mr.PoSrc = "live";
            }
            res.Results.Add(new MraFeedResult { Seq = ev.Seq, Outcome = MraFeedOutcomes.Stored, Po = m.Po, From = prev, To = m.Status });
        }
    }
}
