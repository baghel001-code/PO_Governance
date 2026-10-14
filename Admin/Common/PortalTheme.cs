using MudBlazor;

namespace Admin.Common
{
    /// <summary>VMM colours of v1.1.4 (navy #1A4FAF, pink #E8006E) and its fonts.</summary>
    public static class PortalTheme
    {
        public static readonly MudTheme Theme = new()
        {
            PaletteLight = new PaletteLight
            {
                Primary = "#1A4FAF",
                Secondary = "#E8006E",
                Success = "#00873E",
                Error = "#DC2626",
                Warning = "#D97706",
                Info = "#2563EB",
                AppbarBackground = "#0B1F5C",
                Background = "#EEF2FA",
                TextPrimary = "#0F1B3D",
                TextSecondary = "#4B587A",
            },
            Typography = new Typography
            {
                Default = new Default { FontFamily = new[] { "Inter", "system-ui", "sans-serif" }, FontSize = ".8125rem" },
                H6 = new H6 { FontFamily = new[] { "Barlow Condensed", "Inter", "sans-serif" }, FontWeight = 700 },
            },
            LayoutProperties = new LayoutProperties { DefaultBorderRadius = "8px" }
        };
    }
}
