using System.Net.Http.Headers;
using System.Net.Http.Json;
using Admin.Interfaces;
using Admin.Services;
using Domain.Constants;
using Domain.Entities.Response;
using Microsoft.AspNetCore.Components.Authorization;

namespace Admin.Components.Pages.Grc
{
    /// <summary>
    /// "⬇ Export Row Detail" download. Like <see cref="IAPICallService.DownloadAsync"/>, but it also returns
    /// the <c>X-Message</c> header ("Exported N rows") the browser showed as a toast.
    /// </summary>
    public sealed class GrcExportClient
    {
        private readonly IHttpClientFactory _factory;
        private readonly AuthenticationStateProvider _auth;
        private readonly IAPICallService _api;

        public GrcExportClient(IHttpClientFactory factory, AuthenticationStateProvider auth, IAPICallService api)
        {
            _factory = factory;
            _auth = auth;
            _api = api;
        }

        public async Task<(ApiResponse<DownloadedFile> File, string? Message)> ExportAsync(string? asOfPeriod, CancellationToken ct = default)
        {
            var state = await _auth.GetAuthenticationStateAsync();
            var token = state.User.FindFirst(CustomClaimTypes.SessionToken)?.Value;
            using var req = new HttpRequestMessage(HttpMethod.Post, "api/v1/grc-backtest/export")
            {
                Content = JsonContent.Create(new { asOfPeriod }, options: APICallService.Json)
            };
            if (token != null) req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            try
            {
                using var res = await _factory.CreateClient(AppConstants.ApiClientName).SendAsync(req, ct);
                if (!res.IsSuccessStatusCode)
                {
                    // Re-issue through the normal service to get its error mapping (ApiResponse body / status text).
                    return (await _api.DownloadAsync("api/v1/grc-backtest/export", new { asOfPeriod }, ct), null);
                }
                var bytes = await res.Content.ReadAsByteArrayAsync(ct);
                var name = res.Content.Headers.ContentDisposition?.FileNameStar ?? res.Content.Headers.ContentDisposition?.FileName ?? "GRC_Row_Detail.xlsx";
                var msg = res.Headers.TryGetValues("X-Message", out var v) ? Uri.UnescapeDataString(v.FirstOrDefault() ?? "") : null;
                var file = new DownloadedFile(name.Trim('"'), res.Content.Headers.ContentType?.MediaType ?? "application/octet-stream", bytes);
                return (ApiResponse<DownloadedFile>.Ok(file), msg);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                return (ApiResponse<DownloadedFile>.Fail(503, "Cannot reach the server. Check your network and try again."), null);
            }
        }
    }
}
