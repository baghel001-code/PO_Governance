using System.Text.Json;
using System.Text.Json.Nodes;

namespace Application.Interfaces.V1
{
    /// <summary>
    /// History tab Excel downloads (history.js, SheetJS json_to_sheet / writeFile in v1.1.4):
    /// the Tool 1 request export and stored data-file versions. Implemented in Infrastructure
    /// (ClosedXML, <c>HistoryExcelExporter</c>); register with <c>services.AddHistoryExcelExport()</c>.
    /// </summary>
    public interface IHistoryExcelExporter
    {
        /// <summary>history.js exportRequests(): one sheet "Tool 1 Requests", one row per request.</summary>
        byte[] WriteRequests(IReadOnlyList<JsonObject> requests);

        /// <summary>
        /// history.js downloadUpload() / downloadVersion(): one sheet per data file, columns = every key of
        /// the rows in first-seen order (json_to_sheet). <paramref name="single"/> = one-file download naming.
        /// </summary>
        byte[] WriteDatasets(IReadOnlyList<HistoryDatasetSheet> files, bool single);
    }

    /// <summary>One stored data-file version: its dataset key and the "rows" array of its payload.</summary>
    public sealed record HistoryDatasetSheet(string Key, JsonElement Rows);
}
