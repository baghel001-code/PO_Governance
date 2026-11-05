using DecisionEngine.Common;
using DecisionEngine.Model;

namespace DecisionEngine.Mra
{
    /// <summary>Brand · MC · MRP · Flag (+ description) of a Generic Article, from the Article Master only.</summary>
    public sealed class MraArticleInfo
    {
        public string Brand { get; set; } = "";
        public string Div { get; set; } = "";
        public string McCd { get; set; } = "";
        public string McDesc { get; set; } = "";
        public string Mrp { get; set; } = "";
        public string Flag { get; set; } = "";
        public string Desc { get; set; } = "";
    }

    /// <summary>Where an MRA is and whether it takes capacity (app/mra.js classify).</summary>
    public sealed class MraClass
    {
        public bool Counted { get; set; }
        /// <summary>Delivery month MM.yyyy.</summary>
        public string Mon { get; set; } = "";
        public MraArticleInfo? Info { get; set; }
        public bool Single { get; set; }
        public string Queue { get; set; } = "";
        public string Owner { get; set; } = "";
        public string OwnerEmail { get; set; } = "";
        public string Why { get; set; } = "";
        public bool SetAside { get; set; }
        public bool DdPassed { get; set; }
        /// <summary>SCR products Level 2 buyer = Division Head (level 1 owner).</summary>
        public string DhEmail { get; set; } = "";
        /// <summary>SCR products Category Head (level 2 owner).</summary>
        public string ChEmail { get; set; } = "";
        public string L1Email { get; set; } = "";
    }

    public sealed class MraIncomingItem
    {
        /// <summary>"MRA" or "PO" (a live PO change, switch 2 ON).</summary>
        public string Type { get; set; } = "MRA";
        public string No { get; set; } = "";
        public string Mon { get; set; } = "";
        public long Qty { get; set; }
        public string? Level { get; set; }
        public bool Live { get; set; }
        public string? Status { get; set; }
    }

    /// <summary>What open MRAs (and live PO changes) take in one colour of one group, per delivery month (MM.yyyy).</summary>
    public sealed class MraIncoming
    {
        public Dictionary<string, long> ByMon { get; set; } = new(StringComparer.Ordinal);
        public List<MraIncomingItem> Items { get; set; } = new();
    }

    public sealed class PoEffectPart
    {
        public string Ga { get; set; } = "";
        public string Colour { get; set; } = "";
        public string Mon { get; set; } = "";
        public long Delta { get; set; }
        public MraArticleInfo? Info { get; set; }
        public bool Counted { get; set; }
        public string? Why { get; set; }
    }

    /// <summary>One PO with live messages: what it takes now minus what the morning F2 already has.</summary>
    public sealed class PoEffect
    {
        public string Po { get; set; } = "";
        public string Mra { get; set; } = "";
        public string MraNo { get; set; } = "";
        public string? Status { get; set; }
        public long Now { get; set; }
        public long Was { get; set; }
        public long Delta { get; set; }
        public List<PoEffectPart> Parts { get; set; } = new();
        public bool Known { get; set; }
        public string Ga { get; set; } = "";
        public string Colour { get; set; } = "";
        public string Mon { get; set; } = "";
        public MraArticleInfo? Info { get; set; }
        public bool Counted { get; set; }
        public string Why { get; set; } = "";
        public DateTimeOffset? ChangedAt { get; set; }
    }

    /// <summary>The effect of one feed message on capacity, for "Today's changes" (app/mra.js effectOf).</summary>
    public sealed class MraEffect
    {
        public string Text { get; set; } = "";
        /// <summary>"dn" capacity goes down, "up" capacity comes back, "zr" no effect.</summary>
        public string Cls { get; set; } = "zr";
    }

    /// <summary>
    /// Port of app/mra.js (v1.1.7) — classification of every MRA, the capacity index used by the single concept
    /// engines, the live PO effects (switch 2) and the capacity effect of each feed message. One instance per
    /// (data snapshot, replayed MRA state, day, switch); build it with <see cref="Build"/>.
    /// </summary>
    public sealed class MraCapacity
    {
        public string Today { get; }
        public bool LivePo { get; }
        public MraReplayResult State { get; }
        public IReadOnlyList<(string No, MraState M, MraClass C)> Rows => _rows;
        public IReadOnlyList<PoEffect> PoEffects => _poEffects;

