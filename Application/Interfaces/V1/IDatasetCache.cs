using Domain.Entities.Engine;

namespace Application.Interfaces.V1
{
    /// <summary>
    /// Holds the current <see cref="EngineData"/> snapshot (current version of every data file +
    /// parameters) in memory and rebuilds it when an upload, restore, clear or parameter change bumps
    /// the manifest's dataVersion / paramsVersion.
    /// </summary>
    public interface IDatasetCache
    {
        /// <summary>The snapshot for the current manifest (loads / reloads as needed).</summary>
        Task<EngineData> GetCurrentAsync(CancellationToken ct = default);

        /// <summary>
        /// A snapshot built from specific stored versions ({key: version}) — used to re-open a saved
        /// Tool 2 run with the exact files of that moment.
        /// </summary>
        Task<EngineData> GetForVersionsAsync(IReadOnlyDictionary<string, string> versions, EngineParams p, CancellationToken ct = default);

        /// <summary>Drops the cached snapshot (called after uploads / param changes).</summary>
        void Invalidate();
    }
}
