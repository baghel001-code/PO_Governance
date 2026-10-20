namespace Admin.Components.Shared.Analytics
{
    /// <summary>Filter helpers of the analytics screens.</summary>
    public static class AnQuery
    {
        /// <summary>null for an empty filter value ("All") — what the old query string left out.</summary>
        public static string? N(string? value) => string.IsNullOrEmpty(value) ? null : value;

        /// <summary>The value if it is still one of the options, else "" (a vanished filter value is dropped).</summary>
        public static string Keep(string? value, IEnumerable<string> options) =>
            !string.IsNullOrEmpty(value) && options.Contains(value) ? value : "";
    }
}