        private readonly EngineData _data;
        private readonly IReadOnlyDictionary<string, string> _owners;
        private readonly Dictionary<string, MraArticleInfo> _am;
        private readonly Dictionary<string, (string L1, string L2, string Ch)> _scr;
        private readonly List<(string No, MraState M, MraClass C)> _rows = new();
        private readonly List<PoEffect> _poEffects;
        private readonly Dictionary<string, (string Po, string? Status)> _gov;
        private readonly Dictionary<string, MraIncoming> _idx = new(StringComparer.Ordinal);

        private MraCapacity(EngineData data, MraReplayResult state, string today, bool livePo, IReadOnlyDictionary<string, string>? owners)
        {
            _data = data; State = state; Today = today; LivePo = livePo;
            _owners = owners ?? new Dictionary<string, string>();
            _am = AmMap(data);
            _scr = ScrMap(data);
            _poEffects = BuildPoEffects();
            _gov = new Dictionary<string, (string, string?)>(StringComparer.Ordinal);
            foreach (var e in _poEffects) if (e.MraNo.Length > 0 && e.Known) _gov[e.MraNo] = (e.Po, e.Status);
            BuildIndex();
        }

        /// <param name="today">yyyy-MM-dd (server date).</param>
        public static MraCapacity Build(EngineData data, MraReplayResult state, string today, bool livePo, IReadOnlyDictionary<string, string>? owners = null) =>
            new(data, state, today, livePo, owners);

        // ── helpers ────────────────────────────────────────────────────────
        public static string Nk(string? v)
        {
            var s = (v ?? "").Trim();
            if (s.EndsWith(".0", StringComparison.Ordinal)) s = s[..^2];
            return s.ToLowerInvariant();
        }
        public static string GKey(string? brand, string? mcCd, string? mrp, string? flag) => string.Join("|", Nk(brand), Nk(mcCd), Nk(mrp), Nk(flag));
        public static string GKey(MraArticleInfo i) => GKey(i.Brand, i.McCd, i.Mrp, i.Flag);
        /// <summary>yyyy-MM-dd → MM.yyyy.</summary>
        public static string MonKey(string? iso) { var p = (iso ?? "").Split('-'); return p.Length == 3 ? p[1] + "." + p[0] : ""; }
        private static readonly string[] Mn = { "Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec" };
        public static string DayLabel(string? iso)
        {
            var p = (iso ?? "").Split('-');
            return p.Length == 3 && int.TryParse(p[1], out var m) && m is >= 1 and <= 12 && int.TryParse(p[2], out var d) ? $"{d} {Mn[m - 1]} {p[0]}" : "—";
        }

        private static Dictionary<string, MraArticleInfo> AmMap(EngineData data) => data.GetOrBuild("mra.amMap", d =>
        {
            var out_ = new Dictionary<string, MraArticleInfo>(StringComparer.Ordinal);
            foreach (var r in d.ArtMaster ?? Array.Empty<SheetRow>())
            {
                var k = MraNorm.Ga(r.Col("Generic Material", "generic material", "GENERIC MATERIAL", "GenMat"));
                if (k.Length == 0 || out_.ContainsKey(k)) continue;
                out_[k] = new MraArticleInfo
                {
                    Brand = r.Col("Brand Type", "BRAND TYPE", "brand type"), Div = r.Col("Division", "DIV", "division", "div"),
                    McCd = r.Col("MC CD", "MC_CD", "mc cd", "MC Code"), McDesc = r.Col("MC DES", "MC DESC", "MC Desc", "mc desc", "MC Description", "MC Name"),
                    Mrp = r.Col("MRP", "mrp", "MRP value", "MRP Value"), Flag = r.Col("Flag (BOGO/NON-BOGO)", "BOGO/NON-BOGO", "Flag", "flag", "Article Type", "Bogo/Non-Bogo"),
                    Desc = r.Col("ART DESC", "Art Desc", "art desc", "Article Description")
                };
            }
            return out_;
        });

        /// <summary>Article Master info of a Generic Article (10 digits), or null when it is not in the Article Master.</summary>
        public MraArticleInfo? Article(string ga) => _am.TryGetValue(MraNorm.Ga(ga), out var i) ? i : null;

