using System.Text.Json.Serialization;

namespace Domain.Entities.Base
{
    /// <summary>
    /// Base of every POGovApi response (QPS <c>Domain/Entities/Base/BaseResponse</c>). responseCode 0 = success;
    /// otherwise it carries the HTTP status (400, 401, 403, 404, 409, 503 …) or one of the portal-side
    /// fallback codes of <c>APICallService</c> (1000 = task name not in the API master, 1001 = HTML/XML answer,
    /// 1002 = empty answer, 1003 = API not reachable, 1004 = other client error). responseMessage holds the
    /// user-facing reason (same wording v1.1.4 returned as {"error": …}).
    /// </summary>
    public abstract class BaseResponse
    {
        public int responseCode { get; set; } = 0;
        public string? responseMessage { get; set; } = "SUCCESS";
        public int recordId { get; set; } = 0;

        /// <summary>responseCode == 0 (not serialised).</summary>
        [JsonIgnore] public bool IsSuccess => responseCode == 0;
    }
}
