using Application.Interfaces.V1;
using DecisionEngine.Results.Mra;
using Domain.Entities.Common;
using Domain.Entities.Response;

namespace Admin.Services.Platform
{
    /// <summary>SAP MRAs (v1.1.7): typed calls of PO_MRA_* (POGovApi MraController).</summary>
    public static class MraApi
    {
        private static Task<ApiDataResponse<T>> Call<T, TReq>(IAPICall api, string task, TReq req) =>
            api.CallingAPI<ApiDataResponse<T>, TReq>(task, req);

        public static Task<ApiDataResponse<MraUploadResponse>> MraUploadAsync(this IAPICall api, ApiUploadFile file) =>
            api.CallingAPIUpload<ApiDataResponse<MraUploadResponse>>(AllApiNames.PO_MRA_UPLOAD, new[] { file });

        public static Task<ApiDataResponse<MraStateResponse>> MraStateAsync(this IAPICall api) =>
            Call<MraStateResponse, Domain.Entities.Request.EmptyReq>(api, AllApiNames.PO_MRA_STATE, Domain.Entities.Request.EmptyReq.Instance);

        public static Task<ApiDataResponse<MraSwitchesResponse>> MraSwitchesAsync(this IAPICall api, MraSwitchesReq req) =>
            Call<MraSwitchesResponse, MraSwitchesReq>(api, AllApiNames.PO_MRA_SWITCHES, req);

        public static Task<ApiDataResponse<MraFeedResponse>> MraTestSendAsync(this IAPICall api, MraTestSendReq req) =>
            Call<MraFeedResponse, MraTestSendReq>(api, AllApiNames.PO_MRA_TEST_SEND, req);
    }
}