        private static Dictionary<string, (string, string, string)> ScrMap(EngineData data) => data.GetOrBuild("mra.scr", d =>
        {
            var m = new Dictionary<string, (string, string, string)>(StringComparer.Ordinal);
            foreach (var r in d.CatHeadMap ?? Array.Empty<SheetRow>())
            {
                var k = string.Join("|", Nk(r.Col("Brand Type", "BRAND TYPE")), Nk(r.Col("MC CD", "MC_CD")), Nk(r.Col("MRP")));
                if (!m.ContainsKey(k))
                    m[k] = (r.Col("Level_1_Buyer_Email_ID").Trim().ToLowerInvariant(), r.Col("Level_2_Buyer_Email_ID").Trim().ToLowerInvariant(),
                            r.Col("Category_Email_ID").Trim().ToLowerInvariant());
            }
            return m;
        });

        private (string L1, string L2, string Ch) Owners(MraArticleInfo i) =>
            _scr.TryGetValue(string.Join("|", Nk(i.Brand), Nk(i.McCd), Nk(i.Mrp)), out var o) ? o : ("", "", "");

        /// <summary>Display name of a queue owner (user name when known, else the part before @); "" for vacant.</summary>
        public string Who(string? email)
        {
            if (string.IsNullOrEmpty(email) || email.Contains("vacant", StringComparison.OrdinalIgnoreCase)) return "";
            return _owners.TryGetValue(email, out var n) && !string.IsNullOrEmpty(n) ? n : email.Split('@')[0];
        }

        private bool IsSingle(string ga, string colour) => ResolveIndex.RouteRequest(_data, ga, colour).Engine == "B";

        // ── classification of one MRA ──────────────────────────────────────
        public MraClass Classify(string no, MraState m)
        {
            var info = Article(m.Ga);
            var c = new MraClass { Mon = MonKey(m.Dd), Info = info };
            var lv = m.Level;
            var own = info != null ? Owners(info) : (L1: "", L2: "", Ch: "");
            c.DhEmail = own.L2; c.ChEmail = own.Ch; c.L1Email = own.L1;
            switch (lv)
            {
                case "1": c.Queue = m.Was8 ? "Division Head – exception" : "Division Head"; c.Owner = Who(own.L2) is { Length: > 0 } w1 ? w1 : (m.Was8 ? "Division Head" : ""); c.OwnerEmail = own.L2; break;
                case "2": c.Queue = "Category Head"; c.Owner = Who(own.Ch); c.OwnerEmail = own.Ch; break;
                case "3": c.Queue = "Level 3"; break;
                case "4": c.Queue = "Commercial"; c.Owner = "Commercial"; break;
                case "5": c.Queue = "Final – PO created"; break;
                case "8": c.Queue = "Commercial (exception)"; c.Owner = "Commercial"; break;
                case "7": c.Queue = "Self rejected"; break;
                case "X": c.Queue = "Rejected"; break;
                case "6": c.Queue = "Discarded"; break;
                case "9": c.Queue = "Draft"; break;
            }
            if (info == null) { c.Why = "Not in Article Master"; c.SetAside = true; return c; }
            c.Single = IsSingle(m.Ga, m.Colour);
            if (!c.Single) { c.Why = "Multi concept – not counted (later upgrade)"; c.SetAside = true; return c; }
            if (string.CompareOrdinal(m.Dd, Today) < 0) { c.Why = "Delivery date passed (" + DayLabel(m.Dd) + ")"; c.DdPassed = true; return c; }
            var gov = LivePo && _gov != null && _gov.TryGetValue(no, out var g) ? g : default;
            var governed = gov.Po != null;
            if (lv is "1" or "2" or "4")
            {
                if (governed) { c.Why = "PO " + gov.Po + " has a live change – the PO counts instead"; return c; }
                c.Counted = true; c.Why = "Open MRA"; return c;
            }
            if (lv == "5")
            {
                if (governed) { c.Why = "PO " + gov.Po + " has a live change – the PO counts instead"; return c; }
                if (m.L5Live && m.MraPoStatus is "1" or "2" or "C")
                {
                    c.Why = "Final today – PO " + (m.PoNo ?? "") + " unreleased (status " + (m.MraPoStatus == "C" ? "closed" : m.MraPoStatus) + "): not counted";
                    return c;
                }
                if (m.L5Live)
                {
                    c.Counted = true;
                    c.Why = "Final today – counted until tomorrow's files" + (m.MraPoStatus == "3" ? " (PO released)" : m.MraPoStatus != null ? "" : " (no PO status sent)");
                    return c;
                }
                c.Why = "Final – the released PO counts from the released-PO file (unreleased: not counted)"; return c;
            }
            c.Why = lv == "8" ? "Exception waiting for Commercial – not counted"
                : lv is "7" or "X" ? "Cancelled / rejected – capacity returned"
                : lv == "3" ? "Level 3 – not counted" : "Not counted";
            return c;
        }

