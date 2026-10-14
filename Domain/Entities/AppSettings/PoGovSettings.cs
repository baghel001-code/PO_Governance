namespace Domain.Entities.AppSettings
{
    /// <summary>
    /// Server settings — the .NET equivalent of v1.1.4 config.json (same names, same defaults),
    /// bound from the "PoGovSettings" section of appsettings.json.
    /// </summary>
    public sealed class PoGovSettings
    {
        public const string SectionName = "PoGovSettings";

        /// <summary>Created automatically as Admin + Planner (config.json admin_email).</summary>
        public string AdminEmail { get; set; } = "kosaraju.srihari@vishalwholesale.co.in";
        public string DefaultPassword { get; set; } = "1234";
        public int PasswordMinLength { get; set; } = 8;
        public double SessionIdleHours { get; set; } = 12;
        public int MaxFailedLogins { get; set; } = 5;
        public int LockoutMinutes { get; set; } = 15;
        /// <summary>Buyers / category heads only see requests on their own MCs (SCR products file).</summary>
        public bool RestrictToOwnMcs { get; set; } = true;

        public string BackupDir { get; set; } = "backups";
        /// <summary>Nightly backup after this hour (24h clock, business time zone); negative = off.</summary>
        public int? BackupHour { get; set; } = 2;
        public int BackupKeepDays { get; set; } = 30;

        public int MaxUploadMb { get; set; } = 400;
        /// <summary>0 = keep all history forever.</summary>
        public int HistoryRetentionDays { get; set; }

        public string AnthropicApiKey { get; set; } = "";
        public string AnthropicModel { get; set; } = "claude-sonnet-5";
        public string GroqApiKey { get; set; } = "";
        public string GroqModel { get; set; } = "openai/gpt-oss-120b";
        /// <summary>e.g. http://proxy.company.local:8080 — used for the AI calls.</summary>
        public string OutboundProxy { get; set; } = "";

        /// <summary>Business time zone: "today", IDs and timestamps use it (server.py used the VM's local time).</summary>
        public string BusinessTimeZone { get; set; } = "Asia/Kolkata";

        /// <summary>Connection string; when empty, ConnectionStrings:PoGov is used.</summary>
        public string ConnectionString { get; set; } = "";
        /// <summary>How long the start-up waits for PostgreSQL (server.py init_store wait_seconds).</summary>
        public int DbWaitSeconds { get; set; } = 120;

        /// <summary>
        /// v1.1.4 rejected non-GET calls without "X-VMM: 1" (CSRF guard for the session cookie). With bearer
        /// tokens it is not needed, so it is off by default.
        /// </summary>
        public bool RequireXvmmHeader { get; set; }

        // accounts.py helpers with the config.json fallbacks ("x or default")
        public string DefaultPasswordOrDefault => string.IsNullOrEmpty(DefaultPassword) ? "1234" : DefaultPassword;
        public int PasswordMinLengthOrDefault => PasswordMinLength > 0 ? PasswordMinLength : 8;
        public double SessionIdleSeconds => (SessionIdleHours > 0 ? SessionIdleHours : 12) * 3600;
        public int MaxFailedLoginsOrDefault => MaxFailedLogins > 0 ? MaxFailedLogins : 5;
        public int LockoutMinutesOrDefault => LockoutMinutes > 0 ? LockoutMinutes : 15;
        public int BackupKeepDaysOrDefault => BackupKeepDays > 0 ? BackupKeepDays : 30;
        public string AdminEmailOrDefault => string.IsNullOrWhiteSpace(AdminEmail) ? "kosaraju.srihari@vishalwholesale.co.in" : AdminEmail;
        public bool AiChatEnabled => !string.IsNullOrEmpty(AnthropicApiKey);
        public bool AiInsightsEnabled => !string.IsNullOrEmpty(GroqApiKey) || !string.IsNullOrEmpty(AnthropicApiKey);
    }
}
