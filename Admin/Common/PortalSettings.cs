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
        /// <summary>The sign-in CAPTCHA (PortalSettings:Captcha).</summary>
        public CaptchaSettings Captcha { get; set; } = new();
        /// <summary>How often an open page re-checks the server status (seconds).</summary>
        public int StatusPollSeconds { get; set; } = 30;
        /// <summary>
        /// true once the portal is served over https:// (company certificate on Kestrel / IIS): sign-in cookie marked
        /// Secure, HSTS, http → https redirect. false (default, like v1.1.4 cookie_secure) for plain http://host:port.
        /// </summary>
        public bool UseHttps { get; set; }
    }

    /// <summary>
    /// appsettings "PortalSettings:Captcha": which generator draws the sign-in CAPTCHA and how (the VmmCaptcha.Generate
    /// parameters). QPS called Generate(3, 0, null, 30, Regular, 0) — 3 characters, no rotation, no noise lines; the
    /// defaults here are stronger.
    /// </summary>
    public sealed class CaptchaSettings
    {
        /// <summary>"Vmm" (default) = company VmmCaptcha.dll (Windows; falls back to the built-in one when it can't draw); "Builtin" = PngCaptchaGenerator.</summary>
        public string Engine { get; set; } = "Vmm";
        /// <summary>Characters, 4–6 (the answer box holds 6).</summary>
        public int Length { get; set; } = 5;
        /// <summary>Each character is rotated by a random angle within ± this many degrees.</summary>
        public int AngleDegrees { get; set; } = 15;
        public string FontName { get; set; } = "Arial";
        public int FontSize { get; set; } = 30;
        public bool Bold { get; set; } = true;
        public int NoiseLines { get; set; } = 8;
        public int NoiseDots { get; set; } = 600;

        public int LengthOrDefault => Math.Clamp(Length <= 0 ? 5 : Length, 4, 6);
        public bool UseVmm => !string.Equals(Engine?.Trim(), "Builtin", StringComparison.OrdinalIgnoreCase);
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
