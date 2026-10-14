using Application.DTOs.Platform;
using Application.Interfaces.V1;
using Microsoft.Extensions.Logging;

namespace Application.Services.V1.Platform
{
    /// <summary>server.py _audit and api_GET_audit.</summary>
    public sealed class AuditService : IAuditService
    {
        private readonly IStore _store;
        private readonly IAppClock _clock;
        private readonly ILogger<AuditService> _log;

        public AuditService(IStore store, IAppClock clock, ILogger<AuditService> log)
        {
            _store = store;
            _clock = clock;
            _log = log;
        }

        public async Task AppendAsync(string who, string action, string detail = "", CancellationToken ct = default)
        {
            await _store.AuditAppendAsync(_clock.NowIso, who, action, detail ?? "", ct);
            _log.LogInformation("{Who} {Action} {Detail}", who, action, detail);
        }

        public async Task<AuditQueryResponse> QueryAsync(string? from, string? to, string? q, CancellationToken ct = default)
        {
            PlatformCore.CheckRange(from, to);
            var text = (q ?? "").ToLowerInvariant();
            var outp = new List<AuditItem>();
            foreach (var l in await _store.AuditReadAsync(ct))
            {
                if (!PlatformCore.InRange(l.At, from, to)) continue;
                if (text.Length > 0 && !$"{l.At}\t{l.Who ?? "None"}\t{l.Action ?? "None"}\t{l.Detail ?? "None"}".ToLowerInvariant().Contains(text)) continue;
                outp.Add(new AuditItem { At = l.At, Who = l.Who, Action = l.Action, Detail = l.Detail });
            }
            outp.Reverse();
            return new AuditQueryResponse { Items = outp.Take(5000).ToList(), Total = outp.Count };
        }
    }
}
