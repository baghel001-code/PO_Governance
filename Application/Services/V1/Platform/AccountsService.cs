using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Application.DTOs.Platform;
using Application.Interfaces.V1;
using DecisionEngine.Model;
using Domain.Constants;
using Domain.Entities.AppSettings;
using Domain.Entities.Store;
using Domain.Exceptions;
using Microsoft.Extensions.Options;

namespace Application.Services.V1.Platform
{
    /// <summary>
    /// Port of accounts.py <c>Accounts</c>: users and sessions are held in memory (documents "users" /
    /// "sessions" in the store) and written back whole (the store only writes the rows that changed).
    /// Several API instances can share one store: before users or sessions are read or changed, the
    /// document's doc_stamp is checked and the document re-read when another instance wrote it; a sessions
    /// write applies this instance's changes (new / ended sessions) onto what is stored at that moment, so one
    /// instance never signs out the users of another.
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
        private readonly IHrmsClient _hrms;

        private readonly SemaphoreSlim _gate = new(1, 1);   // accounts.py self.lock
        private Dictionary<string, AppUser> _users = new();
        private Dictionary<string, SessionRecord> _sessions = new();
        private long _mtime;        // users doc_stamp this instance has seen (Unknown = re-read on the next check)
        private long _sessStamp;    // sessions doc_stamp this instance has seen
        private const long Unknown = long.MinValue;
        private volatile bool _loaded;
        private sealed record ScopeCacheEntry(string? Ver, ScopeIndex Idx);
        private volatile ScopeCacheEntry? _scopeCache;   // swapped as one reference

