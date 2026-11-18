namespace Admin.Auth
{
    /// <summary>Security headers of the portal (set for every response in Program.cs).</summary>
    public static class PortalSecurity
    {
        /// <summary>
        /// Content-Security-Policy: scripts, fonts, media and connections only from the portal itself - no inline scripts or
        /// inline event handlers, nothing loaded from other sites. Inline styles stay allowed (MudBlazor and the pages' style
        /// attributes); data: images for the CAPTCHA; ws/wss for Blazor's SignalR connection; blob: for file downloads.
        /// </summary>
        public const string ContentSecurityPolicy =
            "default-src 'self'; script-src 'self'; style-src 'self' 'unsafe-inline'; img-src 'self' data: blob:; font-src 'self' data:; " +
            "connect-src 'self' ws: wss:; media-src 'self'; object-src 'none'; base-uri 'self'; form-action 'self'; frame-ancestors 'self'";
    }
}
