using System.Text.Json.Nodes;
using Domain.Records;

namespace Application.Interfaces.V1.Analytics
{
    /// <summary>
    /// Tool 2 (Raised PO Assessment) result rows — the browser's <c>batchResults</c> — for the Planner
    /// dashboard, AI Insights and the planner AI chat. Each row is the v1.1.4 line object (idx, gm, colour,
    /// dm, relQty, poNum, engine, decision, decisionReason, artSTR, pdstr, quartile_, recQty, excessQty,
    /// opStock, div, mcDesc, mrp, vendor, info{brand, flag …}, poRate, poValue …). Implemented by the Tool 2
    /// service (wired by the coordinator).
    /// </summary>
    public interface ITool2ResultSource
    {
        /// <summary>The rows of the latest run visible to <paramref name="user"/>; null when no run exists yet.</summary>
        Task<Tool2RunRows?> GetLatestRowsAsync(AppUserContext user, CancellationToken ct = default);

        /// <summary>The rows of a saved run (History); null when the run does not exist.</summary>
        Task<Tool2RunRows?> GetRunRowsAsync(string runId, AppUserContext user, CancellationToken ct = default);
    }

    /// <summary>A Tool 2 run: its id and its result rows in assessment order.</summary>
    public sealed record Tool2RunRows(string? RunId, IReadOnlyList<JsonObject> Rows);
}