        public AccountsService(IStore store, IOptions<PoGovSettings> cfg, IAppClock clock, IAuditService audit,
                               IPasswordHasher hasher, IScrSource scr, IHrmsClient? hrms = null)
        {
            _hrms = hrms ?? NoHrms.Instance;
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

        /// <summary>HRMS employee codes: letters, digits and . _ - (up to 30); compared case-insensitively.</summary>
        private static readonly Regex EmpCodeRe = new(@"^[A-Za-z0-9._-]{1,30}\z", RegexOptions.Compiled);

        public static string NormEmpCode(string? code) => (code ?? "").Trim().ToUpperInvariant();

        private static bool SameCode(string? a, string? b) => a != null && b != null && NormEmpCode(a) == NormEmpCode(b);

        /// <summary>The registered login with this employee code (caller holds the lock).</summary>
        private AppUser? FindByEmpCodeLocked(string code) => _users.Values.FirstOrDefault(u => SameCode(u.EmpCode, code));

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
                _mtime = await UsersStampAsync(ct);
                _users = ReadMap<AppUser>(await _store.GetDocAsync(DocNames.Users, ct));
                _sessStamp = await SessionsStampAsync(ct);
                _sessions = ReadMap<SessionRecord>(await _store.GetDocAsync(DocNames.Sessions, ct));
                await SeedAdminLockedAsync(ct);
                _loaded = true;
            }
        }

        /// <summary>Picks up changes made outside this process (another API instance, a reset-admin tool).</summary>
        private async Task MaybeReloadAsync(CancellationToken ct)
        {
            var m = await UsersStampAsync(ct);
            if (m != _mtime)
                using (await LockAsync(ct))
                    await ReloadUsersLockedAsync(ct);
        }

        /// <summary>Re-reads the users when their doc_stamp changed since this instance last read them (caller holds the lock).</summary>
        private async Task ReloadUsersLockedAsync(CancellationToken ct)
        {
            var m = await UsersStampAsync(ct);
            if (m == _mtime) return;
            var node = await _store.GetDocAsync(DocNames.Users, ct);
            var fresh = ReadMap<AppUser>(node);
            if (node is JsonObject o && o.Count > 0) _users = fresh;
            _mtime = m;
        }

        private async Task SaveUsersLockedAsync(CancellationToken ct)
        {
            await _store.PutDocAsync(DocNames.Users, WriteMap(_users), ct);
            // Not the stamp read after the write: another instance may have written in between. Re-read on the next check.
            _mtime = Unknown;
        }

        // ── sessions (shared by every API instance on the store) ────

        private async Task<long> SessionsStampAsync(CancellationToken ct)
        {
            try { return await _store.DocStampAsync(DocNames.Sessions, ct); }
            catch (Exception) { return 0; }
        }

        /// <summary>
        /// Re-reads the sessions when another API instance wrote them since this one last read them (doc_stamp, as for the
        /// users), keeping the later "last seen" of a session both know (last seen is only persisted every 5 minutes).
        /// Caller holds the lock.
        /// </summary>
        private async Task RefreshSessionsLockedAsync(CancellationToken ct)
        {
            var stamp = await SessionsStampAsync(ct);
            if (stamp == _sessStamp) return;
            var fresh = ReadMap<SessionRecord>(await _store.GetDocAsync(DocNames.Sessions, ct));
            foreach (var (token, s) in fresh)
                if (_sessions.TryGetValue(token, out var mine) && mine.Seen > s.Seen) s.Seen = mine.Seen;
            _sessions = fresh;
            _sessStamp = stamp;
        }

        /// <summary>
        /// Writes the sessions with this instance's changes — <paramref name="upserted"/> (new or updated) and
        /// <paramref name="removed"/> tokens — applied onto the sessions stored right now, so sessions another instance
        /// created or ended meanwhile are kept / stay ended. Caller holds the lock.
        /// </summary>
        private async Task SaveSessionsLockedAsync(IEnumerable<string> upserted, IEnumerable<string> removed, CancellationToken ct)
        {
            var up = upserted.ToList();
            var rm = removed.ToList();
            var mine = _sessions;
            var stamp = await SessionsStampAsync(ct);
            if (stamp != _sessStamp)
            {
                await RefreshSessionsLockedAsync(ct);
                foreach (var t in rm) _sessions.Remove(t);
                foreach (var t in up)
                    if (mine.TryGetValue(t, out var s)) _sessions[t] = s;
            }
            await _store.PutDocAsync(DocNames.Sessions, WriteMap(_sessions), ct);
            _sessStamp = Unknown;   // see SaveUsersLockedAsync
        }

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
            // PoGovSettings:AdminEmpCode: the built-in admin signs in with that Employee ID and the HRMS password
            var code = NormEmpCode(_cfg.AdminEmpCode);
            u = _users[admin];
            if (code.Length > 0 && EmpCodeRe.IsMatch(code) && !u.IsEmployee && FindByEmpCodeLocked(code) == null)
            {
                u.EmpCode = code;
                u.MustChange = false;
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
            await MaybeReloadAsync(ct);
            using (await LockAsync(ct)) return _users.Values.Select(u => u.Clone()).ToList();
        }

        public async Task<AppUser?> GetUserAsync(string email, CancellationToken ct = default)
        {
            await InitializeAsync(ct);
            await MaybeReloadAsync(ct);
            using (await LockAsync(ct)) return _users.TryGetValue(NormEmail(email), out var u) ? u.Clone() : null;
        }

        public UserPublicDto Public(AppUser u) => new()
        {
            Email = u.Email, Name = u.Name, Roles = u.Roles.ToList(), Active = u.Active, MustChange = u.MustChange,
            CreatedAt = u.CreatedAt, CreatedBy = u.CreatedBy, Source = u.Source, LastLogin = u.LastLogin,
            LockedUntil = u.LockedUntil, Locked = u.LockedUntil > Now,
            EmpCode = u.EmpCode, LoginType = u.IsEmployee ? LoginTypes.Hrms : LoginTypes.Local,
            NeedsEmpCode = _cfg.HrmsLogin && !u.IsEmployee && !IsBreakGlassAdmin(u),
            Department = u.Hr?.Department, Designation = u.Hr?.Designation, Location = u.Hr?.Location,
            Mobile = u.Hr?.Mobile, Manager = u.Hr?.Manager, HrEmail = u.Hr?.Email, HrFetchedAt = u.Hr?.FetchedAt
        };

        /// <summary>The built-in admin while it may still sign in with email + local password (AllowAdminLocalLogin).</summary>
        private bool IsBreakGlassAdmin(AppUser u) =>
            _cfg.AllowAdminLocalLogin && !u.IsEmployee && u.Email == NormEmail(_cfg.AdminEmailOrDefault);

        public async Task<AppUser> AddUserAsync(string email, string name, IEnumerable<string> roles, string by, string source = "manual", CancellationToken ct = default)
        {
            await InitializeAsync(ct);
            email = NormEmail(email);
            if (!EmailFull.IsMatch(email)) throw new AppValidationException($"Not a valid email: {email}");
            var rl = (roles ?? Enumerable.Empty<string>()).Where(r => Roles.All.Contains(r)).ToList();
            if (rl.Count == 0) throw new AppValidationException("Pick at least one role");
            if (_cfg.HrmsLogin)
                throw new AppValidationException("Users sign in with their Employee ID and HRMS password: add them by Employee ID (fetched from HRMS), not by email.");
            AppUser created;
            using (await LockAsync(ct))
            {
                await ReloadUsersLockedAsync(ct);
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
                await ReloadUsersLockedAsync(ct);
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
                // QPS: deactivating or changing the roles signs the user out everywhere (the portal's cookie holds the roles)
                if (!nw.Active) await EndSessionsLockedAsync(email, null, SessionEndReasons.Disabled, ct);
                else if (!nw.Roles.SequenceEqual(u.Roles)) await EndSessionsLockedAsync(email, null, SessionEndReasons.Changed, ct);
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
                await ReloadUsersLockedAsync(ct);
                if (!_users.TryGetValue(email, out var u)) throw new NotFoundException("No such user");
                if (u.IsEmployee)
                    throw new AppValidationException($"{email} signs in with Employee ID {u.EmpCode} and the HRMS password — it is changed or unlocked in MyVishal (HRMS), not here.");
                u.Pw = _hasher.Hash(DefaultPassword);
                u.MustChange = true;
                u.Failed = 0;
                u.LockedUntil = 0;
                await SaveUsersLockedAsync(ct);
                await EndSessionsLockedAsync(email, null, SessionEndReasons.Password, ct);
            }
            await _audit.AppendAsync(by, "password-reset", email, ct);
        }

        public async Task DeleteUserAsync(string email, string by, CancellationToken ct = default)
        {
            await InitializeAsync(ct);
            email = NormEmail(email);
            using (await LockAsync(ct))
            {
                await ReloadUsersLockedAsync(ct);
                if (!_users.Remove(email)) throw new NotFoundException("No such user");
                await SaveUsersLockedAsync(ct);
                await EndSessionsLockedAsync(email, null, SessionEndReasons.Disabled, ct);
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
                await ReloadUsersLockedAsync(ct);
                if (_users.TryGetValue(email, out var emp) && emp.IsEmployee)
                    return "You sign in with your HRMS password: change it in MyVishal (HRMS).";
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

        /// <summary>
        /// Signs in. HRMS mode (<see cref="PoGovSettings.LoginMode"/>): <paramref name="login"/> is the Employee ID and the
        /// password is checked by HRMS — but only for an employee the admin registered here (active, not locked): for any
        /// other ID HRMS is not called at all, so an HRMS account alone never opens the tool. Email sign-in is refused, except
        /// for the built-in admin while <see cref="PoGovSettings.AllowAdminLocalLogin"/> is on. Local mode: email + the
        /// password kept here (v1.1.4). Every failure takes at least <see cref="PoGovSettings.LoginFailureMinMs"/>.
        /// </summary>
        public async Task<LoginResult> LoginAsync(string login, string password, string ip, CancellationToken ct = default)
        {
            await InitializeAsync(ct);
            var started = Stopwatch.GetTimestamp();
            var res = await LoginCoreAsync((login ?? "").Trim(), password ?? "", ip, ct);
            if (res.Token == null)
            {
                var rest = TimeSpan.FromMilliseconds(Math.Max(0, _cfg.LoginFailureMinMs)) - Stopwatch.GetElapsedTime(started);
                if (rest > TimeSpan.Zero) await Task.Delay(rest, ct);
            }
            return res;
        }

        public const string BadEmployeeLogin = "Employee ID or password is incorrect";

        private async Task<LoginResult> LoginCoreAsync(string login, string password, string ip, CancellationToken ct)
        {
            if (!_cfg.HrmsLogin) return await LocalLoginAsync(NormEmail(login), password, ip, ct);
            if (login.Contains('@'))
            {
                var email = NormEmail(login);
                if (_cfg.AllowAdminLocalLogin && email == NormEmail(_cfg.AdminEmailOrDefault)
                    && await GetUserAsync(email, ct) is { IsEmployee: false })
                    return await LocalLoginAsync(email, password, ip, ct);
                await _audit.AppendAsync(email.Length > 0 ? email : "?", "login-fail", $"email sign-in refused (HRMS sign-in) ip={ip}", ct);
                return LoginResult.Fail("Sign in with your Employee ID and HRMS password.");
            }
            return await EmployeeLoginAsync(NormEmpCode(login), password, ip, ct);
        }

        private async Task<LoginResult> EmployeeLoginAsync(string code, string password, string ip, CancellationToken ct)
        {
            await MaybeReloadAsync(ct);
            string? email = null, refused = null, lockedText = null;
            using (await LockAsync(ct))
            {
                await ReloadUsersLockedAsync(ct);
                var u = EmpCodeRe.IsMatch(code) ? FindByEmpCodeLocked(code) : null;
                if (u == null || !u.Active) refused = u == null ? "not a registered employee" : "inactive user " + u.Email;
                else if (u.LockedUntil > Now) lockedText = LockedText(u);
                else email = u.Email;
            }
            if (refused != null)
            {
                // not registered here (or deactivated): HRMS is not asked, whatever the password
                await _audit.AppendAsync(code.Length > 0 ? "emp:" + PlatformCore.Trunc(code, 30) : "?", "login-fail", $"{refused} ip={ip}", ct);
                return LoginResult.Fail(BadEmployeeLogin);
            }
            if (lockedText != null || email == null) return LoginResult.Fail(lockedText ?? BadEmployeeLogin);

            var r = await _hrms.VerifyPasswordAsync(code, password, ct);
            if (r.Status == HrmsStatus.Ok && !SameCode(r.Employee?.EmpCode, code)) r = r with { Status = HrmsStatus.Invalid, Message = "HRMS answered for another employee code" };
            switch (r.Status)
            {
                case HrmsStatus.Unavailable:
                    await _audit.AppendAsync(email, "login-error", $"HRMS unavailable: {PlatformCore.Trunc(r.Message ?? "", 200)} ip={ip}", ct);
                    return LoginResult.Fail("Sign-in is temporarily unavailable (HRMS did not answer). Please try again in a few minutes.");
                case HrmsStatus.LockedInHrms:
                    await _audit.AppendAsync(email, "login-fail", $"locked in HRMS ip={ip}", ct);
                    return LoginResult.Fail("Your HRMS account is locked after too many wrong passwords. Unlock it in MyVishal (My Page → Unlock User) or try again in about 30 minutes.");
                case HrmsStatus.Ok when !r.Employee!.IsWorking:
                    await _audit.AppendAsync(email, "login-fail", $"HRMS: employee {code} resigned / inactive ip={ip}", ct);
                    return LoginResult.Fail("Your HRMS record is not active. Contact the administrator.");
            }

            var audits = new List<(string, string, string)>();
            LoginResult result;
            using (await LockAsync(ct))
            {
                await ReloadUsersLockedAsync(ct);
                await RefreshSessionsLockedAsync(ct);
                // re-read: the admin may have deactivated or re-linked the user while HRMS was answering
                if (!_users.TryGetValue(email, out var u) || !u.Active || !SameCode(u.EmpCode, code))
                    result = LoginResult.Fail(BadEmployeeLogin);
                else if (r.Status != HrmsStatus.Ok)
                    result = LoginResult.Fail(await RecordFailureLockedAsync(u, BadEmployeeLogin, ip, audits, ct));
                else
                    result = await StartSessionLockedAsync(u, ip, audits, ct);
            }
            foreach (var (w, a, d) in audits) await _audit.AppendAsync(w, a, d, ct);
            return result;
        }

        private string LockedText(AppUser u)
        {
            var mins = (int)Math.Floor((u.LockedUntil - Now) / 60) + 1;
            return $"Too many wrong attempts. Try again in {mins} minute(s), or ask the admin to unlock you.";
        }

        /// <summary>One more wrong password; locks after MaxFailedLogins. Returns the message (caller holds the lock).</summary>
        private async Task<string> RecordFailureLockedAsync(AppUser u, string error, string ip, List<(string, string, string)> audits, CancellationToken ct)
        {
            var lockMin = _cfg.LockoutMinutesOrDefault;
            u.Failed += 1;
            if (u.Failed >= _cfg.MaxFailedLoginsOrDefault)
            {
                u.LockedUntil = Now + lockMin * 60;
                u.Failed = 0;
                error = $"Too many wrong attempts — locked for {lockMin} minutes.";
                audits.Add((u.Email, "locked", $"ip={ip}"));
            }
            await SaveUsersLockedAsync(ct);
            audits.Add((u.Email, "login-fail", $"ip={ip}"));
            return error;
        }

        /// <summary>
        /// Right password: clears the failures and opens a session (caller holds the lock). Single session (QPS
        /// ISingleSessionGuard): the user's other live sessions end as "replaced" — that device is signed out at its next
        /// check and told why.
        /// </summary>
        private async Task<LoginResult> StartSessionLockedAsync(AppUser u, string ip, List<(string, string, string)> audits, CancellationToken ct)
        {
            u.Failed = 0;
            u.LockedUntil = 0;
            u.LastLogin = _clock.NowIso;
            await SaveUsersLockedAsync(ct);
            var token = NewToken();
            var now = Now;
            var (upserted, removed) = PruneSessionsLocked();
            var replaced = new List<string>();
            if (_cfg.SingleSession)
                foreach (var (t, other) in _sessions.Where(kv => kv.Value.Email == u.Email && kv.Value.IsLive))
                {
                    End(other, SessionEndReasons.Replaced, now);
                    replaced.Add(t);
                }
            _sessions[token] = new SessionRecord { Email = u.Email, Created = now, Seen = now, Ip = ip };
            await SaveSessionsLockedAsync(upserted.Concat(replaced).Append(token), removed, ct);
            audits.Add((u.Email, "login", (u.IsEmployee ? $"hrms emp={u.EmpCode} " : "") + $"ip={ip}"
                                          + (replaced.Count > 0 ? $" (signed out {replaced.Count} other session(s))" : "")));
            return new LoginResult(token, null, replaced.Count > 0);
        }

        private static void End(SessionRecord s, string reason, double now)
        {
            s.Ended = now;
            s.EndReason = reason;
        }

        /// <summary>Email + the password kept by the tool (Local mode, and the built-in admin in HRMS mode).</summary>
        private async Task<LoginResult> LocalLoginAsync(string email, string password, string ip, CancellationToken ct)
        {
            await MaybeReloadAsync(ct);
            var audits = new List<(string, string, string)>();
            LoginResult result;
            using (await LockAsync(ct))
            {
                await ReloadUsersLockedAsync(ct);
                await RefreshSessionsLockedAsync(ct);
                if (!_users.TryGetValue(email, out var u) || !u.Active)
                {
                    audits.Add((email.Length > 0 ? email : "?", "login-fail", $"unknown or inactive user ip={ip}"));
                    result = LoginResult.Fail("Email or password is incorrect");
                }
                else if (u.LockedUntil > Now)
                    result = LoginResult.Fail(LockedText(u));
                else if (!_hasher.Verify(password, u.Pw))
                    result = LoginResult.Fail(await RecordFailureLockedAsync(u, "Email or password is incorrect", ip, audits, ct));
                else
                    result = await StartSessionLockedAsync(u, ip, audits, ct);
            }
            foreach (var (w, a, d) in audits) await _audit.AppendAsync(w, a, d, ct);
            return result;
        }

        // ── employees registered from HRMS (admin) ──────────────────

        /// <summary>
        /// The admin's "Fetch from HRMS": the employee's HRMS details (HRP_GET_EMP_DETAIL_CODE) and the login already
        /// registered with that code, if any. Throws when HRMS has no such employee or did not answer.
        /// </summary>
        public async Task<(HrmsEmployee Employee, AppUser? Registered)> LookupEmployeeAsync(string empCode, CancellationToken ct = default)
        {
            await InitializeAsync(ct);
            var code = NormEmpCode(empCode);
            if (!EmpCodeRe.IsMatch(code)) throw new AppValidationException("Enter a valid Employee ID");
            var r = await _hrms.GetEmployeeAsync(code, ct);
            var emp = r.Status switch
            {
                HrmsStatus.Ok when SameCode(r.Employee?.EmpCode, code) => r.Employee!,
                HrmsStatus.Unavailable => throw new AppException(503, "HRMS did not answer: " + (r.Message ?? "try again in a few minutes")),
                _ => throw new AppValidationException($"Employee ID {code} was not found in HRMS")
            };
            await MaybeReloadAsync(ct);
            using (await LockAsync(ct)) return (emp, FindByEmpCodeLocked(code)?.Clone());
        }

        /// <summary>
        /// Registers an employee: their details are fetched from HRMS here (never taken from the caller), so only real,
        /// working employees can be added. The login key is the HRMS email (<paramref name="email"/> only when HRMS has
        /// none). A login that already exists for that email without an Employee ID (e.g. from the SCR file) is linked and
        /// keeps its roles plus <paramref name="roles"/>.
        /// </summary>
        public async Task<AppUser> AddEmployeeAsync(string empCode, IEnumerable<string> roles, string by, string? email = null,
                                                    string source = "manual", CancellationToken ct = default)
        {
            var rl = (roles ?? Enumerable.Empty<string>()).Where(r => Roles.All.Contains(r)).ToList();
            if (rl.Count == 0) throw new AppValidationException("Pick at least one role");
            var (emp, registered) = await LookupEmployeeAsync(empCode, ct);
            var code = NormEmpCode(emp.EmpCode);
            if (registered != null) throw new AppValidationException($"Employee ID {code} is already registered ({registered.Email})");
            if (!emp.IsWorking) throw new AppValidationException($"Employee ID {code} is {(emp.IsResign ? "resigned" : "not active")} in HRMS");
            var key = NormEmail(emp.EmpMailId);
            if (!EmailFull.IsMatch(key)) key = NormEmail(email);
            if (!EmailFull.IsMatch(key))
                throw new AppValidationException($"HRMS has no email for {code}: enter the company email of the employee");
            AppUser result;
            bool linked;
            using (await LockAsync(ct))
            {
                await ReloadUsersLockedAsync(ct);
                if (FindByEmpCodeLocked(code) is { } other) throw new AppValidationException($"Employee ID {code} is already registered ({other.Email})");
                if (_users.TryGetValue(key, out var u))
                {
                    if (u.IsEmployee) throw new AppValidationException($"{key} is already registered with Employee ID {u.EmpCode}");
                    u.Roles = OrderRoles(u.Roles.Concat(rl));
                    linked = true;
                }
                else
                {
                    u = _users[key] = NewUser(key, emp.EmpName, rl, by, source);
                    linked = false;
                }
                ApplyEmployee(u, emp, by);
                await SaveUsersLockedAsync(ct);
                if (linked) await EndSessionsLockedAsync(key, null, SessionEndReasons.Changed, ct);   // now signs in with the Employee ID
                result = u.Clone();
            }
            await _audit.AppendAsync(by, linked ? "user-link-hrms" : "user-add",
                                     $"{key} emp={code} roles={string.Join(",", result.Roles)}", ct);
            return result;
        }

        /// <summary>
        /// Links an existing login to an Employee ID, or re-reads the HRMS details of a linked one (<paramref name="empCode"/>
        /// empty = its current code). From then on it signs in with the Employee ID and the HRMS password only.
        /// </summary>
        public async Task<AppUser> LinkEmployeeAsync(string email, string? empCode, string by, CancellationToken ct = default)
        {
            await InitializeAsync(ct);
            email = NormEmail(email);
            var current = await GetUserAsync(email, ct) ?? throw new NotFoundException("No such user");
            var code = NormEmpCode(string.IsNullOrWhiteSpace(empCode) ? current.EmpCode : empCode);
            var (emp, registered) = await LookupEmployeeAsync(code, ct);
            if (registered != null && registered.Email != email)
                throw new AppValidationException($"Employee ID {code} is already registered ({registered.Email})");
            if (!emp.IsWorking && !current.IsEmployee)
                throw new AppValidationException($"Employee ID {code} is {(emp.IsResign ? "resigned" : "not active")} in HRMS");
            AppUser result;
            using (await LockAsync(ct))
            {
                await ReloadUsersLockedAsync(ct);
                if (!_users.TryGetValue(email, out var u)) throw new NotFoundException("No such user");
                if (_users.Values.FirstOrDefault(x => x != u && SameCode(x.EmpCode, code)) is { } other)
                    throw new AppValidationException($"Employee ID {code} is already registered ({other.Email})");
                var relinked = !SameCode(u.EmpCode, code);
                ApplyEmployee(u, emp, by);
                await SaveUsersLockedAsync(ct);
                if (relinked) await EndSessionsLockedAsync(email, null, SessionEndReasons.Changed, ct);
                result = u.Clone();
            }
            await _audit.AppendAsync(by, current.IsEmployee && SameCode(current.EmpCode, code) ? "user-hrms-refresh" : "user-link-hrms",
                                     $"{email} emp={code}{(emp.IsWorking ? "" : " (not active in HRMS)")}", ct);
            return result;
        }

        /// <summary>HRMS details onto a login; the local password is dropped (HRMS checks the password from now on).</summary>
        private void ApplyEmployee(AppUser u, HrmsEmployee e, string by)
        {
            u.EmpCode = NormEmpCode(e.EmpCode);
            if (!string.IsNullOrWhiteSpace(e.EmpName)) u.Name = PlatformCore.Trunc(e.EmpName.Trim(), 80);
            u.Pw = "";
            u.MustChange = false;
            u.Failed = 0;
            u.LockedUntil = 0;
            u.Hr = new HrProfile
            {
                Email = NormEmail(e.EmpMailId), Location = e.EmpLocation?.Trim(), LocationCode = e.EmpLocCode?.Trim(),
                Department = e.EmpDeptName?.Trim(), Designation = e.EmpDesig?.Trim(), Mobile = e.EmpContactNo?.Trim(),
                Manager = e.EmpRptMng?.Trim(), Grade = e.EmpGrade, GradeDesc = e.EmpGradeDesc?.Trim(),
                HrActive = e.EmpStatus, Resigned = e.IsResign, FetchedAt = _clock.NowIso, FetchedBy = by
            };
        }

        /// <summary>Python secrets.token_urlsafe(32).</summary>
        private static string NewToken() =>
            Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');

        /// <summary>An ended session is kept this long (seconds) so the portal can still learn why it ended.</summary>
        private const double EndedKeepSeconds = 24 * 3600;

        private bool IdleOver(SessionRecord s, double now) => now - s.Seen > _cfg.SessionIdleSeconds;
        private bool LifetimeOver(SessionRecord s, double now) => _cfg.AbsoluteSessionHours > 0 && now - s.Created > _cfg.AbsoluteSessionHours * 3600;

        /// <summary>
        /// Ends idle / expired live sessions (kept as ended) and drops sessions ended more than a day ago.
        /// Returns the changed and the removed tokens (caller holds the lock and saves).
        /// </summary>
        private (List<string> Upserted, List<string> Removed) PruneSessionsLocked()
        {
            var now = Now;
            var upserted = new List<string>();
            var removed = new List<string>();
            foreach (var (t, s) in _sessions.ToList())
            {
                if (!s.IsLive)
                {
                    if (now - s.Ended > EndedKeepSeconds) { _sessions.Remove(t); removed.Add(t); }
                }
                else if (IdleOver(s, now) || LifetimeOver(s, now))
                {
                    End(s, IdleOver(s, now) ? SessionEndReasons.Idle : SessionEndReasons.Expired, now);
                    upserted.Add(t);
                }
            }
            return (upserted, removed);
        }

        public async Task<AppUser?> SessionUserAsync(string? token, CancellationToken ct = default) => (await SessionAsync(token, ct)).User;

        /// <summary>
        /// The user of a live session, or null with the reason the session ended (<see cref="SessionEndReasons"/>; null =
        /// unknown token). Idle timeout and absolute lifetime are checked here; "last seen" is persisted every 5 minutes.
        /// </summary>
        public async Task<(AppUser? User, string? EndReason)> SessionAsync(string? token, CancellationToken ct = default)
        {
            if (string.IsNullOrEmpty(token)) return (null, null);
            await InitializeAsync(ct);
            await MaybeReloadAsync(ct);
            using (await LockAsync(ct))
            {
                await RefreshSessionsLockedAsync(ct);   // a session signed in / out on another API instance
                if (!_sessions.TryGetValue(token, out var s)) return (null, null);
                if (!s.IsLive) return (null, s.EndReason ?? SessionEndReasons.Expired);
                var now = Now;
                if (IdleOver(s, now) || LifetimeOver(s, now))
                {
                    End(s, IdleOver(s, now) ? SessionEndReasons.Idle : SessionEndReasons.Expired, now);
                    await SaveSessionsLockedAsync(new[] { token }, Array.Empty<string>(), ct);
                    return (null, s.EndReason);
                }
                if (!_users.TryGetValue(s.Email, out var u) || !u.Active) return (null, SessionEndReasons.Disabled);
                if (now - s.Seen > 300)
                {
                    s.Seen = now;   // persist "last seen" at most every 5 min
                    await SaveSessionsLockedAsync(new[] { token }, Array.Empty<string>(), ct);
                }
                else s.Seen = now;
                return (u.Clone(), null);
            }
        }

        public async Task LogoutAsync(string token, CancellationToken ct = default)
        {
            await InitializeAsync(ct);
            SessionRecord? s;
            using (await LockAsync(ct))
            {
                await RefreshSessionsLockedAsync(ct);
                _sessions.Remove(token, out s);
                await SaveSessionsLockedAsync(Array.Empty<string>(), new[] { token }, ct);
            }
            if (s is { IsLive: true }) await _audit.AppendAsync(s.Email, "logout", "", ct);
        }

        public async Task EndSessionsAsync(string email, string? keep = null, string reason = SessionEndReasons.Changed, CancellationToken ct = default)
        {
            await InitializeAsync(ct);
            using (await LockAsync(ct)) await EndSessionsLockedAsync(NormEmail(email), keep, reason, ct);
        }

        /// <summary>Ends every live session of <paramref name="email"/> except <paramref name="keep"/>, with the reason (caller holds the lock).</summary>
        private async Task EndSessionsLockedAsync(string email, string? keep, string reason, CancellationToken ct)
        {
            await RefreshSessionsLockedAsync(ct);
            var now = Now;
            var ended = _sessions.Where(kv => kv.Value.Email == email && kv.Key != keep && kv.Value.IsLive).Select(kv => kv.Key).ToList();
            foreach (var t in ended) End(_sessions[t], reason, now);
            if (ended.Count > 0) await SaveSessionsLockedAsync(ended, Array.Empty<string>(), ct);
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
                await ReloadUsersLockedAsync(ct);
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
                foreach (var email in res.Updated) await EndSessionsLockedAsync(email, null, SessionEndReasons.Changed, ct);   // new roles: sign in again
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

    /// <summary>Values of <see cref="UserPublicDto.LoginType"/>.</summary>
    public static class LoginTypes
    {
        public const string Hrms = "hrms";
        public const string Local = "local";
    }

    /// <summary>No HRMS client registered (tests, tools): every call is "unavailable".</summary>
    internal sealed class NoHrms : IHrmsClient
    {
        public static readonly NoHrms Instance = new();
        public Task<HrmsResult> VerifyPasswordAsync(string empCode, string password, CancellationToken ct = default) =>
            Task.FromResult(HrmsResult.Unavailable("No HRMS client configured"));
        public Task<HrmsResult> GetEmployeeAsync(string empCode, CancellationToken ct = default) =>
            Task.FromResult(HrmsResult.Unavailable("No HRMS client configured"));
    }
}
