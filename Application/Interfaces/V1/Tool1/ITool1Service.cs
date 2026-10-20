using DecisionEngine.Results.Tool1;
using Domain.Records;

namespace Application.Interfaces.V1.Tool1
{
    /// <summary>
    /// Tool 1 — the Category view's "✏️ New Request" (index.html formGMInput / formPopulateColours /
    /// populateMonths / formSubmit / renderResult / renderCatHistory and shared.js saveRequest). The decision
    /// runs on the server against the current data snapshot (<c>IDatasetCache</c>) and today's ledger of all
    /// users (<c>IRequestLedger.GetDayAsync(IAppClock.Today)</c>).
    /// </summary>
    public interface ITool1Service
    {
        /// <summary>The form for a GM: attribute strip, engine route, colour options, delivery months.</summary>
        Task<Tool1FormOptionsDto> GetFormOptionsAsync(string? gm, CancellationToken ct = default);

        /// <summary>
        /// Decides a request against today's ledger without saving. When the Confirm Final Quantity step
        /// applies (Single Concept, Max Permissible &gt; 0) the result has <c>NeedsConfirmation</c> and the
        /// pop-up figures. Always returns the <c>SeenSeq</c> to send back with Submit.
        /// </summary>
        Task<Tool1ResultDto> ValidateAsync(Tool1RequestInputDto input, AppUserContext user, CancellationToken ct = default);

        /// <summary>
        /// Re-pulls today's ledger, re-runs the decision (with the confirmed quantity) and saves the request
        /// with the v1.1.4 field names. A 409 (someone approved against the same colour since SeenSeq) is
        /// handled like shared.js: re-check the headroom, cap the approved quantity, save again with force.
        /// </summary>
        Task<Tool1ResultDto> SubmitAsync(Tool1SubmitInputDto input, AppUserContext user, CancellationToken ct = default);

        /// <summary>
        /// Today's Requests table rows (renderCatHistory). Staff see every request; others see their own
        /// and those they approve (see docs/ui/tool1.md for the SCR-scope rule the platform listing applies).
        /// </summary>
        Task<Tool1TodayDto> GetTodayAsync(AppUserContext user, bool onlyMine, CancellationToken ct = default);
    }
}
