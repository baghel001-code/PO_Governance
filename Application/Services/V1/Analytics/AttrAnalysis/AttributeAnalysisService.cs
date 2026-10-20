using Application.Interfaces.V1;
using Application.Interfaces.V1.Analytics;
using DecisionEngine.Analytics.AttrAnalysis;
using DecisionEngine.Results.Analytics.AttrAnalysis;

namespace Application.Services.V1.Analytics.AttrAnalysis
{
    /// <summary>
    /// "🔎 Attribute Analysis" use cases: loads the current snapshot (<see cref="IDatasetCache"/>) and runs
    /// <see cref="IAttributeAnalysisEngine"/> (DecisionEngine). Engine errors become the platform's
    /// <c>AppException</c>s (<see cref="EngineErrors"/>).
    /// </summary>
    public sealed class AttributeAnalysisService : IAttributeAnalysisService
    {
        private readonly IDatasetCache _cache;
        private readonly IAttributeAnalysisEngine _engine;

        public AttributeAnalysisService(IDatasetCache cache, IAttributeAnalysisEngine engine)
        {
            _cache = cache;
            _engine = engine;
        }

        public async Task<AaLevel1Dto> GetLevel1Async(AttrAnalysisQuery? query, CancellationToken ct = default)
        {
            var data = await _cache.GetCurrentAsync(ct);
            return EngineErrors.Run(() => _engine.GetLevel1(data, query));
        }

        public async Task<AaLevel2Dto> GetLevel2Async(string brand, string mcKey, CancellationToken ct = default)
        {
            var data = await _cache.GetCurrentAsync(ct);
            return EngineErrors.Run(() => _engine.GetLevel2(data, brand, mcKey));
        }

        public async Task<AaLevel3Dto> GetLevel3Async(string brand, string mcKey, string mrp, string flag, CancellationToken ct = default)
        {
            var data = await _cache.GetCurrentAsync(ct);
            return EngineErrors.Run(() => _engine.GetLevel3(data, brand, mcKey, mrp, flag));
        }
    }
}
