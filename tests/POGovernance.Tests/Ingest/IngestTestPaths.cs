namespace POGovernance.Tests.Ingest
{
    internal static class IngestTestPaths
    {
        private static string? _root;

        /// <summary>Repository root (folder holding POGovernance.sln).</summary>
        public static string Root
        {
            get
            {
                if (_root != null) return _root;
                var d = new DirectoryInfo(AppContext.BaseDirectory);
                while (d != null && !File.Exists(Path.Combine(d.FullName, "POGovernance.sln"))) d = d.Parent;
                return _root = d?.FullName ?? throw new InvalidOperationException("Repository root not found");
            }
        }

        public static string Fixtures => Path.Combine(Root, "tests", "POGovernance.Tests", "Ingest", "Fixtures");
        public static string Expected => Path.Combine(Root, "tests", "POGovernance.Tests", "Ingest", "Expected");
        public static string Fixture(string name) => Path.Combine(Fixtures, name);
    }
}
