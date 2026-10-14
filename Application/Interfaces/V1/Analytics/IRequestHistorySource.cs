using System.Text.Json.Nodes;
using Domain.Records;

namespace Application.Interfaces.V1.Analytics
{
    /// <summary>
    /// The Tool 1 requests a user may see, for the analytics screens (Category dashboard, AI chat).
    /// v1.1.4 used <c>S.requests</c> = <c>GET /api/requests?date=today</c>: the day's live (not cleared)
    /// requests with approvals applied, filtered for buyers / category heads by server.py <c>visible_to</c>
    /// (own requests, requests they approve, requests on their SCR Brand × MC × MRP; staff see all).
    /// Implemented by the platform's request / history service (wired by the coordinator).
    /// </summary>
    public interface IRequestHistorySource
    {
        /// <summary>
        /// Requests dated <paramref name="fromDate"/> … <paramref name="toDate"/> (yyyy-MM-dd, inclusive) that
        /// <paramref name="user"/> may see, in saved order (seq ascending within a day, days ascending), as the
        /// stored v1.1.4 JSON objects (gm, colour, month, qty, engine, decision, decisionReason, info{…},
        /// finalApprovedQty, approval{…}, id, reqId …).
        /// </summary>
        Task<List<JsonObject>> GetVisibleRequestsAsync(AppUserContext user, string fromDate, string toDate, CancellationToken ct = default);
    }
}