        // ── released-PO file: pending qty per PO number, per article + colour + month line ──
        private sealed class F2Po { public long Qty; public Dictionary<string, (string Ga, string Colour, string Mon, double Qty)> Lines = new(StringComparer.Ordinal); }

        private Dictionary<string, F2Po> F2ByPo() => _data.GetOrBuild("mra.f2ByPo", d =>
        {
            var out_ = new Dictionary<string, F2Po>(StringComparer.Ordinal);
            foreach (var r in d.F2 ?? Array.Empty<SheetRow>())
            {
                var po = r.Col("PO Number", "PO NO", "PO No", "PO_NUMBER", "po number", "PO number", "Purchase Order", "Purchase Order No", "Purchase Order Number",
                               "SAP PO", "SAP PO No", "Order Number", "Order No", "P.O. Number", "P.O No", "PO", "po no", "po").Trim().TrimStart('0');
                if (po.Length == 0) continue;
                var ga = MraNorm.Ga(r.Col("Generic Material", "GENERIC MATERIAL", "generic material"));
                var colour = r.Col("Color", "Colour", "COLOR", "COLOUR").Trim().ToUpperInvariant();
                var mon = r.Col("Delivery Month", "delivery month", "DEL MONTH").Trim();
                if (!out_.TryGetValue(po, out var o)) out_[po] = o = new F2Po();
                var q = Js.Num(r.Col("Pending Qty", "PENDING QTY", "pending qty"));
                var lk = ga + "|" + colour + "|" + mon;
                var l = o.Lines.TryGetValue(lk, out var x) ? x : (Ga: ga, Colour: colour, Mon: mon, Qty: 0d);
                o.Lines[lk] = (l.Ga, l.Colour, l.Mon, l.Qty + q);
                o.Qty += (long)Math.Round(q);
            }
            return out_;
        });

        private string MraOfPo(string po, PoState p)
        {
            if (p.Mra != null && State.Mras.ContainsKey(p.Mra)) return p.Mra;
            foreach (var (no, m) in State.Mras)
            {
                var pn = m.PoNo ?? "";
                if (pn.Length > 0 && pn.Split(',').Select(s => s.Trim()).Contains(po)) return no;
            }
            return "";
        }

        private List<PoEffect> BuildPoEffects()
        {
            var outList = new List<PoEffect>();
            if (!LivePo) return outList;
            var f2 = F2ByPo();
            foreach (var (po, p) in State.Pos)
            {
                f2.TryGetValue(po.TrimStart('0'), out var b);
                var mraNo = MraOfPo(po, p);
                State.Mras.TryGetValue(mraNo, out var mr);
                var lines = new List<(string Ga, string Colour, string Mon, double Qty)>();
                if (b != null) foreach (var l in b.Lines.Values) lines.Add((l.Ga, l.Colour, l.Mon, -l.Qty));
                var known = true;
                if (p.Status == "3")
                {
                    if (p.Qty == null && b != null)
                    {
                        foreach (var l in b.Lines.Values) lines.Add((l.Ga, l.Colour, l.Mon, l.Qty));
                    }
                    else
                    {
                        (string Ga, string Colour, string Mon, double Qty)? fl = b != null && b.Lines.Count > 0 ? b.Lines.Values.First() : null;
                        var ga = p.Ga ?? (mr?.Ga is { Length: > 0 } g1 ? g1 : null) ?? fl?.Ga ?? "";
                        var colour = p.Colour ?? (mr?.Colour is { Length: > 0 } c1 ? c1 : null) ?? fl?.Colour ?? "";
                        var mon = p.Dd != null ? MonKey(p.Dd) : mr != null && mr.Dd.Length > 0 ? MonKey(mr.Dd) : fl?.Mon ?? "";
                        long? q = p.Qty ?? mr?.Qty;
                        if (ga.Length == 0 || colour.Length == 0 || mon.Length == 0 || q == null) known = false;
                        else lines.Add((ga, colour, mon, q.Value));
                    }
                }
                var agg = new Dictionary<string, PoEffectPart>(StringComparer.Ordinal);
                var aggOrder = new List<string>();
                foreach (var l in lines)
                {
                    var k = l.Ga + "|" + l.Colour + "|" + l.Mon;
                    if (!agg.TryGetValue(k, out var a)) { agg[k] = a = new PoEffectPart { Ga = l.Ga, Colour = l.Colour, Mon = l.Mon }; aggOrder.Add(k); }
                    a.Delta += (long)Math.Round(l.Qty);
                }
                var parts = aggOrder.Select(k => agg[k]).ToList();
                foreach (var a in parts)
                {
                    var info = Article(a.Ga); a.Info = info;
                    if (info == null) a.Why = "Not in Article Master";
                    else if (!IsSingle(a.Ga, a.Colour)) a.Why = "Multi concept – not counted";
                    else if (a.Mon.Length == 0) a.Why = "No delivery month";
                    else a.Counted = true;
                }
                if (parts.Any(a => !a.Counted)) known = known && parts.All(a => a.Counted || a.Delta == 0);
                long now = p.Status == "3" ? (p.Qty ?? (b != null ? b.Qty : mr?.Qty ?? 0)) : 0;
                var f = parts.FirstOrDefault();
                outList.Add(new PoEffect
                {
                    Po = po, Mra = p.Mra ?? mraNo, MraNo = mraNo, Status = p.Status, Now = now, Was = b?.Qty ?? 0, Delta = now - (b?.Qty ?? 0),
                    Parts = parts, Known = known, Ga = f?.Ga ?? p.Ga ?? "", Colour = f?.Colour ?? p.Colour ?? "", Mon = f?.Mon ?? "", Info = f?.Info,
                    Counted = known && parts.Count > 0,
                    Why = known ? (parts.Count > 0 ? "" : "No change") : "Article / colour / month / qty not known (MRA keeps counting)",
                    ChangedAt = p.ChangedAt
                });
            }
            return outList;
        }

