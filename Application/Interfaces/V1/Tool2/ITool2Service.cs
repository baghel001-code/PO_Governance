using Application.DTOs.Tool2;
using Domain.Records;

namespace Application.Interfaces.V1.Tool2
{
    /// <summary>
    /// Tool 2 — "📋 PO Assessment" (index.html runAssessment … exportAssXLSX, shared.js saveRun /
    /// enterHistoryRun). Runs the batch on the server on the current data, saves every run
    /// (<see cref="IRunArchive"/>) and re-opens saved runs with the data versions and parameters of that moment.
    /// </summary>
    public interface ITool2Service
    {
        /// <summary>Runs the assessment on the current data, saves the run and returns it with the dashboard.</summary>
        Task<Tool2RunResultDto> RunAsync(AppUserContext user, CancellationToken ct = default);

        /// <summary>Re-opens a saved run (v1.1.4 or this version) — the dashboard exactly as it was.</summary>
        Task<Tool2ViewDto> GetRunAsync(string runId, CancellationToken ct = default);

        /// <summary>The detail view ("Action Required — Detailed List") of a run for a tab + filter.</summary>
        Task<Tool2DetailViewDto> GetDetailAsync(string runId, Tool2DetailFilterDto filter, CancellationToken ct = default);

        /// <summary>The Vendor / Division × MC tables of a run for a filter.</summary>
        Task<Tool2RollupDto> GetRollupAsync(string runId, Tool2RollupFilterDto filter, CancellationToken ct = default);

        /// <summary>Parameters / filter lists / Category Heads (of a run when <paramref name="runId"/> is given).</summary>
        Task<Tool2OptionsDto> GetOptionsAsync(string? runId, CancellationToken ct = default);

        /// <summary>CSV or Excel of a run (per Category Head workbooks zipped when several are selected).</summary>
        Task<Tool2FileDto> ExportAsync(Tool2ExportRequestDto request, CancellationToken ct = default);
    }

    /// <summary>Writes the Tool 2 workbook (.xlsx, no gridlines) and zips several of them.</summary>
    public interface ITool2ExcelExporter
    {
        byte[] Write(Tool2WorkbookDto workbook);
        byte[] Zip(IReadOnlyList<(string FileName, byte[] Content)> files);
    }
}
