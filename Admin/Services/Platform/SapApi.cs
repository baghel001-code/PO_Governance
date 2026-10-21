using Application.Interfaces.V1;
using Domain.Entities.Common;
using Domain.Entities.Request;
using Domain.Entities.Response;
using Domain.Entities.Sap;

namespace Admin.Services.Platform
{
    /// <summary>
    /// Typed calls of the SAP interface monitoring APIs (/admin/sap): extension methods on <see cref="IAPICall"/> over
    /// <c>CallingAPI(AllApiNames.PO_SAP_…, request)</c> (QPS; the rows are in API_MASTER).
    /// </summary>
    public static class SapApi
    {
        private static Task<ApiDataResponse<T>> Call<T, TReq>(IAPICall api, string task, TReq req) =>
            api.CallingAPI<ApiDataResponse<T>, TReq>(task, req);

        public static Task<ApiDataResponse<SapOverviewResponse>> SapOverviewAsync(this IAPICall api) =>
            Call<SapOverviewResponse, EmptyReq>(api, AllApiNames.PO_SAP_OVERVIEW, EmptyReq.Instance);

        public static Task<ApiDataResponse<SapDecisionListResponse>> SapDecisionsAsync(this IAPICall api, SapDecisionListReq req) =>
            Call<SapDecisionListResponse, SapDecisionListReq>(api, AllApiNames.PO_SAP_DECISION_LIST, req);

        public static Task<ApiDataResponse<SapDecisionDetailResponse>> SapDecisionAsync(this IAPICall api, long id) =>
            Call<SapDecisionDetailResponse, SapDecisionGetReq>(api, AllApiNames.PO_SAP_DECISION_GET, new SapDecisionGetReq { Id = id });

        public static Task<ApiDataResponse<SapEventListResponse>> SapEventsAsync(this IAPICall api, SapEventListReq req) =>
            Call<SapEventListResponse, SapEventListReq>(api, AllApiNames.PO_SAP_EVENT_LIST, req);

        public static Task<ApiDataResponse<SapEventRow>> SapEventAsync(this IAPICall api, long id) =>
            Call<SapEventRow, SapEventGetReq>(api, AllApiNames.PO_SAP_EVENT_GET, new SapEventGetReq { Id = id });

        public static Task<ApiDataResponse<SapTechUsersResponse>> SapTechUsersAsync(this IAPICall api) =>
            Call<SapTechUsersResponse, EmptyReq>(api, AllApiNames.PO_SAP_TECH_USER_LIST, EmptyReq.Instance);

        public static Task<ApiDataResponse<SapTechUserResponse>> SapTechUserAddAsync(this IAPICall api, SapTechUserAddReq req) =>
            Call<SapTechUserResponse, SapTechUserAddReq>(api, AllApiNames.PO_SAP_TECH_USER_ADD, req);

        public static Task<ApiDataResponse<SapTechUserResponse>> SapTechUserUpdateAsync(this IAPICall api, string userName, string action, string? password = null) =>
            Call<SapTechUserResponse, SapTechUserUpdateReq>(api, AllApiNames.PO_SAP_TECH_USER_UPDATE,
                new SapTechUserUpdateReq { UserName = userName, Action = action, Password = password });

        public static Task<ApiDataResponse<SapReasonCodesListResponse>> SapReasonCodesAsync(this IAPICall api) =>
            Call<SapReasonCodesListResponse, EmptyReq>(api, AllApiNames.PO_SAP_REASON_CODE_LIST, EmptyReq.Instance);

        public static Task<ApiDataResponse<SapReasonCodeRow>> SapReasonCodeSaveAsync(this IAPICall api, SapReasonCodeSaveReq req) =>
            Call<SapReasonCodeRow, SapReasonCodeSaveReq>(api, AllApiNames.PO_SAP_REASON_CODE_SAVE, req);

        public static Task<ApiDataResponse<SapTestDecisionResponse>> SapTestDecisionAsync(this IAPICall api, string requestJson) =>
            Call<SapTestDecisionResponse, SapTestDecisionReq>(api, AllApiNames.PO_SAP_TEST_DECISION, new SapTestDecisionReq { RequestJson = requestJson });

        public static Task<ApiDataResponse<SapTestDecisionResponse>> SapSampleRequestAsync(this IAPICall api) =>
            Call<SapTestDecisionResponse, EmptyReq>(api, AllApiNames.PO_SAP_SAMPLE_REQUEST, EmptyReq.Instance);
    }
}
