using Application.Interfaces.V1;
using DecisionEngine.Results.Analytics.Grc;
using Domain.Entities.Common;
using Domain.Entities.Response;

namespace Admin.Components.Pages.Grc
{
    /// <summary>
    /// "⬇ Export Row Detail" download (PO_GRC_EXPORT): the file plus the <c>X-Message</c> header ("Exported N rows")
    /// the browser showed as a toast.
    /// </summary>
    public sealed class GrcExportClient
    {
        private readonly IAPICall _api;

        public GrcExportClient(IAPICall api) => _api = api;

        public async Task<(ApiFileResponse File, string? Message)> ExportAsync(string? asOfPeriod, CancellationToken ct = default)
        {
            var file = await _api.CallingAPIDownload(AllApiNames.PO_GRC_EXPORT, new GrcImpactRequest { AsOfPeriod = asOfPeriod }, ct);
            return (file, file.IsSuccess ? file.Header("X-Message") : null);
        }
    }
}
