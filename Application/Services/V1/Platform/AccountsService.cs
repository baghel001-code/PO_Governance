using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Application.DTOs.Platform;
using Application.Interfaces.V1;
using Domain.Constants;
using Domain.Entities.AppSettings;
using Domain.Entities.Common;
using Domain.Entities.Store;
using Domain.Exceptions;
using Microsoft.Extensions.Options;

namespace Application.Services.V1.Platform
{
    /// <summary>
    /// Port of accounts.py <c>Accounts</c>: users and sessions are held in memory (documents "users" /
    /// "sessions" in the store), reloaded when another process changed the users document
    /// (doc_stamp), and written back whole (the store only writes the rows that changed).
    /// </summary>
    public sealed class AccountsService : IAccountsService
    {
        public static readonly Regex EmailRe = new(@"[A-Za-z0-9._%+'-]+@[A-Za-z0-9.-]+\.[A-Za-z]{2,}", RegexOptions.Compiled);
        private static readonly Regex EmailFull = new(@"^(?:[A-Za-z0-9._%+'-]+@[A-Za-z0-9.-]+\.[A-Za-z]{2,})\z", RegexOptions.Compiled);

        private readonly IStore _store;
        private readonly PoGovSettings _cfg;
        private readonly IAppClock _clock;
        private readonly IAuditService _audit;
        private readonly IPasswordHasher _hasher;
        private readonly IScrSource _scr;

        private readonly SemaphoreSlim _gate = new(1, 1);   // accounts.py self.lock
        private Dictionary<string, AppUser> _users = new();
        private Dictionary<string, SessionRecord> _sessions = new();
        private long _mtime;
        private volatile bool _loaded;
        private sealed record ScopeCacheEntry(string? Ver, ScopeIndex Idx);
        private volatile ScopeCacheEntry? _scopeCache;   // swapped as one reference

        public AccountsService(IStore store, IOptions<PoGovSettings> cfg, IAppClock clock, IAuditService audit,
                               IPasswordHasher hasher, IScrSource scr)
        {
            _store = store;
            _cfg = cfg.Value;
            _clock = clock;
            _audit = audit;
            _hasher = hasher;
            _scr = scr;
        }

        // ── helpers ─────────────────────────────────────────────────

        public static string NormEmail(string? e) => (e ?? "").Trim().ToLowerInvariant();

        /// <summary>sukhdev.singh@x.com → Sukhdev Singh</summary>
        public static string NameFromEmail(string email)
        {
            var local = NormEmail(email).Split('@')[0];
            var parts = Regex.Split(local, @"[._\d-]+").Where(p => p.Length > 0);
            var s = string.Join(" ", parts.Select(Capitalize));
            return s.Length > 0 ? s : email;
        }

        private static string Capitalize(string p) => p.Length == 0 ? p : char.ToUpperInvariant(p[0]) + p[1..].ToLowerInvariant();

        private static List<string> OrderRoles(IEnumerable<string> roles)
        {
            var set = new HashSet<string>(roles);
            return Roles.All.Where(set.Contains).ToList();
        }

        public string DefaultPassword => _cfg.DefaultPasswordOrDefault;

        private double Now => _clock.Epoch();

        private async Task<IDisposable> LockAsync(CancellationToken ct)
        {
            await _gate.WaitAsync(ct);
            return new Releaser(_gate);
        }

        private sealed class Releaser : IDisposable
        {
            private SemaphoreSlim? _s;
            public Releaser(SemaphoreSlim s) => _s = s;
            public void Dispose() => Interlocked.Exchange(ref _s, null)?.Release();
        }

        // ── persistence ─────────────────────────────────────────────

        private static Dictionary<string, T> ReadMap<T>(JsonNode? node)
        {
            var d = new Dictionary<string, T>();
            if (node is JsonObject o)
                foreach (var kv in o)
                {
                    var v = PyJson.FromNode<T>(kv.Value);
                    if (v != null) d[kv.Key] = v;
                }
            return d;
        }

        private static JsonObject WriteMap<T>(Dictionary<string, T> map)
        {
            var o = new JsonObject();
            foreach (var kv in map) o[kv.Key] = PyJson.ToNode(kv.Value);
            return o;
        }

        private async Task<long> UsersStampAsync(CancellationToken ct)
        {
            try { return await _store.DocStampAsync(DocNames.Users, ct); }
            catch (Exception) { return 0; }
        }

