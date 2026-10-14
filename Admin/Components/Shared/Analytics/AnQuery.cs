using System.Text;

namespace Admin.Components.Shared.Analytics
{
    /// <summary>Builds "path?a=1&amp;b=2" for the analytics GET endpoints (empty values are left out).</summary>
    public static class AnQuery
    {
        public static string Build(string path, params (string Name, string? Value)[] args)
        {
            var sb = new StringBuilder(path);
            var first = !path.Contains('?');
            foreach (var (name, value) in args)
            {
                if (string.IsNullOrEmpty(value)) continue;
                sb.Append(first ? '?' : '&').Append(name).Append('=').Append(Uri.EscapeDataString(value));
                first = false;
            }
            return sb.ToString();
        }

        /// <summary>The value if it is still one of the options, else "" (a vanished filter value is dropped).</summary>
        public static string Keep(string? value, IEnumerable<string> options) =>
            !string.IsNullOrEmpty(value) && options.Contains(value) ? value : "";
    }
}
