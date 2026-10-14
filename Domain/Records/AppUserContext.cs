using Domain.Constants;

namespace Domain.Records
{
    /// <summary>
    /// The signed-in person making an API call (server.py <c>self.me</c> plus their SCR scope).
    /// </summary>
    public sealed record AppUserContext(
        string Email,
        string Name,
        IReadOnlyList<string> Roles,
        bool MustChange,
        string? SessionToken = null,
        string? ClientIp = null)
    {
        public bool Has(params string[] roles) => roles.Any(r => Roles.Contains(r));
        /// <summary>Planner or admin — sees everything (server.py is_staff).</summary>
        public bool IsStaff => Has(Constants.Roles.Admin, Constants.Roles.Planner);
        public bool IsAdmin => Has(Constants.Roles.Admin);
        /// <summary>Name used in audit lines / "by" fields (server.py Handler.user()).</summary>
        public string AuditName => string.IsNullOrEmpty(Name) ? "unknown" : Name;
    }
}