        // ── index: what counts per Brand·MC·MRP·Flag + colour, by delivery month ──
        private void BuildIndex()
        {
            foreach (var (no, m) in State.Mras)
            {
                var c = Classify(no, m);
                _rows.Add((no, m, c));
                if (!c.Counted) continue;
                var e = Entry(GKey(c.Info!) + "|" + Nk(m.Colour));
                e.ByMon[c.Mon] = (e.ByMon.TryGetValue(c.Mon, out var v) ? v : 0) + m.Qty;
                e.Items.Add(new MraIncomingItem { Type = "MRA", No = no, Mon = c.Mon, Qty = m.Qty, Level = m.Level, Live = m.Src == "live" });
            }
            foreach (var p in _poEffects)
            {
                if (!p.Counted) continue;
                foreach (var a in p.Parts)
                {
                    if (!a.Counted || a.Delta == 0) continue;
                    var e = Entry(GKey(a.Info!) + "|" + Nk(a.Colour));
                    e.ByMon[a.Mon] = (e.ByMon.TryGetValue(a.Mon, out var v) ? v : 0) + a.Delta;
                    e.Items.Add(new MraIncomingItem { Type = "PO", No = p.Po, Mon = a.Mon, Qty = a.Delta, Status = p.Status });
                }
            }
        }

        private MraIncoming Entry(string k)
        {
            if (!_idx.TryGetValue(k, out var e)) _idx[k] = e = new MraIncoming();
            return e;
        }

        /// <summary>Open MRAs (+ live PO changes) of one colour of one group, or null when nothing counts there.</summary>
        public MraIncoming? IncomingFor(string? brand, string? mcCd, string? mrp, string? flag, string? colour)
        {
            if (!_idx.TryGetValue(GKey(brand, mcCd, mrp, flag) + "|" + Nk(colour), out var e)) return null;
            return e.ByMon.Values.Sum() != 0 || e.Items.Count > 0 ? e : null;
        }

        /// <summary>Per colour (lower-case) of a group: {colour: {MM.yyyy: pcs}}.</summary>
        public Dictionary<string, Dictionary<string, long>> ByColourFor(string? mc, string? mrp, string? brand, string? flag)
        {
            var pre = GKey(brand, mc, mrp, flag) + "|";
            var out_ = new Dictionary<string, Dictionary<string, long>>(StringComparer.Ordinal);
            foreach (var (k, e) in _idx) if (k.StartsWith(pre, StringComparison.Ordinal)) out_[k[pre.Length..]] = e.ByMon;
            return out_;
        }

        public IReadOnlyDictionary<string, MraIncoming> Index => _idx;

        // ── capacity effect of each feed message (Today's changes) ─────────
        private static bool Countable(string? level, bool live5) => level is "1" or "2" or "4" || (level == "5" && live5);

