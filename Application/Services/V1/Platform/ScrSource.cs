using Application.Interfaces.V1;
using Domain.Constants;
using Domain.Entities.Common;
using Domain.Entities.Engine;
using Domain.Entities.Store;

namespace Application.Services.V1.Platform
{
    /// <summary>server.py scr_rows(): (version, rows) of the current SCR products / Cat Head Mapping file, cached per version.</summary>
    public sealed class ScrSource : IScrSource
    {
        private readonly IStore _store;
        private sealed record Entry(string? Ver, IReadOnlyList<SheetRow> Rows);
        private volatile Entry _cache = new(null, Array.Empty<SheetRow>());   // swapped as one reference

        public ScrSource(IStore store) => _store = store;

        public async Task<(string? Version, IReadOnlyList<SheetRow> Rows)> GetScrRowsAsync(CancellationToken ct = default)
        {
            var m = await _store.GetManifestAsync(ct);
            if (!m.Datasets.TryGetValue(DatasetKeys.CatHeadMap, out var e) || e == null) return (null, Array.Empty<SheetRow>());
            var c = _cache;
            if (c.Ver != e.Version || c.Ver == null)
            {
                var data = await _store.GetBlobAsync(BlobKinds.Dataset, DatasetKeys.CatHeadMap, e.Version ?? "", ct);
                if (data == null) return (null, Array.Empty<SheetRow>());
                c = new Entry(e.Version, DatasetPayload.FromGzip(data).Rows);
                _cache = c;
            }
            return (e.Version, c.Rows);
        }
    }
}