        public async Task InitializeAsync(CancellationToken ct = default)
        {
            if (_loaded) return;
            using (await LockAsync(ct))
            {
                if (_loaded) return;
                _users = ReadMap<AppUser>(await _store.GetDocAsync(DocNames.Users, ct));
                _mtime = await UsersStampAsync(ct);
                _sessions = ReadMap<SessionRecord>(await _store.GetDocAsync(DocNames.Sessions, ct));
                await SeedAdminLockedAsync(ct);
                _loaded = true;
            }
        }

        /// <summary>Picks up changes made outside this process (e.g. a reset-admin tool).</summary>
        private async Task MaybeReloadAsync(CancellationToken ct)
        {
            var m = await UsersStampAsync(ct);
            if (m != _mtime)
            {
                using (await LockAsync(ct))
                {
                    var node = await _store.GetDocAsync(DocNames.Users, ct);
                    var fresh = ReadMap<AppUser>(node);
                    if (node is JsonObject o && o.Count > 0) _users = fresh;
                    _mtime = m;
                }
            }
        }

        private async Task SaveUsersLockedAsync(CancellationToken ct)
        {
            await _store.PutDocAsync(DocNames.Users, WriteMap(_users), ct);
            _mtime = await UsersStampAsync(ct);
        }

        private Task SaveSessionsLockedAsync(CancellationToken ct) => _store.PutDocAsync(DocNames.Sessions, WriteMap(_sessions), ct);

        private async Task SeedAdminLockedAsync(CancellationToken ct)
        {
            var admin = NormEmail(_cfg.AdminEmailOrDefault);
            if (!_users.TryGetValue(admin, out var u))
            {
                _users[admin] = NewUser(admin, NameFromEmail(admin), new[] { Roles.Admin, Roles.Planner }, "system");
                await SaveUsersLockedAsync(ct);
            }
            else if (!u.Roles.Contains(Roles.Admin))
            {
                u.Roles = OrderRoles(u.Roles.Concat(new[] { Roles.Admin, Roles.Planner }));
                await SaveUsersLockedAsync(ct);
            }
        }

        private AppUser NewUser(string email, string? name, IEnumerable<string> roles, string by, string source = "manual") => new()
        {
            Email = email,
            Name = string.IsNullOrEmpty(name) ? NameFromEmail(email) : name,
            Roles = OrderRoles(roles),
            Active = true,
            Pw = _hasher.Hash(DefaultPassword),
            MustChange = true,
            Failed = 0,
            LockedUntil = 0,
            CreatedAt = _clock.NowIso,
            CreatedBy = by,
            Source = source,
            LastLogin = null
        };

        // ── users ───────────────────────────────────────────────────

        public async Task<IReadOnlyList<AppUser>> GetUsersAsync(CancellationToken ct = default)
        {
            await InitializeAsync(ct);
            using (await LockAsync(ct)) return _users.Values.Select(u => u.Clone()).ToList();
        }

        public async Task<AppUser?> GetUserAsync(string email, CancellationToken ct = default)
        {
            await InitializeAsync(ct);
            using (await LockAsync(ct)) return _users.TryGetValue(NormEmail(email), out var u) ? u.Clone() : null;
        }

        public UserPublicDto Public(AppUser u) => new()
        {
            Email = u.Email, Name = u.Name, Roles = u.Roles.ToList(), Active = u.Active, MustChange = u.MustChange,
            CreatedAt = u.CreatedAt, CreatedBy = u.CreatedBy, Source = u.Source, LastLogin = u.LastLogin,
            LockedUntil = u.LockedUntil, Locked = u.LockedUntil > Now
        };

        public async Task<AppUser> AddUserAsync(string email, string name, IEnumerable<string> roles, string by, string source = "manual", CancellationToken ct = default)
        {
            await InitializeAsync(ct);
            email = NormEmail(email);
            if (!EmailFull.IsMatch(email)) throw new AppValidationException($"Not a valid email: {email}");
            var rl = (roles ?? Enumerable.Empty<string>()).Where(r => Roles.All.Contains(r)).ToList();
            if (rl.Count == 0) throw new AppValidationException("Pick at least one role");
            AppUser created;
            using (await LockAsync(ct))
            {
                if (_users.ContainsKey(email)) throw new AppValidationException($"{email} already exists");
                created = _users[email] = NewUser(email, name, rl, by, source);
                await SaveUsersLockedAsync(ct);
            }
            await _audit.AppendAsync(by, "user-add", $"{email} roles={string.Join(",", rl)}", ct);
            return created.Clone();
        }