        private sealed class Prev { public string? Level; public bool Live5; public bool Reached; public string? St; public long Qty; }

        /// <summary>The effect of each feed event in <paramref name="log"/> (arrival order), from the morning file level of each MRA.</summary>
        public List<MraEffect> Effects(IReadOnlyList<(MraFeedEvent Ev, MraFeedResult? R)> log)
        {
            var prev = new Dictionary<string, Prev>(StringComparer.Ordinal);
            foreach (var (no, m) in State.Mras)
                if (m.History.Count > 0 && m.History[0].From is { } from)
                    prev[no] = new Prev { Level = from, Live5 = false, Qty = m.FileQty ?? m.Qty };
            var list = new List<MraEffect>(log.Count);
            foreach (var (ev, r) in log) list.Add(EffectOf(ev, r, prev));
            return list;
        }

        private MraEffect EffectOf(MraFeedEvent ev, MraFeedResult? r, Dictionary<string, Prev> prevByMra)
        {
            var outcome = ev.Error != null ? MraFeedOutcomes.Parked : r?.Outcome;
            if (outcome == MraFeedOutcomes.Parked) return new MraEffect { Text = "set aside" };
            if (outcome is MraFeedOutcomes.Duplicate or MraFeedOutcomes.Older) return new MraEffect { Text = "ignored" };
            if (ev.Kind == "po")
            {
                if (!LivePo) return new MraEffect { Text = "stored only (PO queue off)" };
                var st = ev.Po?.Status;
                return st == "3" ? new MraEffect { Text = "counts as released PO", Cls = "dn" } : new MraEffect { Text = "not counted (status " + st + ")", Cls = "up" };
            }
            var m = ev.Mra!;
            if (!State.Mras.TryGetValue(m.Mra, out var cur)) return new MraEffect { Text = "—" };
            var c = Classify(m.Mra, cur);
            if (c.SetAside) return new MraEffect { Text = "not counted (" + c.Why + ")" };
            if (c.DdPassed) return new MraEffect { Text = "not counted (delivery date passed)" };
            prevByMra.TryGetValue(m.Mra, out var prev);
            var was = prev != null && Countable(prev.Level, prev.Live5);
            var wasQty = prev?.Qty ?? 0;
            var reached = m.Level == "5" && (prev == null || prev.Level != "5" || prev.Reached);
            var stt = m.PoStatus ?? (prev != null && prev.Level == "5" ? prev.St : "");
            var l5 = reached && !(stt is "1" or "2" or "C");
            var now = Countable(m.Level, l5);
            var q = m.Qty ?? prev?.Qty ?? cur.Qty;
            prevByMra[m.Mra] = new Prev { Level = m.Level, Live5 = l5, Reached = reached, St = stt, Qty = q };
            string N(long v) => Js.Fmt(Math.Abs(v));
            if (was && now)
                return q != wasQty
                    ? new MraEffect { Text = (q > wasQty ? "▼ " : "▲ ") + N(q - wasQty) + (q > wasQty ? " down" : " up"), Cls = q > wasQty ? "dn" : "up" }
                    : new MraEffect { Text = m.Level == "5" ? "no change (counted until tomorrow)" : "no change" };
            if (was && !now && m.Level == "5") return new MraEffect { Text = "▲ " + N(wasQty) + " up (PO unreleased)", Cls = "up" };
            if (!was && now) return new MraEffect { Text = "▼ " + N(q) + " down", Cls = "dn" };
            if (was && !now) return new MraEffect { Text = "▲ " + N(wasQty) + " up", Cls = "up" };
            return new MraEffect { Text = "no change" };
        }

        // ── tool decision as sent by SAP (nothing is worked out here) ───────
        public static readonly string[] DecisionGroups = { "Allow", "Reduce", "Watch List", "Hold", "Block", "No Plan", "Other", "Not sent" };

        public static string DecisionGroup(string? d)
        {
            var s = (d ?? "").ToLowerInvariant();
            if (s.Length == 0) return "Not sent";
            if (System.Text.RegularExpressions.Regex.IsMatch(s, "no ?plan")) return "No Plan";
            if (s.Contains("block")) return "Block";
            if (s.Contains("hold")) return "Hold";
            if (s.Contains("watch")) return "Watch List";
            if (s.Contains("reduce")) return "Reduce";
            if (s.Contains("allow") || s.Contains("retain") || s.Contains("within")) return "Allow";
            return "Other";
        }
    }
}
