using System.Xml.Linq;

namespace POGovernance.Tests.Architecture
{
    /// <summary>
    /// The AutoMapper package (13.0.1 had known high-severity vulnerabilities) was replaced by our own mapper
    /// (Application/Mapping, README "Object mapping"). No project may bring it back, directly or through
    /// AutoMapper.Extensions.* packages.
    /// </summary>
    public class NoAutoMapperTests
    {
        private static string RepoRoot()
        {
            var d = new DirectoryInfo(AppContext.BaseDirectory);
            while (d != null && !File.Exists(Path.Combine(d.FullName, "POGovernance.sln"))) d = d.Parent;
            return d?.FullName ?? throw new InvalidOperationException("Repository root (POGovernance.sln) not found");
        }

        [Fact]
        public void No_project_references_the_AutoMapper_package()
        {
            var root = RepoRoot();
            var sep = Path.DirectorySeparatorChar;
            var files = Directory.EnumerateFiles(root, "*.csproj", SearchOption.AllDirectories)
                .Concat(Directory.EnumerateFiles(root, "Directory.*.props", SearchOption.AllDirectories))
                .Where(f => !f.Contains($"{sep}bin{sep}") && !f.Contains($"{sep}obj{sep}") && !f.Contains($"{sep}legacy{sep}"))
                .ToList();
            Assert.NotEmpty(files);

            var found = (from f in files
                         from e in XDocument.Load(f).Descendants()
                         where e.Name.LocalName is "PackageReference" or "PackageVersion" or "Reference"
                         let name = (string?)e.Attribute("Include") ?? (string?)e.Attribute("Update") ?? ""
                         where name.StartsWith("AutoMapper", StringComparison.OrdinalIgnoreCase)
                         select $"{Path.GetRelativePath(root, f)}: {name}").ToList();
            Assert.True(found.Count == 0, "Use Application.Mapping instead of AutoMapper: " + string.Join(", ", found));
        }
    }
}