        public async Task<AppUser> UpdateUserAsync(string email, JsonObject changes, string by, CancellationToken ct = default)
        {
            await InitializeAsync(ct);
            email = NormEmail(email);
            AppUser result;
            using (await LockAsync(ct))
            {
                if (!_users.TryGetValue(email, out var u)) throw new NotFoundException("No such user");
                var me = email == NormEmail(by);
                var nw = u.Clone();
                if (changes.ContainsKey("name"))
                {
                    var n = PyJson.Str(changes["name"]).Trim();
                    n = PlatformCore.Trunc(n, 80);
                    nw.Name = n.Length > 0 ? n : u.Name;
                }
                if (changes.ContainsKey("roles"))
                {
                    var asked = PyJson.Truthy(changes["roles"]) ? changes.GetStrList("roles") : new List<string>();
                    var roles = Roles.All.Where(asked.Contains).ToList();
                    if (roles.Count == 0) throw new AppValidationException("A user needs at least one role");
                    if (me && u.Roles.Contains(Roles.Admin) && !roles.Contains(Roles.Admin))
                        throw new AppValidationException("You can't remove your own admin role");
                    nw.Roles = roles;
                }
                if (changes.ContainsKey("active"))
                {
                    var active = PyJson.Truthy(changes["active"]);
                    if (me && !active) throw new AppValidationException("You can't deactivate yourself");
                    nw.Active = active;
                }
                if (changes.GetTruthy("unlock"))
                {
                    nw.Failed = 0;
                    nw.LockedUntil = 0;
                }
                var admins = _users.Count(kv =>
                {
                    var x = kv.Key == email ? nw : kv.Value;
                    return x.Active && x.Roles.Contains(Roles.Admin);
                });
                if (admins == 0) throw new AppValidationException("At least one active admin must remain");
                _users[email] = nw;
                if (!nw.Active) await EndSessionsLockedAsync(email, null, ct);
                await SaveUsersLockedAsync(ct);
                result = nw.Clone();
            }
            await _audit.AppendAsync(by, "user-update", $"{email} {PyJson.Dumps(changes, true)}", ct);
            return result;
        }

        public async Task ResetPasswordAsync(string email, string by, CancellationToken ct = default)
        {
            await InitializeAsync(ct);
            email = NormEmail(email);
            using (await LockAsync(ct))
            {
                if (!_users.TryGetValue(email, out var u)) throw new NotFoundException("No such user");
                u.Pw = _hasher.Hash(DefaultPassword);
                u.MustChange = true;
                u.Failed = 0;
                u.LockedUntil = 0;
                await SaveUsersLockedAsync(ct);
                await EndSessionsLockedAsync(email, null, ct);
            }
            await _audit.AppendAsync(by, "password-reset", email, ct);
        }

        public async Task DeleteUserAsync(string email, string by, CancellationToken ct = default)
        {
            await InitializeAsync(ct);
            email = NormEmail(email);
            using (await LockAsync(ct))
            {
                if (!_users.Remove(email)) throw new NotFoundException("No such user");
                await SaveUsersLockedAsync(ct);
                await EndSessionsLockedAsync(email, null, ct);
            }
            await _audit.AppendAsync(by, "user-delete", email, ct);
        }

        public string? ValidateNewPassword(string email, string pw)
        {
            var n = _cfg.PasswordMinLengthOrDefault;
            var len = pw.Count(c => !char.IsLowSurrogate(c));   // Python len() counts code points
            if (len < n) return $"Password must be at least {n} characters";
            if (pw == DefaultPassword) return "Choose a password different from the default";
            var lower = pw.ToLowerInvariant();
            if (lower == email.ToLowerInvariant() || lower == email.Split('@')[0].ToLowerInvariant())
                return "Password can't be your email or user name";
            if (!(Regex.IsMatch(pw, "[A-Za-z]") && Regex.IsMatch(pw, @"\d")))
                return "Use at least one letter and one number";
            return null;
        }

