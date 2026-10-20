using System.Text.Json.Nodes;
using Application.DTOs.Platform;
using Application.Interfaces.V1;
using Domain.Entities.Common;
using Domain.Entities.Request;
using Domain.Entities.Response;
using Microsoft.AspNetCore.Components;
using MudBlazor;

namespace Admin.Services.Platform
{
    /// <summary>
    /// Typed calls of the platform APIs used by the Approvals, History, Requests, Users &amp; Security and Feedback
    /// screens: extension methods on <see cref="IAPICall"/> over <c>CallingAPI(AllApiNames.X, request)</c>.
    /// </summary>
    public static class PlatformApi
    {
        private static Task<ApiDataResponse<T>> Call<T, TReq>(IAPICall api, string task, TReq req) =>
            api.CallingAPI<ApiDataResponse<T>, TReq>(task, req);

        // ── approvals / requests ──
        public static Task<ApiDataResponse<ApprovalsResponse>> GetApprovalsAsync(this IAPICall api, int days = 120) =>
            Call<ApprovalsResponse, ApprovalsGetReq>(api, AllApiNames.PO_APPROVALS_GET, new ApprovalsGetReq { Days = days });

        public static Task<ApiDataResponse<SeqResponse>> DecideApprovalAsync(this IAPICall api, string reqId, string decision, long? qty, string comment) =>
            Call<SeqResponse, ApprovalDecideReq>(api, AllApiNames.PO_APPROVAL_DECIDE, new ApprovalDecideReq
            {
                ReqId = reqId, Decision = decision, Qty = qty is long q ? JsonValue.Create(q) : null, Comment = comment
            });

        public static Task<ApiDataResponse<RequestsDayResponse>> GetRequestsDayAsync(this IAPICall api, string? date = null) =>
            Call<RequestsDayResponse, RequestsDayReq>(api, AllApiNames.PO_REQUESTS_GET, new RequestsDayReq { Date = date });

        public static Task<ApiDataResponse<SeqResponse>> ClearRequestsAsync(this IAPICall api, string date) =>
            Call<SeqResponse, RequestsDayReq>(api, AllApiNames.PO_REQUESTS_CLEAR, new RequestsDayReq { Date = date });

        // ── history ──
        public static Task<ApiDataResponse<ItemsResponse>> GetRequestHistoryAsync(this IAPICall api, string? from, string? to) =>
            Call<ItemsResponse, DateRangeReq>(api, AllApiNames.PO_HISTORY_REQUESTS, new DateRangeReq { From = from ?? "", To = to ?? "" });

        public static Task<ApiDataResponse<UploadHistoryResponse>> GetUploadHistoryAsync(this IAPICall api, string? from, string? to) =>
            Call<UploadHistoryResponse, DateRangeReq>(api, AllApiNames.PO_HISTORY_UPLOADS, new DateRangeReq { From = from ?? "", To = to ?? "" });

        public static Task<ApiDataResponse<ItemsResponse>> GetRunsAsync(this IAPICall api, string? from, string? to) =>
            Call<ItemsResponse, DateRangeReq>(api, AllApiNames.PO_RUNS_LIST, new DateRangeReq { From = from ?? "", To = to ?? "" });

        public static Task<ApiDataResponse<OkResponse>> DeleteRunAsync(this IAPICall api, string id) =>
            Call<OkResponse, RunIdReq>(api, AllApiNames.PO_RUN_DELETE, new RunIdReq { RunId = id });

        public static Task<ApiDataResponse<UploadRestoreResponse>> RestoreUploadAsync(this IAPICall api, string uploadId) =>
            Call<UploadRestoreResponse, UploadIdReq>(api, AllApiNames.PO_UPLOAD_RESTORE, new UploadIdReq { UploadId = uploadId });

        public static Task<ApiDataResponse<DataSaveResponse>> RestoreVersionAsync(this IAPICall api, string key, string version) =>
            Call<DataSaveResponse, DataKeyReq>(api, AllApiNames.PO_DATA_RESTORE, new DataKeyReq { Key = key, Version = version });

        /// <summary>Tool 1 requests of the range (same search / decision filter as the screen) as Excel.</summary>
        public static Task<ApiFileResponse> ExportRequestsAsync(this IAPICall api, string? from, string? to, string q, string decision) =>
            api.CallingAPIDownload(AllApiNames.PO_HISTORY_EXPORT_REQUESTS, new RequestExportReq { From = from ?? "", To = to ?? "", Q = q, Decision = decision });

        /// <summary>Every stored file of one upload, one sheet each.</summary>
        public static Task<ApiFileResponse> ExportUploadAsync(this IAPICall api, string uploadId) =>
            api.CallingAPIDownload(AllApiNames.PO_HISTORY_EXPORT_UPLOAD, new UploadIdReq { UploadId = uploadId });

        /// <summary>One stored version of one data file.</summary>
        public static Task<ApiFileResponse> ExportDataVersionAsync(this IAPICall api, string key, string version, string? uploadId) =>
            api.CallingAPIDownload(AllApiNames.PO_HISTORY_EXPORT_DATA, new DataKeyReq { Key = key, Version = version, UploadId = uploadId ?? "" });

        // ── users & security ──
        public static Task<ApiDataResponse<UsersListResponse>> GetUsersAsync(this IAPICall api) =>
            Call<UsersListResponse, EmptyReq>(api, AllApiNames.PO_USERS_LIST, EmptyReq.Instance);

