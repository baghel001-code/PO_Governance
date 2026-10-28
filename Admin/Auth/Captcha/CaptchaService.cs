using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;

namespace Admin.Auth.Captcha;

/// <summary>
/// Issues and checks CAPTCHAs, entirely on the server and without HttpContext.Session.
/// The answer is stored here in memory under a random id. On an interactive page that id stays
/// in the component's server-side state, so the browser only ever receives the image.
/// Each challenge is single-use (checked once, right or wrong) and expires after 5 minutes.
/// The image itself comes from the registered <see cref="ICaptchaGenerator"/>. (QPS Admin/Auth/Captcha/CaptchaService.)
///
/// In-memory, like LoginTicketStore: fine for one server or sticky sessions.
/// </summary>
public sealed class CaptchaService(ICaptchaGenerator generator, TimeProvider clock)
{
    /// <summary>Upper bound for the answer box; generators may use shorter codes.</summary>
    public const int MaxLength = 6;
    private static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(5);
    private const int PruneThreshold = 10_000;

    private readonly ConcurrentDictionary<string, Entry> _challenges = new();

    private sealed record Entry(string Answer, DateTimeOffset ExpiresUtc);

    public sealed record Challenge(string Id, string ImageDataUrl);

    public Challenge Create()
    {
        var now = clock.GetUtcNow();
        if (_challenges.Count > PruneThreshold)
            foreach (var (key, e) in _challenges)
                if (e.ExpiresUtc <= now) _challenges.TryRemove(key, out _);

        var generated = generator.Generate();
        var id = Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
        _challenges[id] = new Entry(Normalize(generated.Answer), now + Lifetime);
        return new Challenge(id, "data:image/png;base64," + generated.ImageBase64);
    }

    /// <summary>
    /// True if <paramref name="typed"/> matches (case-insensitive, spaces ignored).
    /// Uses the challenge up either way, so every attempt needs a fresh image.
    /// </summary>
    public bool Validate(string? id, string? typed)
    {
        if (string.IsNullOrEmpty(id) || !_challenges.TryRemove(id, out var entry))
            return false;
        if (entry.ExpiresUtc <= clock.GetUtcNow())
            return false;

        var normalized = Normalize(typed);
        return normalized.Length == entry.Answer.Length &&
               CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(normalized), Encoding.UTF8.GetBytes(entry.Answer));
    }

    public void Discard(string? id)
    {
        if (id is not null)
            _challenges.TryRemove(id, out _);
    }

    private static string Normalize(string? text) =>
        new string((text ?? "").Where(c => !char.IsWhiteSpace(c)).ToArray()).ToUpperInvariant();
}