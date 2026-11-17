using System.Reflection;
using System.Xml.Linq;
using Domain.Constants;

namespace POGovernance.Tests.Platform
{
    /// <summary>The shown app version comes from the assembly, i.e. from Version in Directory.Build.props.</summary>
    public class AppVersionTests
    {
        [Fact]
        public void The_app_version_is_the_assembly_version_from_Directory_Build_props()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "Directory.Build.props"))) dir = dir.Parent;
            Assert.NotNull(dir);
            var version = XDocument.Load(Path.Combine(dir!.FullName, "Directory.Build.props")).Descendants("Version").Single().Value;

            Assert.Equal(version, AppConstants.AppVersion);
            Assert.DoesNotContain("+", AppConstants.AppVersion);
            Assert.Equal(version, typeof(AppConstants).Assembly.GetName().Version!.ToString(3));
        }
    }
}
