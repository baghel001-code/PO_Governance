using Application.DTOs.Ingest;
using Domain.Entities.Common;
using Domain.Records;

namespace Application.Interfaces.V1.Ingest
{
    /// <summary>
    /// Planner "Data Setup" uploads (index.html loadFile / loadZipBundle / handleRawImport / loadGRCFile
    /// + the shared.js save). The Parse* methods are pure (no storage) and return exactly the rows the
    /// browser put into <c>S.files</c>; the Import* methods also read the current datasets (BSR
    /// accumulation, required-file check) and save the result as ONE upload batch.
    /// </summary>
    public interface IDataIngestService
    {
        /// <summary>loadFile(event, key): one or more files for one data slot. <paramref name="existingRows"/>
        /// are the current rows of a MULTI_KEYS slot (bsr), which a new upload adds to.</summary>
        IngestResult ParseFiles(string key, IReadOnlyList<IngestUpload> files, IReadOnlyList<SheetRow>? existingRows = null);

        /// <summary>loadZipBundle: one zip with every input file. <paramref name="hasCurrent"/> tells whether a
        /// dataset key already has rows on the server (for the required-file check).</summary>
        IngestResult ParseZip(IngestUpload zip, Func<string, bool>? hasCurrent = null);

        /// <summary>handleRawImport: one or more raw system exports, auto-detected per sheet.</summary>
        IngestResult ParseRaw(IReadOnlyList<IngestUpload> files);

        /// <summary>loadGRCFile: the GRC buying-history file (meta grcPeriodType, periods found).</summary>
        IngestResult ParseGrc(IngestUpload file);

        /// <summary>Creates one upload batch and saves every dataset of <paramref name="result"/>.</summary>
        Task<IngestResponse> CommitAsync(IngestResult result, AppUserContext user, CancellationToken ct = default);

        /// <summary>Parse + commit a slot upload. <paramref name="append"/>: BSR uploads add to the current rows (v1.1.4 behaviour).</summary>
        Task<IngestResponse> ImportFilesAsync(string key, IReadOnlyList<IngestUpload> files, AppUserContext user, bool append = true, CancellationToken ct = default);
        Task<IngestResponse> ImportZipAsync(IngestUpload zip, AppUserContext user, CancellationToken ct = default);
        Task<IngestResponse> ImportRawAsync(IReadOnlyList<IngestUpload> files, AppUserContext user, CancellationToken ct = default);
        Task<IngestResponse> ImportGrcAsync(IngestUpload file, AppUserContext user, CancellationToken ct = default);
    }
}
