using Application.Interfaces.V1;
using Application.Interfaces.V1.Analytics;
using DecisionEngine.Analytics.Help;
using DecisionEngine.Results.Analytics.Help;

namespace Application.Services.V1.Analytics.Help
{
    /// <summary>"How it works" use case: the current parameters (<see cref="IDatasetCache"/>) through <see cref="IHelpContentEngine"/>.</summary>
    public sealed class HelpContentService : IHelpContentService
    {
        private readonly IDatasetCache _cache;
        private readonly IHelpContentEngine _engine;

        public HelpContentService(IDatasetCache cache, IHelpContentEngine engine)
        {
            _cache = cache;
            _engine = engine;
        }

        public async Task<HelpParamsDto> GetParamsAsync(CancellationToken ct = default) =>
            _engine.Compute((await _cache.GetCurrentAsync(ct)).Params);
    }
}
