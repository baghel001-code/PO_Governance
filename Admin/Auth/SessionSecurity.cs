using System.Collections.Concurrent;
using System.Security.Cryptography;
using Domain.Constants;

namespace Admin.Auth
{
    /// <summary>
    /// A verified sign-in waiting for its cookie: the Blazor sign-in page (which cannot set cookies) hands it to the
    /// /account/complete-login HTTP endpoint (which can). Holds the POGovApi session token — never the password.
    /// </summary>
    public sealed record LoginTicket(string SessionToken, bool RememberMe, bool ReplacedOtherSession, DateTimeOffset ExpiresUtc);

    /// <summary>
    /// QPS LoginTicketStore: random, single-use tickets that expire after 60 seconds. In memory: fine for one portal server
    /// or sticky sessions (Blazor Server needs them anyway).
    /// </summary>
    public sealed class LoginTicketStore
    {
        private static readonly TimeSpan Lifetime = TimeSpan.FromSeconds(60);
        private readonly ConcurrentDictionary<string, LoginTicket> _tickets = new();
        private readonly TimeProvider _clock;

        public LoginTicketStore(TimeProvider clock) => _clock = clock;

        public string Issue(string sessionToken, bool rememberMe, bool replacedOtherSession)
        {
            var now = _clock.GetUtcNow();
            foreach (var (key, t) in _tickets)
                if (t.ExpiresUtc <= now) _tickets.TryRemove(key, out _);
            var ticket = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
            _tickets[ticket] = new LoginTicket(sessionToken, rememberMe, replacedOtherSession, now + Lifetime);
            return ticket;
        }

        /// <summary>Returns the ticket once; a second call with the same value returns null.</summary>
        public LoginTicket? Redeem(string? ticket)
        {
            if (string.IsNullOrEmpty(ticket) || ticket.Length != 64) return null;
            return _tickets.TryRemove(ticket, out var t) && t.ExpiresUtc > _clock.GetUtcNow() ? t : null;
        }
    }

    /// <summary>
    /// The ?reason= values of the sign-in page: <see cref="SessionEndReasons"/> (from POGovApi) plus "signedout".
    /// </summary>
    public static class SignOutReasons
    {
        public const string SignedOut = "signedout";

        public static readonly IReadOnlySet<string> All = new HashSet<string>
        {
            SignedOut, SessionEndReasons.Idle, SessionEndReasons.Replaced, SessionEndReasons.Changed,
            SessionEndReasons.Disabled, SessionEndReasons.Password, SessionEndReasons.Expired
        };

        public static string Normalize(string? reason) => reason != null && All.Contains(reason) ? reason : SignedOut;

        /// <summary>The sign-in page's message for each reason (null = none).</summary>
        public static string? Text(string? reason) => reason switch
        {
            SignedOut => "You have been signed out.",
            SessionEndReasons.Idle => "You were signed out after a period of inactivity.",
            SessionEndReasons.Replaced => "You were signed out because your account was signed in on another device.",
            SessionEndReasons.Changed => "Your access was changed by the administrator. Please sign in again.",
            SessionEndReasons.Disabled => "Your account has been deactivated. Contact the administrator.",
            SessionEndReasons.Password => "Your password was changed. Sign in with the new password.",
            SessionEndReasons.Expired => "Your session has ended. Please sign in again.",
            _ => null
        };
    }

    /// <summary>
    /// QPS EndedSessions: sessions found ended while a page was open. The cookie handler re-checks with the API only every
    /// few seconds; this list makes it refuse such a cookie on the very next request. Keyed by the API session token.
    /// </summary>
    public sealed class EndedSessions
    {
        private static readonly TimeSpan Keep = TimeSpan.FromHours(24);   // longer than any cookie lives
        private readonly ConcurrentDictionary<string, (string Reason, DateTimeOffset Until)> _ended = new();
        private readonly TimeProvider _clock;

        public EndedSessions(TimeProvider clock) => _clock = clock;

        public void Add(string? sessionToken, string reason)
        {
            if (string.IsNullOrEmpty(sessionToken)) return;
            var now = _clock.GetUtcNow();
            foreach (var (key, entry) in _ended)
                if (entry.Until <= now) _ended.TryRemove(key, out _);
            _ended[sessionToken] = (reason, now + Keep);
        }

        public bool TryGet(string? sessionToken, out string reason)
        {
            reason = "";
            if (string.IsNullOrEmpty(sessionToken) || !_ended.TryGetValue(sessionToken, out var e) || e.Until <= _clock.GetUtcNow()) return false;
            reason = e.Reason;
            return true;
        }
    }

    /// <summary>
    /// QPS LoginNotices: one-time messages for the first page after sign-in ("your other device was signed out"), keyed by
    /// the new session token, kept for 2 minutes.
    /// </summary>
    public sealed class LoginNotices
    {
        public const string DuplicateLogin = "DuplicateLogin";
        private static readonly TimeSpan Keep = TimeSpan.FromMinutes(2);
        private readonly ConcurrentDictionary<string, (string Notice, DateTimeOffset Until)> _notices = new();
        private readonly TimeProvider _clock;

        public LoginNotices(TimeProvider clock) => _clock = clock;

        public void Add(string sessionToken, string notice)
        {
            var now = _clock.GetUtcNow();
            foreach (var (key, e) in _notices)
                if (e.Until <= now) _notices.TryRemove(key, out _);
            _notices[sessionToken] = (notice, now + Keep);
        }

        /// <summary>Returns the notice once; later calls return null.</summary>
        public string? Take(string? sessionToken) =>
            !string.IsNullOrEmpty(sessionToken) && _notices.TryRemove(sessionToken, out var e) && e.Until > _clock.GetUtcNow() ? e.Notice : null;
    }

    /// <summary>Per circuit: why the session check signed the user out (set by ApiRevalidatingAuthStateProvider, read by the layout).</summary>
    public sealed class SessionEndState
    {
        public string? Reason { get; set; }
    }

    /// <summary>QPS UserActivityState: per circuit, the last time the user did something in the page (idle timeout).</summary>
    public sealed class UserActivityState
    {
        private readonly TimeProvider _clock;
        private long _last;

        public UserActivityState(TimeProvider clock)
        {
            _clock = clock;
            _last = clock.GetUtcNow().ToUnixTimeMilliseconds();
        }

        public void Touch() => Interlocked.Exchange(ref _last, _clock.GetUtcNow().ToUnixTimeMilliseconds());

        public TimeSpan IdleFor => TimeSpan.FromMilliseconds(_clock.GetUtcNow().ToUnixTimeMilliseconds() - Interlocked.Read(ref _last));
    }
}
