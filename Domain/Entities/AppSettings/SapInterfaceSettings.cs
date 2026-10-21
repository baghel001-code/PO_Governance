namespace Domain.Entities.AppSettings
{
    /// <summary>
    /// The SAP R/3 interface (<c>/api/sap/v1/*</c>, spec v1.4), bound from the "SapInterface" section of
    /// POGovApi/appsettings.json. See docs/sap/README.md.
    /// </summary>
    public sealed class SapInterfaceSettings
    {
        public const string SectionName = "SapInterface";

        /// <summary>false = every /api/sap call answers 503 SAP_INTERFACE_DISABLED (SAP continues as today).</summary>
        public bool Enabled { get; set; } = true;

        /// <summary>PRODUCTION or TEST — returned by GET /ping (spec PingResponse.environment).</summary>
        public string Environment { get; set; } = "TEST";

        /// <summary>GET /ping api_version.</summary>
        public string ApiVersion { get; set; } = "1.0.0";

        /// <summary>
        /// IP allow-list of the SAP application servers: single addresses or CIDR ranges (IPv4 / IPv6), e.g.
        /// "10.10.20.15", "10.10.21.0/24". Empty = nobody (every call is refused with 401 IP_NOT_ALLOWED).
        /// </summary>
        public List<string> AllowedIps { get; set; } = new();

        /// <summary>
        /// Reverse proxies / load balancers whose X-Forwarded-For is trusted (addresses or CIDR). X-Forwarded-For from any
        /// other peer is ignored: the TCP peer address is checked against <see cref="AllowedIps"/>.
        /// </summary>
        public List<string> TrustedProxies { get; set; } = new();

        /// <summary>0 = any port; else /api/sap/* answers 404 on every other local port (e.g. 8443 = HTTPS listener only).</summary>
        public int OnlyOnPort { get; set; }

        /// <summary>rules_version in every decision; empty = "R-" + the planner parameters version.</summary>
        public string RulesVersion { get; set; } = "";

        /// <summary>Hard cap of a /decisions call: after this the API answers 503 DECISION_TIMEOUT (SAP: NOT_CHECKED). SAP waits 3 s.</summary>
        public int DecisionTimeoutMs { get; set; } = 2500;

        /// <summary>data_delayed = true when the event feed lags more than this (spec: "e.g. 60 seconds").</summary>
        public int EventsLagLimitSeconds { get; set; } = 60;

        /// <summary>Mode of the portal's "Test decision" (SHADOW; SAP itself always sends the mode).</summary>
        public string DefaultMode { get; set; } = "SHADOW";

        /// <summary>HOLD (default, spec v1.4) or BLOCK: the verdict sent for the tool's Block decisions (handover §8 item 2).</summary>
        public string BlockVerdict { get; set; } = "HOLD";

        /// <summary>REJECT (default: 422 when any item cannot be decided) or WATCH (such items get WATCH / CANNOT_VALIDATE).</summary>
        public string UnknownItemPolicy { get; set; } = "REJECT";

        /// <summary>bm_head_required rule hook — off until the business sets the limit (handover §8 item 5).</summary>
        public SapBmHeadSettings BmHead { get; set; } = new();

        /// <summary>Failed Basic-auth logins before a technical user is locked, and for how long.</summary>
        public int MaxFailedLogins { get; set; } = 5;
        public int LockoutMinutes { get; set; } = 15;

        /// <summary>Minimum password length of a technical user (spec §2: at least 24 characters).</summary>
        public int MinPasswordLength { get; set; } = 24;

        /// <summary>Events per batch (spec: 1–500) and maximum body size (5 MB) — above: 413.</summary>
        public int MaxEventsPerBatch { get; set; } = 500;
        public int MaxBodyBytes { get; set; } = 5 * 1024 * 1024;
    }

    /// <summary>SapInterface:BmHead — see DecisionEngine.Sap.SapBmHeadRule.</summary>
    public sealed class SapBmHeadSettings
    {
        public bool Enabled { get; set; }
        public double MinNetValue { get; set; }
        public bool IncludeWatch { get; set; }
    }
}
