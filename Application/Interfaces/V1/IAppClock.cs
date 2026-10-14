namespace Application.Interfaces.V1
{
    /// <summary>
    /// Server clock in the business time zone (IST). "Today" for the request ledger is always the
    /// server's date, never the PC's (server.py uses datetime.now()).
    /// </summary>
    public interface IAppClock
    {
        DateTimeOffset Now { get; }
        /// <summary>yyyy-MM-dd in the business time zone.</summary>
        string Today { get; }
        /// <summary>ISO-8601 with offset, seconds precision (server.py _now()).</summary>
        string NowIso { get; }
    }
}
