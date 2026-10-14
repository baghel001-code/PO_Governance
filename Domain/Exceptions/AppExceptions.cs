namespace Domain.Exceptions
{
    /// <summary>Base for errors that map to an HTTP status with a user-facing message.</summary>
    public class AppException : Exception
    {
        public AppException(int status, string message, object? payload = null) : base(message)
        {
            Status = status;
            Payload = payload;
        }
        public int Status { get; }
        /// <summary>Optional extra body (e.g. {conflict:true, byOthers:…} for a 409).</summary>
        public object? Payload { get; }
    }

    /// <summary>400 — bad input (server.py ValueError).</summary>
    public sealed class AppValidationException : AppException
    {
        public AppValidationException(string message) : base(400, message) { }
    }

    public sealed class ForbiddenException : AppException
    {
        public ForbiddenException(string message) : base(403, message) { }
    }

    public sealed class NotFoundException : AppException
    {
        public NotFoundException(string message) : base(404, message) { }
    }

    public sealed class ConflictException : AppException
    {
        public ConflictException(string message, object? payload = null) : base(409, message, payload) { }
    }

    /// <summary>503 — database not reachable / feature not configured.</summary>
    public sealed class ServiceUnavailableException : AppException
    {
        public ServiceUnavailableException(string message) : base(503, message) { }
    }
}
