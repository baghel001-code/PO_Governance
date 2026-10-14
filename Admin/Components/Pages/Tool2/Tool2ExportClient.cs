using System.Net.Http.Headers;
using System.Net.Http.Json;
using Admin.Interfaces;
using Admin.Services;
using Application.DTOs.Tool2;
using Domain.Constants;
using Domain.Entities.Response;
using Microsoft.AspNetCore.Components.Authorization;

namespace Admin.Components.Pages.Tool2
{
    /// <summary>
    /// "⬇ Excel" / CSV of a run (POST api/v1/assessment/export). Like <see cref="IAPICallService.DownloadAsync"/>,
    /// but it also returns the <c>X-Export-Message</c> header (the v1.1.4 toast, e.g. "Excel exported ✓ — 11 sheets").
    /// </summary>
    public sealed class Tool2ExportClient
    {
        private const string Path = "api/v1/assessment/export";
        private readonly IHttpClientFactory _factory;
        private readonly AuthenticationStateProvider _auth;
        private readonly IAPICallService _api;

        public Tool2ExportClient(IHttpClientFactory factory, AuthenticationStateProvider auth, IAPICallService api)
        {
            _factory = factory;
            _auth = auth;
            _api = api;
        }

        public async Task<(ApiResponse<DownloadedFile> File, string? Message)> ExportAsync(Tool2ExportRequestDto request, CancellationToken ct = default)
        {
            var state = await _auth.GetAuthenticationStateAsync();
            var token = state.User.FindFirst(CustomClaimTypes.SessionToken)?.Value;
            using var req = new HttpRequestMessage(HttpMethod.Post, Path) { Content = JsonContent.Create(request, options: APICallService.Json) };
            if (token != null) req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            try
            {
                using var res = await _factory.CreateClient(AppConstants.ApiClientName).SendAsync(req, ct);
                if (!res.IsSuccessStatusCode)
                {
                    // Re-issue through the normal service for its error mapping (ApiResponse body → v1.1.4 toast text).
                    return (await _api.DownloadAsync(Path, request, ct), null);
                }
                var bytes = await res.Content.ReadAsByteArrayAsync(ct);
                var cd = res.Content.Headers.ContentDisposition;
                var name = cd?.FileNameStar ?? cd?.FileName ?? "PO_Assessment.xlsx";
                var msg = res.Headers.TryGetValues("X-Export-Message", out var v) ? Uri.UnescapeDataString(v.FirstOrDefault() ?? "") : null;
                var file = new DownloadedFile(name.Trim('"'), res.Content.Headers.ContentType?.MediaType ?? "application/octet-stream", bytes);
                return (ApiResponse<DownloadedFile>.Ok(file), string.IsNullOrEmpty(msg) ? null : msg);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                return (ApiResponse<DownloadedFile>.Fail(503, "Cannot reach the server. Check your network and try again."), null);
            }
        }
    }
}
