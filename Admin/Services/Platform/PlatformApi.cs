using System.Text.Json.Nodes;
using Admin.Interfaces;
using Application.DTOs.Platform;
using Domain.Entities.Response;
using Microsoft.AspNetCore.Components;
using MudBlazor;

namespace Admin.Services.Platform
{
    /// <summary>
    /// Typed calls to the POGovApi platform routes used by the Approvals, History, Requests, Users &amp; Security
    /// and Feedback screens (extension methods on <see cref="IAPICallService"/>, so no extra DI registration).
    /// </summary>
    public static class PlatformApi
    {
        private static string E(string s) => Uri.EscapeDataString(s);
        private static string Range(string? from, string? to) => "from=" + E(from ?? "") + "&to=" + E(to ?? "");

        // ── approvals / requests ──
        public static Task<ApiResponse<ApprovalsResponse>> GetApprovalsAsync(this IAPICallService api, int days = 120) =>
            api.GetAsync<ApprovalsResponse>("api/v1/approvals?days=" + days);

        public static Task<ApiResponse<SeqResponse>> DecideApprovalAsync(this IAPICallService api, string reqId, string decision, long? qty, string comment) =>
            api.PostAsync<SeqResponse>("api/v1/approvals/" + E(reqId), new { decision, qty, comment });

        public static Task<ApiResponse<RequestsDayResponse>> GetRequestsDayAsync(this IAPICallService api, string? date = null) =>
            api.GetAsync<RequestsDayResponse>("api/v1/requests" + (string.IsNullOrEmpty(date) ? "" : "?date=" + E(date)));

        public static Task<ApiResponse<SeqResponse>> ClearRequestsAsync(this IAPICallService api, string date) =>
            api.DeleteAsync<SeqResponse>("api/v1/requests?date=" + E(date));

        // ── history ──
        public static Task<ApiResponse<ItemsResponse>> GetRequestHistoryAsync(this IAPICallService api, string? from, string? to) =>
            api.GetAsync<ItemsResponse>("api/v1/history/requests?" + Range(from, to));

        public static Task<ApiResponse<UploadHistoryResponse>> GetUploadHistoryAsync(this IAPICallService api, string? from, string? to) =>
            api.GetAsync<UploadHistoryResponse>("api/v1/history/uploads?" + Range(from, to));

        public static Task<ApiResponse<ItemsResponse>> GetRunsAsync(this IAPICallService api, string? from, string? to) =>
            api.GetAsync<ItemsResponse>("api/v1/runs?" + Range(from, to));

        public static Task<ApiResponse<OkResponse>> DeleteRunAsync(this IAPICallService api, string id) =>
            api.DeleteAsync<OkResponse>("api/v1/runs/" + E(id));

        public static Task<ApiResponse<UploadRestoreResponse>> RestoreUploadAsync(this IAPICallService api, string uploadId) =>
            api.PostAsync<UploadRestoreResponse>("api/v1/uploads/" + E(uploadId) + "/restore");

        public static Task<ApiResponse<DataSaveResponse>> RestoreVersionAsync(this IAPICallService api, string key, string version) =>
            api.PostAsync<DataSaveResponse>("api/v1/data/" + E(key) + "/restore?version=" + E(version));

        /// <summary>Tool 1 requests of the range (same search / decision filter as the screen) as Excel.</summary>
        public static Task<ApiResponse<DownloadedFile>> ExportRequestsAsync(this IAPICallService api, string? from, string? to, string q, string decision) =>
            api.DownloadAsync("api/v1/history/export/requests", new { from = from ?? "", to = to ?? "", q, decision });

        /// <summary>Every stored file of one upload, one sheet each.</summary>
        public static Task<ApiResponse<DownloadedFile>> ExportUploadAsync(this IAPICallService api, string uploadId) =>
            api.DownloadAsync("api/v1/history/export/uploads/" + E(uploadId));

        /// <summary>One stored version of one data file.</summary>
        public static Task<ApiResponse<DownloadedFile>> ExportDataVersionAsync(this IAPICallService api, string key, string version, string? uploadId) =>
            api.DownloadAsync("api/v1/history/export/data/" + E(key) + "?version=" + E(version) + "&uploadId=" + E(uploadId ?? ""));

