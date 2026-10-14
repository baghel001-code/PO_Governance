using Application.DTOs.Analytics.Grc;
using Domain.Entities.Engine;

namespace Application.Interfaces.V1.Analytics
{
    /// <summary>
    /// Season Selection: the sorted seasons of Article Master and the effective Current / Fallback
    /// selection (index.html _seasonSortKey / _allSeasonsSorted / renderSeasonPills / _toggleSeasonPill).
    /// Engines that build CY / fallback peer pools must use <see cref="Resolve"/>'s selection — it is what
    /// the browser had in S.params after Data Setup loaded Article Master.
    /// </summary>
    public interface ISeasonService
    {
        /// <summary>Seasons (most recent first) + effective selection for the snapshot's parameters.</summary>
        SeasonSelectionDto Resolve(EngineData data);

        /// <summary>Selection after clicking a pill (toggle, then re-validate). Does not save — the caller saves the params.</summary>
        SeasonSelectionDto Toggle(EngineData data, string group, string season);

        Task<SeasonSelectionDto> GetCurrentAsync(CancellationToken ct = default);
        Task<SeasonSelectionDto> ToggleAsync(string group, string season, CancellationToken ct = default);
    }
}
