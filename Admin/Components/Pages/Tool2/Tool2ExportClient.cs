using Application.Interfaces.V1;
using DecisionEngine.Results.Tool2;
using Domain.Entities.Common;
using Domain.Entities.Response;

namespace Admin.Components.Pages.Tool2
{
    /// <summary>
    /// "⬇ Excel" / CSV of a run (PO_TOOL2_EXPORT): the file plus the <c>X-Export-Message</c> header
    /// (the v1.1.4 toast, e.g. "Excel exported ✓ — 11 sheets").
    /// </summary>
    public sealed class Tool2ExportClient
    {
        private readonly IAPICall _api;

        public Tool2ExportClient(IAPICall api) => _api = api;

        public async Task<(ApiFileResponse File, string? Message)> ExportAsync(Tool2ExportRequestDto request, CancellationToken ct = default)
        {
            var file = await _api.CallingAPIDownload(AllApiNames.PO_TOOL2_EXPORT, request, ct);
            return (file, file.IsSuccess ? file.Header("X-Export-Message") : null);
        }
    }
}
