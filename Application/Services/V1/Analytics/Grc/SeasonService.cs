using Application.Interfaces.V1;
using Application.Interfaces.V1.Analytics;
using DecisionEngine.Grc;
using DecisionEngine.Model;
using DecisionEngine.Results.Analytics.Grc;

namespace Application.Services.V1.Analytics.Grc
{
    /// <summary>
    /// Season Selection use cases (see <see cref="ISeasonService"/>): loads the current snapshot from
    /// <see cref="IDatasetCache"/> and asks <see cref="ISeasonSelectionEngine"/> (DecisionEngine) for the selection.
    /// </summary>
    public sealed class SeasonService : ISeasonService
    {
        private readonly IDatasetCache _cache;
        private readonly ISeasonSelectionEngine _engine;

        public SeasonService(IDatasetCache cache, ISeasonSelectionEngine engine)
        {
            _cache = cache;
            _engine = engine;
        }

        public SeasonSelectionDto Resolve(EngineData data) => _engine.Resolve(data);

        public SeasonSelectionDto Toggle(EngineData data, string group, string season) => _engine.Toggle(data, group, season);

        public async Task<SeasonSelectionDto> GetCurrentAsync(CancellationToken ct = default) =>
            _engine.Resolve(await _cache.GetCurrentAsync(ct));

        public async Task<SeasonSelectionDto> ToggleAsync(string group, string season, CancellationToken ct = default) =>
            _engine.Toggle(await _cache.GetCurrentAsync(ct), group, season);
    }
}
