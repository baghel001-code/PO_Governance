using Domain.Constants;

namespace Admin.Navigation
{
    public sealed record NavItem(string Href, string Label, string Icon, string[]? Roles = null, bool ApprovalsBadge = false);

    /// <summary>The tabs of v1.1.4, per view. Roles = who sees the tab (null = everyone).</summary>
    public static class NavItems
    {
        public static readonly NavItem[] Planner =
        {
            new("/planner/data", "Data Setup", "📂", new[] { Roles.Admin, Roles.Planner }),
            new("/planner/assessment", "PO Assessment", "📋", new[] { Roles.Admin, Roles.Planner }),
            new("/planner/grc", "GRC Back Testing", "📈", new[] { Roles.Admin, Roles.Planner }),
            new("/planner/attributes", "Attribute Analysis", "🔎", new[] { Roles.Admin, Roles.Planner }),
            new("/planner/ai-insights", "AI Insights", "🤖", new[] { Roles.Admin, Roles.Planner }),
            new("/history", "History", "🗂"),
            new("/approvals", "Approvals", "✅", new[] { Roles.Admin, Roles.Planner, Roles.CatHead }, true),
            new("/admin/users", "Users", "👥", new[] { Roles.Admin }),
        };

        public static readonly NavItem[] Category =
        {
            new("/category/request", "New Request", "✏️"),
            new("/category/requests", "Requests", "📄"),
            new("/category/insights", "Insights", "📊"),
            new("/category/video", "How-to Video", "🎬"),
            new("/approvals", "Approvals", "✅", new[] { Roles.Admin, Roles.Planner, Roles.CatHead }, true),
            new("/history", "History", "🗂"),
            new("/category/how-it-works", "How it works", "❓"),
        };
    }
}
