using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Application.Interfaces.V1;
using Application.Interfaces.V1.Sap;
using Domain.Entities.Sap;
using Domain.Exceptions;

namespace Application.Services.V1.Sap
{
    /// <summary>
    /// The read-only verdict page behind a decision's view_url (portal /verdict/&lt;token&gt;, opened without sign-in, as
    /// chosen for SAP users): business fields only. Unknown token → 404, past View_Expires_At → 410.
    /// </summary>
    public sealed class SapVerdictViewService : ISapVerdictViewService
    {
        private static readonly Regex TokenRx = new(@"^[A-Za-z0-9_\-]{20,64}$", RegexOptions.Compiled);

        private readonly ISapRepository _repo;
        private readonly IAppClock _clock;

        public SapVerdictViewService(ISapRepository repo, IAppClock clock)
        {
            _repo = repo;
            _clock = clock;
        }

        public async Task<SapVerdictView> GetAsync(string token, CancellationToken ct = default)
        {
            if (!_repo.Available) throw new ServiceUnavailableException(_repo.UnavailableReason ?? "SAP interface requires SQL Server storage");
            var t = (token ?? "").Trim();
            if (!TokenRx.IsMatch(t)) throw new NotFoundException("This verdict link is not valid.");
            var d = await _repo.GetDecisionByViewTokenAsync(t, ct) ?? throw new NotFoundException("This verdict link is not valid.");
            var row = d.Decision!;
            if (row.ViewExpiresAt is { } until && until < _clock.Now)
                throw new AppException(410, "This verdict link expired on " + until.ToOffset(_clock.Now.Offset).ToString("dd-MMM-yyyy") + ".");

            string? summary = null;
            try { summary = (JsonNode.Parse(row.ResponseJson ?? "{}") as JsonObject)?["summary_text"]?.GetValue<string>(); }
            catch (System.Text.Json.JsonException) { /* old row without JSON: no summary */ }

            return new SapVerdictView
            {
                DecisionId = row.DecisionId, RequestId = row.RequestId, PoNumber = row.PoNumber, BuyerUser = row.BuyerUser, Division = row.Division,
                Mode = row.Mode, OverallVerdict = row.OverallVerdict, RouteTo = row.RouteTo, SummaryText = summary, DataDelayed = row.DataDelayed,
                DecidedAt = row.DecidedAt, ExpiresAt = row.ViewExpiresAt,
                Items = d.Items.Select(i => new SapVerdictViewItem
                {
                    PoItem = i.PoItem, Article = i.Article, GenericArticle = i.GenericArticle, Colour = i.Colour ?? i.ColourSent,
                    DeliveryMonth = i.DeliveryMonth, OrderQty = i.OrderQty, Verdict = i.Verdict, MaxQty = i.MaxQty, ReasonCode = i.ReasonCode,
                    ReasonText = i.ReasonText
                }).ToList(),
                Document = d.Documents.FirstOrDefault(x => x.IsCurrent)
            };
        }
    }
}
