namespace Domain.Entities.Store
{
    /// <summary>
    /// Event types of the per-day "requests" log (table request_events, column event_type). A day's
    /// state is the replay of its events: {"type":"request","seq","request":{…}},
    /// {"type":"approval","seq","reqId","status","qty","comment","by","byName","at"},
    /// {"type":"clear","seq","by","at"}.
    /// </summary>
    public static class RequestEventTypes
    {
        public const string Request = "request";
        public const string Approval = "approval";
        public const string Clear = "clear";
    }

    /// <summary>approval.status values.</summary>
    public static class ApprovalStatus
    {
        public const string Pending = "pending";
        public const string Approved = "approved";
        public const string Rejected = "rejected";
        public const string Cleared = "cleared";
    }
}
