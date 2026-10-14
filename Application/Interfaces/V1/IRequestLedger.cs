using System.Text.Json.Nodes;
using Domain.Records;

namespace Application.Interfaces.V1
{
    /// <summary>
    /// Tool 1 request log (server.py /api/requests). Requests are kept as JSON objects with the same
    /// field names v1.1.4 stored (gm, colour, month, qty, decision, finalApprovedQty, engine, info{…},
    /// colourKey, reqId, seq, requestedBy, requestedByEmail, savedAt, approval{…} …) so history recorded
    /// by the Python tool and by this version read the same.
    /// </summary>
    public interface IRequestLedger
    {
        /// <summary>
        /// Today's live (not cleared) requests of all users, with approvals applied, and the day's
        /// current sequence number. Unfiltered — used by the same-day Max Permissible ledger.
        /// </summary>
        Task<(List<JsonObject> Requests, int Seq)> GetDayAsync(string date, CancellationToken ct = default);

        /// <summary>
        /// Saves a request (server.py api_POST_requests): scope check, stale-headroom conflict check
        /// against <paramref name="seenSeq"/> (unless <paramref name="force"/>), assigns seq / id / reqId,
        /// requestedBy*, savedAt and — for "Watch List" — the pending approval with the category heads
        /// of that Brand × MC × MRP. Throws ConflictException (409, payload {conflict, byOthers, seq}).
        /// Returns the saved request.
        /// </summary>
        Task<JsonObject> SaveAsync(JsonObject request, int seenSeq, bool force, AppUserContext user, CancellationToken ct = default);
    }
}