        public static Task<ApiDataResponse<UserResponse>> AddUserAsync(this IAPICall api, string email, string name, List<string> roles) =>
            Call<UserResponse, AddUserRequest>(api, AllApiNames.PO_USER_ADD, new AddUserRequest { Email = email, Name = name, Roles = roles });

        public static Task<ApiDataResponse<BulkUsersResponse>> BulkAddUsersAsync(this IAPICall api, List<AddUserRequest> users) =>
            Call<BulkUsersResponse, BulkUsersRequest>(api, AllApiNames.PO_USERS_BULK, new BulkUsersRequest { Users = users });

        /// <summary>Only the keys present in <paramref name="changes"/> (name, roles, active, unlock) are changed.</summary>
        public static Task<ApiDataResponse<UserResponse>> UpdateUserAsync(this IAPICall api, string email, JsonObject changes) =>
            Call<UserResponse, UserUpdateReq>(api, AllApiNames.PO_USER_UPDATE, new UserUpdateReq
            {
                Email = email,
                Name = changes["name"]?.DeepClone(),
                Roles = changes["roles"]?.DeepClone(),
                Active = changes["active"]?.DeepClone(),
                Unlock = changes["unlock"]?.DeepClone()
            });

        public static Task<ApiDataResponse<OkResponse>> ResetPasswordAsync(this IAPICall api, string email) =>
            Call<OkResponse, UserEmailReq>(api, AllApiNames.PO_USER_RESET_PASSWORD, new UserEmailReq { Email = email });

        public static Task<ApiDataResponse<OkResponse>> DeleteUserAsync(this IAPICall api, string email) =>
            Call<OkResponse, UserEmailReq>(api, AllApiNames.PO_USER_DELETE, new UserEmailReq { Email = email });

        public static Task<ApiDataResponse<ScrOverviewResponse>> GetScrAsync(this IAPICall api) =>
            Call<ScrOverviewResponse, EmptyReq>(api, AllApiNames.PO_USERS_SCR, EmptyReq.Instance);

        public static Task<ApiDataResponse<ScrSyncResponse>> SaveScrMappingAsync(this IAPICall api, Dictionary<string, string> columns) =>
            Call<ScrSyncResponse, ScrMappingRequest>(api, AllApiNames.PO_USERS_SCR_MAPPING, new ScrMappingRequest { Mapping = new() { Columns = columns } });

        public static Task<ApiDataResponse<AuditQueryResponse>> GetAuditAsync(this IAPICall api, string from, string to, string q) =>
            Call<AuditQueryResponse, AuditQueryReq>(api, AllApiNames.PO_AUDIT_QUERY, new AuditQueryReq { From = from ?? "", To = to ?? "", Q = q ?? "" });

        public static Task<ApiDataResponse<BackupListResponse>> GetBackupsAsync(this IAPICall api) =>
            Call<BackupListResponse, EmptyReq>(api, AllApiNames.PO_BACKUPS_LIST, EmptyReq.Instance);

        public static Task<ApiDataResponse<BackupResponse>> BackupNowAsync(this IAPICall api) =>
            Call<BackupResponse, EmptyReq>(api, AllApiNames.PO_BACKUP_CREATE, EmptyReq.Instance);

        // ── feedback ──
        public static Task<ApiDataResponse<ItemsResponse>> GetFeedbackAsync(this IAPICall api, bool mine) =>
            Call<ItemsResponse, FeedbackListReq>(api, AllApiNames.PO_FEEDBACK_LIST, new FeedbackListReq { Mine = mine });

        public static Task<ApiDataResponse<FeedbackCreatedResponse>> SendFeedbackAsync(this IAPICall api, string kind, string screen, string article, string message, int? rating, string view) =>
            Call<FeedbackCreatedResponse, FeedbackCreateReq>(api, AllApiNames.PO_FEEDBACK_CREATE, new FeedbackCreateReq
            {
                Kind = kind, Screen = screen, Article = article, Message = message, Rating = rating is int r ? JsonValue.Create(r) : null, View = view
            });

        public static Task<ApiDataResponse<OkResponse>> SetFeedbackStatusAsync(this IAPICall api, string id, string status, string note) =>
            Call<OkResponse, FeedbackSetStatusReq>(api, AllApiNames.PO_FEEDBACK_SET_STATUS, new FeedbackSetStatusReq { Id = id, Status = status, Note = note });
    }

    /// <summary>v1.1.4 toast() / confirm() / alert() on MudBlazor.</summary>
    public static class Ui
    {
        public static void Toast(this ISnackbar s, string? msg) => s.Add(msg ?? "", Severity.Normal);
        public static void Warn(this ISnackbar s, string? msg) => s.Add("⚠ " + msg, Severity.Warning);

        private static MarkupString Lines(string text) =>
            new(string.Join("<br>", text.Split('\n').Select(System.Net.WebUtility.HtmlEncode)));

        /// <summary>window.confirm(): true when OK was pressed.</summary>
        public static async Task<bool> Confirm(this IDialogService d, string text, string title = "Please confirm", string ok = "OK") =>
            await d.ShowMessageBox(title, Lines(text), yesText: ok, cancelText: "Cancel") == true;

        public static Task Alert(this IDialogService d, string text, string title = "") =>
            d.ShowMessageBox(title, Lines(text), yesText: "OK");
    }
}
