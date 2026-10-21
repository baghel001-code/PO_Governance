namespace Domain.Exceptions
{
    /// <summary>
    /// An error answer of the SAP interface (/api/sap/v1): HTTP status + error_code + message, written as the contract's
    /// Error body ({error_code, message, trace_id}) — never the portal's ApiDataResponse envelope.
    /// </summary>
    public class SapApiException : Exception
    {
        public SapApiException(int status, string errorCode, string message) : base(message)
        {
            Status = status;
            ErrorCode = errorCode;
        }

        public int Status { get; }
        public string ErrorCode { get; }
    }

    /// <summary>The store refused a second answered decision for a request_id (another call decided it first).</summary>
    public sealed class SapRequestIdTakenException : Exception
    {
        public SapRequestIdTakenException(string requestId) : base("request_id " + requestId + " has already been decided.")
        {
            RequestId = requestId;
        }

        public string RequestId { get; }
    }
}
