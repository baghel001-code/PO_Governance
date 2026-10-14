namespace Domain.Entities.Response
{
    /// <summary>
    /// Envelope of every POGovApi response. ResponseCode 0 = success; otherwise it carries the HTTP
    /// status (400, 401, 403, 404, 409, 503 …) and ResponseMessage holds the user-facing reason
    /// (the same wording v1.1.4 returned as {"error": …}).
    /// </summary>
    public class ApiResponse<T>
    {
        public int ResponseCode { get; set; }
        public string ResponseMessage { get; set; } = "SUCCESS";
        public T? Data { get; set; }

        public bool IsSuccess => ResponseCode == 0;

        public static ApiResponse<T> Ok(T? data) => new() { Data = data };
        public static ApiResponse<T> Fail(int code, string message, T? data = default) =>
            new() { ResponseCode = code, ResponseMessage = message, Data = data };
    }

    public class ApiResponse : ApiResponse<object>
    {
        public static ApiResponse Success(string message = "SUCCESS") => new() { ResponseMessage = message };
    }
}
