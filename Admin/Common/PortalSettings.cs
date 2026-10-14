namespace Admin.Common
{
    /// <summary>appsettings "PortalSettings".</summary>
    public sealed class PortalSettings
    {
        /// <summary>Base URL of POGovApi, e.g. http://localhost:5080/</summary>
        public string ApiBaseUrl { get; set; } = "http://localhost:5080/";
        /// <summary>Signed out after this long without using the site (config.json session_idle_hours).</summary>
        public double SessionIdleHours { get; set; } = 12;
        /// <summary>How often an open page re-checks the session and the server status (seconds).</summary>
        public int StatusPollSeconds { get; set; } = 30;
        public int ApiTimeoutSeconds { get; set; } = 300;
    }
}
