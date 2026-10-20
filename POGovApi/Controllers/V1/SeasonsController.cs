using Application.Interfaces.V1.Analytics;
using DecisionEngine.Results.Analytics.Grc;
using Domain.Entities.Response;
using Microsoft.AspNetCore.Mvc;

namespace POGovApi.Controllers.V1
{
    /// <summary>
    /// Season Selection (Data Setup): seasons of Article Master, most recent first, and the effective
    /// Current / Fallback selection used for the BSR peer pools.
    /// </summary>
    [Route("api/v1/[Action]")]
    public sealed class SeasonsController : ApiControllerBase
    {
        private readonly ISeasonService _seasons;
        public SeasonsController(ISeasonService seasons) => _seasons = seasons;

        /// <summary>Sorted seasons + effective selection (defaults: latest = Current, second latest = Fallback).</summary>
        [HttpPost]
        public async Task<ActionResult<ApiDataResponse<SeasonSelectionDto>>> SeasonsGet(CancellationToken ct) =>
            Success(await _seasons.GetCurrentAsync(ct));

        /// <summary>
        /// Selection after clicking a pill (toggle + re-validation). Not saved here: the portal saves
        /// seasonCurrent / seasonFallback through ParamsSave, as the browser saved S.params.
        /// </summary>
        [HttpPost]
        public async Task<ActionResult<ApiDataResponse<SeasonSelectionDto>>> SeasonsToggle([FromBody] SeasonToggleRequest request, CancellationToken ct)
        {
            RequireStaff();
            return Success(await _seasons.ToggleAsync(request.Group, request.Season, ct));
        }
    }
}
