namespace Domain.Constants
{
    public static class AppConstants
    {
        public const string AppVersion = "2.0.0";
        public const string AuthScheme = "PoGovSession";
        public const string PortalAuthScheme = "PoGovPortal";
        public const string PortalCookie = "pogov_auth";
        public const string ApiClientName = "PoGovApi";
        public const string RestrictedHeader = "X-VMM";
    }

    /// <summary>Claim types the portal puts in its auth cookie.</summary>
    public static class CustomClaimTypes
    {
        public const string SessionToken = "pogov:session";
        public const string MustChange = "pogov:mustchange";
        public const string Email = "pogov:email";
    }
}
