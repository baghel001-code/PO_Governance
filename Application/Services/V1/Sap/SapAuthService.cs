using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using Application.Interfaces.V1;
using Application.Interfaces.V1.Sap;
using Domain.Entities.AppSettings;
using Domain.Entities.Sap;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Application.Services.V1.Sap
{
    /// <summary>
    /// HTTP Basic authentication of the SAP technical users (spec v1.4 §2: one account per SAP system, e.g. sap_ecq /
    /// sap_ecp). Passwords are PBKDF2 hashes (<see cref="IPasswordHasher"/>, the same as the portal logins). After
    /// SapInterface:MaxFailedLogins failures in a row the user is locked for SapInterface:LockoutMinutes; failures, lockouts
    /// and calls of inactive users are written to the audit trail ("sap-login-fail", "sap-locked").
    /// <para>
    /// Speed: a PBKDF2 check costs ~100 ms (200 000 iterations), too much for every /decisions call. A successful check is
    /// remembered for 10 minutes keyed by (user, SHA-256 of the password, stored hash), so a password reset or a different
    /// password always goes through PBKDF2 again; the user row (active / locked) is still read on every call.
    /// </para>
    /// </summary>
    public sealed class SapAuthService : ISapAuthService
    {
        private static readonly TimeSpan VerifiedTtl = TimeSpan.FromMinutes(10);
        private static readonly TimeSpan TouchEvery = TimeSpan.FromSeconds(60);

        private readonly ISapRepository _repo;
        private readonly IPasswordHasher _hasher;
        private readonly IAuditService _audit;
        private readonly IAppClock _clock;
        private readonly SapInterfaceSettings _cfg;
        private readonly ILogger<SapAuthService> _log;
        private readonly ConcurrentDictionary<string, DateTime> _verified = new();
        private readonly ConcurrentDictionary<string, DateTime> _touched = new(StringComparer.OrdinalIgnoreCase);

        public SapAuthService(ISapRepository repo, IPasswordHasher hasher, IAuditService audit, IAppClock clock,
                              IOptions<SapInterfaceSettings> cfg, ILogger<SapAuthService> log)
        {
            _repo = repo; _hasher = hasher; _audit = audit; _clock = clock; _cfg = cfg.Value; _log = log;
        }

        public async Task<SapAuthResult> AuthenticateAsync(string userName, string password, string clientIp, CancellationToken ct = default)
        {
            var name = (userName ?? "").Trim();
            if (name.Length == 0 || string.IsNullOrEmpty(password))
                return new SapAuthResult(false, null, SapErrorCodes.Unauthorized, "Missing or wrong credentials");
            var user = await _repo.GetTechUserAsync(name, ct);
            if (user == null)
            {
                await AuditAsync("sap:" + name, "sap-login-fail", "unknown technical user from " + clientIp);
                return new SapAuthResult(false, null, SapErrorCodes.Unauthorized, "Missing or wrong credentials");
            }
            if (!user.Active)
            {
                await AuditAsync("sap:" + name, "sap-login-fail", "technical user is disabled, call from " + clientIp);
                return new SapAuthResult(false, user, SapErrorCodes.Unauthorized, "Missing or wrong credentials");
            }
            if (user.LockedUntil is { } until && until > _clock.Now)
                return new SapAuthResult(false, user, SapErrorCodes.UserLocked,
                    "Technical user locked after too many failed logins until " + SapJson.Timestamp(until.ToOffset(_clock.Now.Offset)));

            var key = CacheKey(user, password);
            var ok = _verified.TryGetValue(key, out var exp) && exp > DateTime.UtcNow;
            if (!ok)
            {
                ok = _hasher.Verify(password, user.PasswordHash);
                if (ok)
                {
                    _verified[key] = DateTime.UtcNow + VerifiedTtl;
                    if (_verified.Count > 1000)
                        foreach (var kv in _verified.Where(kv => kv.Value < DateTime.UtcNow).ToList()) _verified.TryRemove(kv.Key, out _);
                }
            }
            if (!ok)
            {
                var after = await _repo.LoginFailAsync(user.UserName, Math.Max(1, _cfg.MaxFailedLogins), Math.Max(1, _cfg.LockoutMinutes), ct);
                var locked = after?.LockedUntil is { } lu && lu > _clock.Now;
                await AuditAsync("sap:" + user.UserName, locked ? "sap-locked" : "sap-login-fail",
                    "wrong password from " + clientIp + " (" + (after?.FailedCount ?? 0) + " failure(s) in a row" + (locked ? ", locked for " + _cfg.LockoutMinutes + " min" : "") + ")");
                _log.LogWarning("SAP technical user {User}: wrong password from {Ip}{Locked}", user.UserName, clientIp, locked ? " - locked" : "");
                return new SapAuthResult(false, user, SapErrorCodes.Unauthorized, "Missing or wrong credentials");
            }

            // success: reset the failure count / remember the last call (at most once a minute per user)
            if (user.FailedCount > 0 || !_touched.TryGetValue(user.UserName, out var last) || DateTime.UtcNow - last > TouchEvery || user.LastCallIp != clientIp)
            {
                _touched[user.UserName] = DateTime.UtcNow;
                await _repo.LoginOkAsync(user.UserName, clientIp, ct);
            }
            return new SapAuthResult(true, user, "", "");
        }

        private static string CacheKey(SapTechUser u, string password) =>
            u.UserName + "\n" + u.PasswordHash + "\n" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(password)));

        private async Task AuditAsync(string who, string action, string detail)
        {
            try { await _audit.AppendAsync(who, action, detail); }
            catch (Exception ex) { _log.LogWarning("Audit line not written ({Error})", ex.Message); }
        }
    }
}
