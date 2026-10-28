namespace Domain.Constants
{
    public static class AppConstants
    {
        public const string AppVersion = "2.0.0";
        public const string AuthScheme = "PoGovSession";
        public const string PortalAuthScheme = "PoGovPortal";
        public const string PortalCookie = "pogov_auth";
        /// <summary>Header carrying the signed-in user's session token (the Authorization header carries the static API token).</summary>
        public const string SessionHeader = "X-Session-Token";
        public const string RestrictedHeader = "X-VMM";
    }

    /// <summary>Claim types the portal puts in its auth cookie.</summary>
    /// <summary>
    /// Why the server ended a session (SessionRecord.EndReason). POGovApi answers 401 with <see cref="Message"/>; the portal
    /// maps it back with <see cref="FromMessage"/> and shows the reason on the sign-in page (?reason=…).
    /// </summary>
    public static class SessionEndReasons
    {
        /// <summary>The same user signed in on another device (single session).</summary>
        public const string Replaced = "replaced";
        /// <summary>The admin changed the user's roles or sign-in (Employee ID).</summary>
        public const string Changed = "changed";
        /// <summary>The admin deactivated or deleted the user.</summary>
        public const string Disabled = "disabled";
        /// <summary>The password was reset by the admin or changed on another device.</summary>
        public const string Password = "password";
        /// <summary>No activity for the idle timeout.</summary>
        public const string Idle = "idle";
        /// <summary>Absolute lifetime reached, or unknown session.</summary>
        public const string Expired = "expired";

        public const string PleaseLogIn = "Please log in";

        private static readonly Dictionary<string, string> Messages = new()
        {
            [Replaced] = "Signed out: your account was signed in on another device",
            [Changed] = "Signed out: the administrator changed your access",
            [Disabled] = "Signed out: your account was deactivated",
            [Password] = "Signed out: your password was changed",
            [Idle] = "Signed out after a period of inactivity",
            [Expired] = "Signed out: your session has ended",
        };

        public static string Message(string? reason) => reason != null && Messages.TryGetValue(reason, out var m) ? m : PleaseLogIn;

        public static string? FromMessage(string? message) =>
            Messages.FirstOrDefault(kv => string.Equals(kv.Value, message, StringComparison.Ordinal)).Key;
    }

    public static class CustomClaimTypes
    {
        public const string SessionToken = "pogov:session";
        public const string MustChange = "pogov:mustchange";
        public const string Email = "pogov:email";
        /// <summary>"hrms" (Employee ID + HRMS password) or "local" (email + password kept by the tool).</summary>
        public const string LoginType = "pogov:logintype";
        public const string EmpCode = "pogov:empcode";
        /// <summary>Unix seconds of the sign-in (absolute lifetime).</summary>
        public const string AuthTime = "pogov:authtime";
        /// <summary>Idle timeout in minutes (from POGovApi).</summary>
        public const string IdleMinutes = "pogov:idlemin";
    }
}
