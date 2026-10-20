using System.Globalization;
using System.Text.Json.Nodes;
using Application.Interfaces.V1;
using Application.Interfaces.V1.Analytics;
using Domain.Entities.AppSettings;
using Domain.Records;
using Microsoft.Extensions.Options;

namespace Application.Services.V1.Adapters
{
    /// <summary>
    /// Requests a user may see, for the analytics screens: each day's live requests with approvals applied,
    /// filtered by visibility — exactly what PO_REQUESTS_GET (api/v1/RequestsGet) returns (v1.1.4 S.requests).
    /// </summary>
    public sealed class RequestHistorySource : IRequestHistorySource
    {
        /// <summary>Upper bound on the days read for one range (a "Last 90 days" view is 91).</summary>
        private const int MaxDays = 400;
        private readonly IRequestService _requests;

        public RequestHistorySource(IRequestService requests) => _requests = requests;

        public async Task<List<JsonObject>> GetVisibleRequestsAsync(AppUserContext user, string fromDate, string toDate, CancellationToken ct = default)
        {
            var from = DateOnly.ParseExact(fromDate, "yyyy-MM-dd", CultureInfo.InvariantCulture);
            var to = DateOnly.ParseExact(toDate, "yyyy-MM-dd", CultureInfo.InvariantCulture);
            if (to < from) (from, to) = (to, from);
            if (to.DayNumber - from.DayNumber >= MaxDays) from = to.AddDays(-(MaxDays - 1));
            var all = new List<JsonObject>();
            for (var d = from; d <= to; d = d.AddDays(1))
            {
                var day = await _requests.GetDayForUserAsync(d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), user, ct);
                all.AddRange(day.Requests);
            }
            return all;
        }
    }

    /// <summary>The assistant's AI provider = the platform AI client (server.py anthropic_call / insights_call).</summary>
    public sealed class AiGatewayAdapter : IAiGateway
    {
        private readonly IAiClient _client;
        private readonly PoGovSettings _cfg;

        public AiGatewayAdapter(IAiClient client, IOptions<PoGovSettings> cfg)
        {
            _client = client;
            _cfg = cfg.Value;
        }

        public bool ChatEnabled => _cfg.AiChatEnabled;
        public bool InsightsEnabled => _cfg.AiInsightsEnabled;

        public async Task<AiGatewayResult> ChatAsync(string system, IReadOnlyList<AiChatMessage> messages, int maxTokens, CancellationToken ct = default)
        {
            var arr = new JsonArray();
            foreach (var m in messages) arr.Add(new JsonObject { ["role"] = m.Role, ["content"] = m.Content });
            var r = await _client.AnthropicCallAsync(system, arr, maxTokens, ct);
            return new AiGatewayResult(r.Status, r.Data);
        }

        public async Task<AiGatewayResult> InsightsAsync(string prompt, CancellationToken ct = default)
        {
            var r = await _client.InsightsCallAsync(prompt, ct);
            return new AiGatewayResult(r.Status, r.Data);
        }
    }
}

