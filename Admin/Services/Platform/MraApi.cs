using Application.Interfaces.V1;
using Domain.Entities.Common;
using Domain.Entities.Response;
using Domain.Entities.Sap;

namespace Admin.Services.Platform
{
    /// <summary>Data Setup → MRA status: typed calls of PO_MRA_* (POGovApi MraController).</summary>
    public static class MraApi
    {
        private static Task<ApiDataResponse<T>> Call<T, TReq>(IAPICall api, string task, TReq req) =>
            api.CallingAPI<ApiDataResponse<T>, TReq>(task, req);

        public static Task<ApiDataResponse<MraUploadResponse>> MraUploadAsync(this IAPICall api, ApiUploadFile file) =>
            api.CallingAPIUpload<ApiDataResponse<MraUploadResponse>>(AllApiNames.PO_MRA_UPLOAD, new[] { file });

        public static Task<ApiDataResponse<MraSummaryResponse>> MraSummaryAsync(this IAPICall api) =>
            Call<MraSummaryResponse, Domain.Entities.Request.EmptyReq>(api, AllApiNames.PO_MRA_SUMMARY, Domain.Entities.Request.EmptyReq.Instance);

        public static Task<ApiDataResponse<MraListResponse>> MraListAsync(this IAPICall api, MraListReq req) =>
            Call<MraListResponse, MraListReq>(api, AllApiNames.PO_MRA_LIST, req);

        public static Task<ApiDataResponse<MraHistoryResponse>> MraHistoryAsync(this IAPICall api, string mraNo) =>
            Call<MraHistoryResponse, MraNoReq>(api, AllApiNames.PO_MRA_HISTORY, new MraNoReq { MraNo = mraNo });
    }
}
