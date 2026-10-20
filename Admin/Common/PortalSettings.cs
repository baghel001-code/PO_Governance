namespace Admin.Common
{
    /// <summary>appsettings "PortalSettings". (Where POGovApi is: MasterApiSettings:Api_Url, then the API_MASTER rows.)</summary>
    public sealed class PortalSettings
    {
        /// <summary>Signed out after this long without using the site (config.json session_idle_hours).</summary>
        public double SessionIdleHours { get; set; } = 12;
        /// <summary>How often an open page re-checks the server status (seconds).</summary>
        public int StatusPollSeconds { get; set; } = 30;
    }

    /// <summary>appsettings "AppConfigurationSettings" (QPS names).</summary>
    public sealed class AppConfigurationSettings
    {
        /// <summary>1 = call POGovApi through the proxy of each API_MASTER row (Api_Proxy / Api_Port / Api_Username / Api_Pwd).</summary>
        public int ProxyValue { get; set; }
        /// <summary>How often an open page re-checks the signed-in user's session with the API (PO_AUTH_ME), seconds.</summary>
        public int UserRevalidateInSeconds { get; set; } = 60;
    }
}