        // ── users & security ──
        public static Task<ApiResponse<UsersListResponse>> GetUsersAsync(this IAPICallService api) => api.GetAsync<UsersListResponse>("api/v1/users");

        public static Task<ApiResponse<UserResponse>> AddUserAsync(this IAPICallService api, string email, string name, List<string> roles) =>
            api.PostAsync<UserResponse>("api/v1/users", new AddUserRequest { Email = email, Name = name, Roles = roles });

        public static Task<ApiResponse<BulkUsersResponse>> BulkAddUsersAsync(this IAPICallService api, List<AddUserRequest> users) =>
            api.PostAsync<BulkUsersResponse>("api/v1/users/bulk", new BulkUsersRequest { Users = users });

        public static Task<ApiResponse<UserResponse>> UpdateUserAsync(this IAPICallService api, string email, JsonObject changes) =>
            api.PostAsync<UserResponse>("api/v1/users/" + E(email) + "/update", changes);

        public static Task<ApiResponse<OkResponse>> ResetPasswordAsync(this IAPICallService api, string email) =>
            api.PostAsync<OkResponse>("api/v1/users/" + E(email) + "/reset-password", new { });

        public static Task<ApiResponse<OkResponse>> DeleteUserAsync(this IAPICallService api, string email) =>
            api.PostAsync<OkResponse>("api/v1/users/" + E(email) + "/delete", new { });

        public static Task<ApiResponse<ScrOverviewResponse>> GetScrAsync(this IAPICallService api) => api.GetAsync<ScrOverviewResponse>("api/v1/users/scr");

        public static Task<ApiResponse<ScrSyncResponse>> SaveScrMappingAsync(this IAPICallService api, Dictionary<string, string> columns) =>
            api.PostAsync<ScrSyncResponse>("api/v1/users/scr/mapping", new { mapping = new { columns } });

        public static Task<ApiResponse<AuditQueryResponse>> GetAuditAsync(this IAPICallService api, string from, string to, string q) =>
            api.GetAsync<AuditQueryResponse>("api/v1/audit?" + Range(from, to) + "&q=" + E(q));

        public static Task<ApiResponse<BackupListResponse>> GetBackupsAsync(this IAPICallService api) => api.GetAsync<BackupListResponse>("api/v1/backups");

        public static Task<ApiResponse<BackupResponse>> BackupNowAsync(this IAPICallService api) => api.PostAsync<BackupResponse>("api/v1/backups", new { });

        // ── feedback ──
        public static Task<ApiResponse<ItemsResponse>> GetFeedbackAsync(this IAPICallService api, bool mine) =>
            api.GetAsync<ItemsResponse>("api/v1/feedback" + (mine ? "?mine=1" : ""));

        public static Task<ApiResponse<FeedbackCreatedResponse>> SendFeedbackAsync(this IAPICallService api, string kind, string screen, string article, string message, int? rating, string view) =>
            api.PostAsync<FeedbackCreatedResponse>("api/v1/feedback", new { kind, screen, article, message, rating, view });

        public static Task<ApiResponse<OkResponse>> SetFeedbackStatusAsync(this IAPICallService api, string id, string status, string note) =>
            api.PostAsync<OkResponse>("api/v1/feedback/" + E(id), new { status, note });
    }

    /// <summary>v1.1.4 toast() / confirm() / alert() on MudBlazor.</summary>
    public static class Ui
    {
        public static void Toast(this ISnackbar s, string msg) => s.Add(msg, Severity.Normal);
        public static void Warn(this ISnackbar s, string msg) => s.Add("⚠ " + msg, Severity.Warning);

        private static MarkupString Lines(string text) =>
            new(string.Join("<br>", text.Split('\n').Select(System.Net.WebUtility.HtmlEncode)));

        /// <summary>window.confirm(): true when OK was pressed.</summary>
        public static async Task<bool> Confirm(this IDialogService d, string text, string title = "Please confirm", string ok = "OK") =>
            await d.ShowMessageBox(title, Lines(text), yesText: ok, cancelText: "Cancel") == true;

        public static Task Alert(this IDialogService d, string text, string title = "") =>
            d.ShowMessageBox(title, Lines(text), yesText: "OK");
    }
}
