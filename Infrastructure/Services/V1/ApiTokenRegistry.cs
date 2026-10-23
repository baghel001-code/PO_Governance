using System.Security.Cryptography;
using System.Text;

namespace Infrastructure.Services.V1
{
    /// <summary>
    /// The API token POGovApi accepts per api/v1 path. By default every path takes the static token (JwtSettings:SecretKey);
    /// a path whose API_MASTER row gets another token for this environment from API_AUTHORIZATION (Auth_Source TASK /
    /// ENVIRONMENT, see <see cref="ApiTokenRefreshService"/>) takes that token instead. When a path's token changes, the
    /// previous one is still accepted for <see cref="Grace"/>, so the portal (which reloads its API list every few
    /// minutes) keeps working while it catches up. Thread-safe: readers see one consistent snapshot.
    /// </summary>
    public sealed class ApiTokenRegistry
    {
        public static readonly TimeSpan Grace = TimeSpan.FromMinutes(15);

        private sealed record Entry(byte[] Current, IReadOnlyList<(byte[] Token, DateTime Until)> Previous);

        private readonly byte[] _default;
        private readonly Func<DateTime> _now;
        private readonly object _gate = new();
        private volatile Dictionary<string, Entry> _paths = new(StringComparer.OrdinalIgnoreCase);

        public ApiTokenRegistry(string defaultToken, Func<DateTime>? utcNow = null)
        {
            _default = Encoding.UTF8.GetBytes(defaultToken);
            _now = utcNow ?? (() => DateTime.UtcNow);
        }

        /// <summary>Number of paths with their own token (or a token still in its grace period).</summary>
        public int Count => _paths.Count;

        public static string NormalizePath(string path)
        {
            var p = (path ?? "").Trim();
            if (!p.StartsWith('/')) p = "/" + p;
            return p.Length > 1 ? p.TrimEnd('/') : p;
        }

        /// <summary>True when <paramref name="token"/> is accepted for <paramref name="path"/> (constant-time comparisons).</summary>
        public bool IsValid(string path, string token)
        {
            var t = Encoding.UTF8.GetBytes(token);
            if (!_paths.TryGetValue(NormalizePath(path), out var e)) return CryptographicOperations.FixedTimeEquals(t, _default);
            if (CryptographicOperations.FixedTimeEquals(t, e.Current)) return true;
            var now = _now();
            foreach (var (prev, until) in e.Previous)
                if (until > now && CryptographicOperations.FixedTimeEquals(t, prev)) return true;
            return false;
        }

        /// <summary>
        /// Sets the paths that have their own token (path → plain token); every other path goes back to the static token.
        /// A token that is replaced stays valid for <see cref="Grace"/>.
        /// </summary>
        public void Update(IReadOnlyDictionary<string, string> overrides)
        {
            lock (_gate)
            {
                var now = _now();
                var wanted = overrides.ToDictionary(kv => NormalizePath(kv.Key), kv => Encoding.UTF8.GetBytes(kv.Value), StringComparer.OrdinalIgnoreCase);
                var old = _paths;
                var next = new Dictionary<string, Entry>(StringComparer.OrdinalIgnoreCase);
                foreach (var path in old.Keys.Union(wanted.Keys, StringComparer.OrdinalIgnoreCase))
                {
                    var desired = wanted.TryGetValue(path, out var w) ? w : _default;
                    old.TryGetValue(path, out var had);
                    var current = had?.Current ?? _default;
                    var previous = (had?.Previous ?? Array.Empty<(byte[], DateTime)>()).Where(p => p.Until > now).ToList();
                    if (!CryptographicOperations.FixedTimeEquals(current, desired)) previous.Add((current, now + Grace));
                    previous.RemoveAll(p => CryptographicOperations.FixedTimeEquals(p.Token, desired));
                    if (CryptographicOperations.FixedTimeEquals(desired, _default) && previous.Count == 0) continue;
                    next[path] = new Entry(desired, previous);
                }
                _paths = next;
            }
        }
    }
}
