using System.Reflection;
using System.Xml.Linq;
using DecisionEngine.Tool1;

namespace POGovernance.Tests.Architecture
{
    /// <summary>
    /// Guards the DecisionEngine layer rule (DecisionEngine/README.md): the core business-logic library is
    /// BCL-only — no project or package references, and its compiled assembly references nothing but
    /// System.* / netstandard / mscorlib. Also checks that Domain stays independent of it.
    /// </summary>
    public class DecisionEngineArchitectureTests
    {
        private static readonly Assembly Engine = typeof(Tool1DecisionEngine).Assembly;

        private static string RepoRoot()
        {
            var d = new DirectoryInfo(AppContext.BaseDirectory);
            while (d != null && !File.Exists(Path.Combine(d.FullName, "POGovernance.sln"))) d = d.Parent;
            return d?.FullName ?? throw new InvalidOperationException("Repository root (POGovernance.sln) not found");
        }

        [Fact]
        public void Csproj_has_no_project_or_package_references()
        {
            var csproj = Path.Combine(RepoRoot(), "DecisionEngine", "DecisionEngine.csproj");
            var doc = XDocument.Load(csproj);
            var forbidden = new[] { "ProjectReference", "PackageReference", "Reference", "FrameworkReference", "COMReference" };
            var found = doc.Descendants().Where(e => forbidden.Contains(e.Name.LocalName))
                           .Select(e => e.Name.LocalName + " " + (string?)e.Attribute("Include")).ToList();
            Assert.True(found.Count == 0, "DecisionEngine.csproj must not reference anything: " + string.Join(", ", found));
        }

        [Fact]
        public void Assembly_references_only_the_BCL()
        {
            static bool IsBcl(string name) =>
                name == "System" || name.StartsWith("System.", StringComparison.Ordinal) || name == "netstandard" || name == "mscorlib";
            var outside = Engine.GetReferencedAssemblies().Select(a => a.Name ?? "").Where(n => !IsBcl(n)).ToList();
            Assert.True(outside.Count == 0, "DecisionEngine references non-BCL assemblies: " + string.Join(", ", outside));
        }

        [Fact]
        public void All_public_types_live_in_the_DecisionEngine_namespace()
        {
            var stray = Engine.GetExportedTypes().Where(t => t.Namespace == null || !(t.Namespace == "DecisionEngine" || t.Namespace.StartsWith("DecisionEngine.", StringComparison.Ordinal)))
                              .Select(t => t.FullName).ToList();
            Assert.True(stray.Count == 0, "Types outside DecisionEngine.*: " + string.Join(", ", stray));
        }

        [Fact]
        public void Domain_does_not_reference_DecisionEngine()
        {
            var domain = typeof(Domain.Exceptions.AppException).Assembly;
            Assert.DoesNotContain(domain.GetReferencedAssemblies(), a => a.Name == Engine.GetName().Name);
            var csproj = XDocument.Load(Path.Combine(RepoRoot(), "Domain", "Domain.csproj"));
            Assert.DoesNotContain(csproj.Descendants().Where(e => e.Name.LocalName == "ProjectReference"),
                                  e => ((string?)e.Attribute("Include") ?? "").Contains("DecisionEngine", StringComparison.OrdinalIgnoreCase));
        }
    }
}
