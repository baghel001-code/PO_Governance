namespace Admin.Common
{
    /// <summary>appsettings "PortalSettings". (Where POGovApi is: MasterApiSettings:Api_Url, then the API_MASTER rows.)</summary>
    public sealed class PortalSettings
    {
        /// <summary>
        /// Sliding life of the sign-in cookie, minutes (QPS Auth:IdleTimeoutMinutes). Keep it equal to
        /// PoGovSettings:IdleTimeoutMinutes of POGovApi, which drives the idle warning and sign-out on the page.
        /// </summary>
        public int IdleTimeoutMinutes { get; set; } = 30;
        /// <summary>Signed out this many hours after signing in, even when active (QPS Auth:AbsoluteLifetimeHours).</summary>
        public double AbsoluteLifetimeHours { get; set; } = 12;
        /// <summary>
        /// Development only (ignored in every other environment): a fixed CAPTCHA code, for automated UI tests. Empty = random.
        /// </summary>
        public string DevCaptchaCode { get; set; } = "";
        /// <summary>How often an open page re-checks the server status (seconds).</summary>
        public int StatusPollSeconds { get; set; } = 30;
        /// <summary>
        /// true once the portal is served over https:// (company certificate on Kestrel / IIS): sign-in cookie marked
        /// Secure, HSTS, http → https redirect. false (default, like v1.1.4 cookie_secure) for plain http://host:port.
        /// </summary>
        public bool UseHttps { get; set; }
    }

    /// <summary>appsettings "AppConfigurationSettings" (QPS names).</summary>
    public sealed class AppConfigurationSettings
    {
        /// <summary>1 = call POGovApi through the proxy of each API_MASTER row (Api_Proxy / Api_Port / Api_Username / Api_Pwd).</summary>
        public int ProxyValue { get; set; }
        /// <summary>How often an open page re-checks the signed-in user's session with the API (PO_AUTH_ME), seconds.</summary>
        public int UserRevalidateInSeconds { get; set; } = 60;
        /// <summary>
        /// How often the portal reloads its API list (GetAllApi), minutes: URL and token changes in API_BASE_URL /
        /// API_AUTHORIZATION reach it without a restart (POGovApi accepts a replaced token for 15 more minutes). 0 = only at start-up.
        /// </summary>
        public int ApiMasterReloadMinutes { get; set; } = 5;
    }
}
