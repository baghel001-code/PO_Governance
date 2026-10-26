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

            string? summary = null, bw = null;
            try
            {
                var answer = JsonNode.Parse(row.ResponseJson ?? "{}") as JsonObject;
                summary = answer?["summary_text"]?.GetValue<string>();
                bw = answer?["data_as_of"]?["bw_snapshot"]?.GetValue<string>();
            }
            catch (Exception e) when (e is System.Text.Json.JsonException or InvalidOperationException) { /* no usable answer JSON */ }

            return new SapVerdictView
            {
                DecisionId = row.DecisionId, RequestId = row.RequestId, PoNumber = row.PoNumber, BuyerUser = row.BuyerUser, Division = row.Division,
                Mode = row.Mode, OverallVerdict = row.OverallVerdict, VerdictCode = row.VerdictCode, RouteTo = row.RouteTo,
                BmHeadRequired = row.BmHeadRequired, SummaryText = summary, RulesVersion = row.RulesVersion, SourceSystem = row.SourceSystem,
                BwSnapshot = bw, DataDelayed = row.DataDelayed, RequestedAt = row.RequestedAt, DecidedAt = row.DecidedAt, ExpiresAt = row.ViewExpiresAt,
                Items = d.Items.Select(i =>
                {
                    var m = Metrics(i.MetricsJson);
                    return new SapVerdictViewItem
                    {
                        PoItem = i.PoItem, Article = i.Article, GenericArticle = i.GenericArticle, Colour = i.Colour ?? i.ColourSent,
                        DeliveryMonth = i.DeliveryMonth, OrderQty = i.OrderQty, GroupQty = i.GroupQty, Verdict = i.Verdict, MaxQty = i.MaxQty,
                        ReasonCode = i.ReasonCode, RuleId = i.RuleId, ReasonText = i.ReasonText, PlanQty = m?.PlanQty, StockQty = m?.StockQty,
                        OpenPoQty = m?.OpenPoQty, HeadroomQty = m?.HeadroomQty, CoverDays = m?.CoverDays, SellThroughPct = m?.SellThroughPct
                    };
                }).ToList(),
                Document = d.Documents.FirstOrDefault(x => x.IsCurrent)
            };
        }

        private static SapDecisionMetrics? Metrics(string? json)
        {
            if (string.IsNullOrWhiteSpace(json)) return null;
            try { return SapJson.Deserialize<SapDecisionMetrics>(json); }
            catch (System.Text.Json.JsonException) { return null; }
        }
    }
}
