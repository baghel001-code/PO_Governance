using System.Collections.Concurrent;
using Domain.Constants;
using Domain.Entities.Common;

namespace Domain.Entities.Engine
{
    /// <summary>
    /// Immutable snapshot of everything the decision engines read: the current version of every
    /// data file plus the planner parameters — the server-side equivalent of the browser's
    /// <c>S.files</c> + <c>S.params</c>. Built by the dataset cache whenever the manifest's
    /// dataVersion / paramsVersion changes; engines must treat it as read-only.
    /// </summary>
    public sealed class EngineData
    {
        public EngineData(IReadOnlyDictionary<string, IReadOnlyList<SheetRow>?> files, EngineParams p,
                          long dataVersion, long paramsVersion, string? grcPeriodType = null)
        {
            Files = files;
            Params = p;
            DataVersion = dataVersion;
            ParamsVersion = paramsVersion;
            GrcPeriodType = grcPeriodType;
        }

        /// <summary>Rows per dataset key (see <see cref="DatasetKeys"/>); null / missing = not uploaded.</summary>
        public IReadOnlyDictionary<string, IReadOnlyList<SheetRow>?> Files { get; }
        public EngineParams Params { get; }
        public long DataVersion { get; }
        public long ParamsVersion { get; }
        /// <summary>"week" or "month" for the GRC buying-history file (browser S.files.grcPeriodType).</summary>
        public string? GrcPeriodType { get; }

        public IReadOnlyList<SheetRow>? Get(string key) => Files.TryGetValue(key, out var v) ? v : null;
        public bool Has(string key) => Get(key) is { Count: > 0 };

        public IReadOnlyList<SheetRow>? F1 => Get(DatasetKeys.F1);
        public IReadOnlyList<SheetRow>? F2 => Get(DatasetKeys.F2);
        public IReadOnlyList<SheetRow>? F3 => Get(DatasetKeys.F3);
        public IReadOnlyList<SheetRow>? F4 => Get(DatasetKeys.F4);
        public IReadOnlyList<SheetRow>? Um => Get(DatasetKeys.Um);
        public IReadOnlyList<SheetRow>? Bsr => Get(DatasetKeys.Bsr);
        public IReadOnlyList<SheetRow>? ArtMaster => Get(DatasetKeys.ArtMaster);
        public IReadOnlyList<SheetRow>? ColMaster => Get(DatasetKeys.ColMaster);
        public IReadOnlyList<SheetRow>? AttrRel => Get(DatasetKeys.AttrRel);
        public IReadOnlyList<SheetRow>? CatHeadMap => Get(DatasetKeys.CatHeadMap);
        public IReadOnlyList<SheetRow>? Grc => Get(DatasetKeys.Grc);

        /// <summary>
        /// Per-snapshot cache for derived indexes (the browser's <c>_idx</c> object). Engines store
        /// lookup maps here under a unique name so they are built once per data version.
        /// </summary>
        public ConcurrentDictionary<string, object> Cache { get; } = new();

        public T GetOrBuild<T>(string name, Func<EngineData, T> build) where T : class =>
            (T)Cache.GetOrAdd(name, _ => build(this));

        /// <summary>A copy that uses different parameters (e.g. a saved Tool 2 run's parameters).</summary>
        public EngineData WithParams(EngineParams p) => new(Files, p, DataVersion, ParamsVersion, GrcPeriodType);
    }
}