        public async Task<string?> ChangePasswordAsync(string email, string oldPassword, string newPassword, CancellationToken ct = default)
        {
            await InitializeAsync(ct);
            email = NormEmail(email);
            using (await LockAsync(ct))
            {
                if (!_users.TryGetValue(email, out var u) || !_hasher.Verify(oldPassword, u.Pw))
                    return "Current password is incorrect";
                var err = ValidateNewPassword(email, newPassword);
                if (err != null) return err;
                u.Pw = _hasher.Hash(newPassword);
                u.MustChange = false;
                await SaveUsersLockedAsync(ct);
            }
            await _audit.AppendAsync(email, "password-change", "", ct);
            return null;
        }

        // ── login / sessions ────────────────────────────────────────

        public async Task<(string? Token, string? Error)> LoginAsync(string email, string password, string ip, CancellationToken ct = default)
        {
            await InitializeAsync(ct);
            email = NormEmail(email);
            await MaybeReloadAsync(ct);
            var maxFail = _cfg.MaxFailedLoginsOrDefault;
            var lockMin = _cfg.LockoutMinutesOrDefault;
            var audits = new List<(string, string, string)>();
            string? token = null, error = null;
            using (await LockAsync(ct))
            {
                if (!_users.TryGetValue(email, out var u) || !u.Active)
                {
                    audits.Add((email.Length > 0 ? email : "?", "login-fail", $"unknown or inactive user ip={ip}"));
                    error = "Email or password is incorrect";
                }
                else if (u.LockedUntil > Now)
                {
                    var mins = (int)Math.Floor((u.LockedUntil - Now) / 60) + 1;
                    error = $"Too many wrong attempts. Try again in {mins} minute(s), or ask the admin to unlock you.";
                }
                else if (!_hasher.Verify(password, u.Pw))
                {
                    u.Failed += 1;
                    error = "Email or password is incorrect";
                    if (u.Failed >= maxFail)
                    {
                        u.LockedUntil = Now + lockMin * 60;
                        u.Failed = 0;
                        error = $"Too many wrong attempts — locked for {lockMin} minutes.";
                        audits.Add((email, "locked", $"ip={ip}"));
                    }
                    await SaveUsersLockedAsync(ct);
                    audits.Add((email, "login-fail", $"ip={ip}"));
                }
                else
                {
                    u.Failed = 0;
                    u.LockedUntil = 0;
                    u.LastLogin = _clock.NowIso;
                    await SaveUsersLockedAsync(ct);
                    token = NewToken();
                    var now = Now;
                    _sessions[token] = new SessionRecord { Email = email, Created = now, Seen = now, Ip = ip };
                    PruneSessionsLocked();
                    await SaveSessionsLockedAsync(ct);
                    audits.Add((email, "login", $"ip={ip}"));
                }
            }
            foreach (var (w, a, d) in audits) await _audit.AppendAsync(w, a, d, ct);
            return (token, error);
        }

        /// <summary>Python secrets.token_urlsafe(32).</summary>
        private static string NewToken() =>
            Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');

        private void PruneSessionsLocked()
        {
            var idle = _cfg.SessionIdleSeconds;
            var now = Now;
            foreach (var t in _sessions.Where(kv => now - kv.Value.Seen > idle).Select(kv => kv.Key).ToList())
                _sessions.Remove(t);
        }

        public async Task<AppUser?> SessionUserAsync(string? token, CancellationToken ct = default)
        {
            if (string.IsNullOrEmpty(token)) return null;
            await InitializeAsync(ct);
            var idle = _cfg.SessionIdleSeconds;
            await MaybeReloadAsync(ct);
            using (await LockAsync(ct))
            {
                if (!_sessions.TryGetValue(token, out var s)) return null;
                var now = Now;
                if (now - s.Seen > idle)
                {
                    _sessions.Remove(token);
                    await SaveSessionsLockedAsync(ct);
                    return null;
                }
                if (!_users.TryGetValue(s.Email, out var u) || !u.Active) return null;
                if (now - s.Seen > 300)
                {
                    s.Seen = now;   // persist "last seen" at most every 5 min
                    await SaveSessionsLockedAsync(ct);
                }
                else s.Seen = now;
                return u.Clone();
            }
        }

        public async Task LogoutAsync(string token, CancellationToken ct = default)
        {
            await InitializeAsync(ct);
            SessionRecord? s;
            using (await LockAsync(ct))
            {
                _sessions.Remove(token, out s);
                await SaveSessionsLockedAsync(ct);
            }
            if (s != null) await _audit.AppendAsync(s.Email, "logout", "", ct);
        }

        public async Task EndSessionsAsync(string email, string? keep = null, CancellationToken ct = default)
        {
            await InitializeAsync(ct);
            using (await LockAsync(ct)) await EndSessionsLockedAsync(email, keep, ct);
        }

