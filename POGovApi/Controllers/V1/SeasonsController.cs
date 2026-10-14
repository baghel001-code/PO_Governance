using Application.DTOs.Analytics.Grc;
using Application.Interfaces.V1.Analytics;
using Domain.Entities.Response;
using Microsoft.AspNetCore.Mvc;

namespace POGovApi.Controllers.V1
{
    /// <summary>
    /// Season Selection (Data Setup): seasons of Article Master, most recent first, and the effective
    /// Current / Fallback selection used for the BSR peer pools.
    /// </summary>
    [Route("api/v1/seasons")]
    public sealed class SeasonsController : ApiControllerBase
    {
        private readonly ISeasonService _seasons;
        public SeasonsController(ISeasonService seasons) => _seasons = seasons;

        /// <summary>Sorted seasons + effective selection (defaults: latest = Current, second latest = Fallback).</summary>
        [HttpGet]
        public async Task<ActionResult<ApiResponse<SeasonSelectionDto>>> Get(CancellationToken ct) =>
            Success(await _seasons.GetCurrentAsync(ct));

        /// <summary>
        /// Selection after clicking a pill (toggle + re-validation). Not saved here: the portal saves
        /// seasonCurrent / seasonFallback through the parameters endpoint, as the browser saved S.params.
        /// </summary>
        [HttpPost("toggle")]
        public async Task<ActionResult<ApiResponse<SeasonSelectionDto>>> Toggle([FromBody] SeasonToggleRequest request, CancellationToken ct)
        {
            RequireStaff();
            return Success(await _seasons.ToggleAsync(request.Group, request.Season, ct));
        }
    }
}
