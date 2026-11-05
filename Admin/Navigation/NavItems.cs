using Domain.Constants;

namespace Admin.Navigation
{
    public sealed record NavItem(string Href, string Label, string Icon, string[]? Roles = null, bool ApprovalsBadge = false);

    /// <summary>The tabs of v1.1.6, per view. Roles = who sees the tab (null = everyone).</summary>
    public static class NavItems
    {
        public static readonly NavItem[] Planner =
        {
            new("/planner/data", "Data Setup", "📂", new[] { Roles.Admin, Roles.Planner }),
            new("/planner/assessment", "PO Assessment", "📋", new[] { Roles.Admin, Roles.Planner }),
            new("/planner/grc", "GRC Back Testing", "📈", new[] { Roles.Admin, Roles.Planner }),
            // v1.1.6 (5 Nov 2026): Attribute Analysis and AI Insights removed from the Planner view; their screens
            // (/planner/attributes, /planner/ai-insights) stay in the portal but are not reachable from the tabs.
            new("/mra", "MRA Queues", "📦"),
            new("/history", "History", "🗂"),
            new("/approvals", "Approvals", "✅", new[] { Roles.Admin, Roles.Planner, Roles.CatHead }, true),
            new("/admin/users", "Users", "👥", new[] { Roles.Admin }),
            new("/admin/sap-feed", "SAP Feed", "🔌", new[] { Roles.Admin }),
        };

        public static readonly NavItem[] Category =
        {
            new("/category/request", "New Request", "✏️"),
            new("/category/requests", "Requests", "📄"),
            new("/category/insights", "Insights", "📊"),
            new("/category/video", "How-to Video", "🎬"),
            new("/mra", "MRA Queues", "📦"),
            new("/approvals", "Approvals", "✅", new[] { Roles.Admin, Roles.Planner, Roles.CatHead }, true),
            new("/history", "History", "🗂"),
            new("/category/how-it-works", "How it works", "❓"),
        };
    }
}
