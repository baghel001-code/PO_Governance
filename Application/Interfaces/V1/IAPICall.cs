using Domain.Entities.Common;
using Domain.Entities.Response;

namespace Application.Interfaces.V1
{
    /// <summary>
    /// The portal's only way to reach data (QPS IAPICall): HTTP calls to POGovApi by task name. The task name
    /// selects the API_MASTER row (<see cref="AllApi.ApiDetails"/>: URL, method, timeout, proxy, static token);
    /// the signed-in user's session token goes in the X-Session-Token header (<see cref="ISessionTokenAccessor"/>).
    /// Never throws for HTTP or network errors: failures come back as a <c>RepT</c> (a BaseResponse) with
    /// responseCode = the HTTP status (401, 403, 409 …) and the API's message, or a fallback code
    /// 1000 (task name not in the API master), 1001 (HTML/XML answer), 1002 (empty answer),
    /// 1003 (API not reachable), 1004 (any other error).
    /// </summary>
    public interface IAPICall
    {
        /// <summary>JSON request → JSON response (QPS signature). File answers become base64 when RepT is string.</summary>
        Task<RepT> CallingAPI<RepT, ReqT>(string ApiName, ReqT requestBody);

        /// <summary>As <see cref="CallingAPI{RepT,ReqT}(string,ReqT)"/> with an explicit session token (sign-in, revalidation) and cancellation.</summary>
        Task<RepT> CallingAPI<RepT, ReqT>(string ApiName, ReqT requestBody, string? sessionToken, CancellationToken ct = default);

        /// <summary>multipart/form-data: files in the "files" field, plus text fields.</summary>
        Task<RepT> CallingAPIUpload<RepT>(string ApiName, IEnumerable<ApiUploadFile> files, IDictionary<string, string>? fields = null, CancellationToken ct = default);

        /// <summary>A file answer (Excel / CSV / zip): bytes, name, content type and the X-* headers.</summary>
        Task<ApiFileResponse> CallingAPIDownload<ReqT>(string ApiName, ReqT requestBody, CancellationToken ct = default);
    }

    /// <summary>
    /// The signed-in user's POGovApi session token (sent as X-Session-Token). The portal implements it over its
    /// authentication state, so <c>APICallService</c> stays free of Blazor types.
    /// </summary>
    public interface ISessionTokenAccessor
    {
        Task<string?> GetSessionTokenAsync();
    }

    /// <summary>API master list (API side): API_MASTER through PRO_GET_ALL_API.</summary>
    public interface IAPI
    {
        Task<ApiMasterRes> GetAllApi(CancellationToken ct = default);
        Task<ApiMasterRes> GetSpecificApiData(string? taskName, string? param, CancellationToken ct = default);
        /// <summary>The active API_BASE_URL base URL of <paramref name="baseUrlKey"/> in this environment (ends with /); null when none.</summary>
        Task<string?> GetBaseUrlAsync(string baseUrlKey, CancellationToken ct = default);
    }
}