        private async Task EndSessionsLockedAsync(string email, string? keep, CancellationToken ct)
        {
            foreach (var t in _sessions.Where(kv => kv.Value.Email == email && kv.Key != keep).Select(kv => kv.Key).ToList())
                _sessions.Remove(t);
            await SaveSessionsLockedAsync(ct);
        }

        // ── SCR products → users, scope, approvers ──────────────────

        public async Task<ScrMapping> GetMappingAsync(CancellationToken ct = default) =>
            PyJson.FromNode<ScrMapping>(await _store.GetDocAsync(DocNames.ScrMapping, ct) as JsonObject) ?? new ScrMapping();

        public async Task SaveMappingAsync(ScrMapping mapping, string by, CancellationToken ct = default)
        {
            await InitializeAsync(ct);
            var node = PyJson.ToObject(mapping);
            // drop nulls the admin didn't send ({"columns":…} only) so the stored doc looks like v1.1.4's
            foreach (var k in node.Where(kv => kv.Value == null).Select(kv => kv.Key).ToList()) node.Remove(k);
            using (await LockAsync(ct)) await _store.PutDocAsync(DocNames.ScrMapping, node, ct);
            _scopeCache = null;
            await _audit.AppendAsync(by, "scr-mapping", PyJson.Dumps(node, true), ct);
        }

        private static IEnumerable<string> Emails(object? v) =>
            EmailRe.Matches(PyJson.StrOrEmpty(v)).Select(m => NormEmail(m.Value));

        private static string Lower(string s) => s.ToLowerInvariant();

        public ScrAnalysis AnalyseScr(IReadOnlyList<SheetRow> rows)
        {
            if (rows == null || rows.Count == 0) return new ScrAnalysis();
            var headers = rows[0].Keys.ToList();
            foreach (var r in rows.Take(50))
                foreach (var h in r.Keys)
                    if (!headers.Contains(h)) headers.Add(h);
            var sample = rows.Take(500).ToList();
            var cols = new List<ScrColumnInfo>();
            foreach (var h in headers)
            {
                var vals = sample.Select(r => r[h]).Where(v => PyJson.StrOrEmpty(v).Trim().Length > 0).ToList();
                if (vals.Count == 0) continue;
                var hits = vals.Count(v => EmailRe.IsMatch(PyJson.Str(v)));
                if ((double)hits / vals.Count < 0.5) continue;
                var hl = Regex.Replace(Lower(h), "[^a-z]", " ");
                var lvl = Regex.Replace(Lower(h), "[^a-z0-9]", "");
                string guess;
                if (lvl is "level1" or "l1" or "level1email" or "level1emailid") guess = Roles.Buyer;        // Level 1: Buyer / Sr Buyer / ACM / BFT
                else if (lvl is "level2" or "l2" or "level2email" or "level2emailid") guess = Roles.Buyer;   // Level 2: own MCs, no approvals yet
                else if (lvl is "level3" or "l3" or "level3email" or "level3emailid") guess = Roles.CatHead; // Level 3: Watch List approvals park here
                else if (hl.Contains("division") || Regex.IsMatch(hl, @"\bdiv\b") || hl.Trim() == "dh") guess = "skip";
                else if (hl.Contains("buyer") || hl.Contains("buying")) guess = Roles.Buyer;
                else if (hl.Contains("category") || Regex.IsMatch(hl, @"\bcat\b") || hl.Trim() == "ch") guess = Roles.CatHead;
                else guess = "skip";
                string? nameCol = null;
                var bas = guess == Roles.Buyer ? "buyer" : "cat";
                foreach (var h2 in headers)
                {
                    var h2l = Lower(h2);
                    if (h2 != h && h2l.Contains("name") && h2l.Contains(bas)) { nameCol = h2; break; }
                }
                var distinct = vals.SelectMany(Emails).Distinct().Count();
                var ex = PyJson.Str(vals[0]);
                cols.Add(new ScrColumnInfo { Column = h, Guess = guess, NameColumn = nameCol, DistinctEmails = distinct, Example = PlatformCore.Trunc(ex, 80) });
            }

            string? Find(params string[] cands)
            {
                foreach (var c in cands)
                    foreach (var h in headers)
                        if (Regex.Replace(Lower(h), "[^a-z]", "") == c) return h;
                return null;
            }
            return new ScrAnalysis
            {
                Columns = cols, BrandCol = Find("brandtype", "brand"), McCol = Find("mccd", "mccode", "mc"), MrpCol = Find("mrp", "mrpvalue")
            };
        }

        public Dictionary<string, ScrPerson> ScrPeople(IReadOnlyList<SheetRow> rows, ScrMapping mapping)
        {
            var info = AnalyseScr(rows);
            var rolesByCol = new Dictionary<string, string>();
            foreach (var c in info.Columns) rolesByCol[c.Column] = c.Guess;
            if (mapping.Columns != null)
                foreach (var kv in mapping.Columns) rolesByCol[kv.Key] = kv.Value;   // admin's choices win
            var nameCols = info.Columns.ToDictionary(c => c.Column, c => c.NameColumn);
            var b = !string.IsNullOrEmpty(mapping.BrandCol) ? mapping.BrandCol : info.BrandCol;
            var m = !string.IsNullOrEmpty(mapping.McCol) ? mapping.McCol : info.McCol;
            var p = !string.IsNullOrEmpty(mapping.MrpCol) ? mapping.MrpCol : info.MrpCol;
            var people = new Dictionary<string, ScrPerson>();
            foreach (var r in rows)
            {
                var key = string.Join("|",
                    b != null ? PlatformCore.NKeyCell(r[b]) : "",
                    m != null ? PlatformCore.NKeyCell(r[m]) : "",
                    p != null ? PlatformCore.NKeyCell(r[p]) : "");
                foreach (var (col, role) in rolesByCol)
                {
                    if (role != Roles.Buyer && role != Roles.CatHead) continue;
                    var emails = Emails(r[col]).ToList();
                    nameCols.TryGetValue(col, out var nc);
                    var names = nc != null
                        ? Regex.Split(PyJson.StrOrEmpty(r[nc]), "[,;/]").Select(x => x.Trim()).ToList()
                        : new List<string>();
                    for (var i = 0; i < emails.Count; i++)
                    {
                        var e = emails[i];
                        if (!people.TryGetValue(e, out var d)) people[e] = d = new ScrPerson();
                        d.Roles.Add(role);
                        d.Keys.Add(key);
                        if (role == Roles.CatHead) d.CatheadKeys.Add(key);
                        if (d.Name.Length == 0 && i < names.Count && names[i].Length > 0) d.Name = names[i];
                    }
                }
            }
            return people;
        }

        public async Task<ScrSyncResult> SyncFromScrAsync(IReadOnlyList<SheetRow> rows, string by, CancellationToken ct = default)
        {
            await InitializeAsync(ct);
            var people = ScrPeople(rows, await GetMappingAsync(ct));
            var res = new ScrSyncResult { Found = people.Count };
            using (await LockAsync(ct))
            {
                foreach (var (email, d) in people)
                {
                    if (!_users.TryGetValue(email, out var u))
                    {
                        _users[email] = NewUser(email, d.Name, d.Roles, by, "scr");
                        res.Added.Add(email);
                    }
                    else
                    {
                        var newRoles = OrderRoles(u.Roles.Concat(d.Roles));
                        if (!newRoles.SequenceEqual(u.Roles))
                        {
                            u.Roles = newRoles;
                            res.Updated.Add(email);
                        }
                    }
                }
                if (res.Added.Count > 0 || res.Updated.Count > 0) await SaveUsersLockedAsync(ct);
            }
            _scopeCache = null;
            await _audit.AppendAsync(by, "scr-sync", $"added={res.Added.Count} updated={res.Updated.Count}", ct);
            return res;
        }

        public async Task<ScopeIndex> ScopeIndexAsync(CancellationToken ct = default)
        {
            var (ver, rows) = await _scr.GetScrRowsAsync(ct);
            var cache = _scopeCache;
            if (ver != null && cache != null && cache.Ver == ver) return cache.Idx;
            var people = ScrPeople(rows ?? Array.Empty<SheetRow>(), await GetMappingAsync(ct));
            var approvers = new Dictionary<string, List<string>>();
            foreach (var (e, d) in people)
                foreach (var k in d.CatheadKeys)
                {
                    if (!approvers.TryGetValue(k, out var l)) approvers[k] = l = new List<string>();
                    l.Add(e);
                }
            var idx = new ScopeIndex(people, approvers);
            _scopeCache = new ScopeCacheEntry(ver, idx);
            return idx;
        }
    }
}
