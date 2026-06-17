using System.Xml.Linq;

namespace YASN.Migration.Tests
{
    /// <summary>
    /// Verifies that the active application project has moved to the Avalonia scaffold shape.
    /// </summary>
    public sealed class YasnAppProjectTests
    {
        private static readonly string RepositoryRoot = FindRepositoryRoot();
        private static readonly XDocument ProjectDocument = XDocument.Load(
            Path.Combine(RepositoryRoot, "src", "YASN.App", "YASN.App.csproj"));

        /// <summary>
        /// Confirms the executable project no longer requires the Windows-only WPF target.
        /// </summary>
        [Fact]
        public void AppProjectTargetsCrossPlatformDotNet()
        {
            Assert.Equal("net10.0", GetPropertyValue("TargetFramework"));
            Assert.Null(GetPropertyValue("UseWPF"));
            Assert.Null(GetPropertyValue("UseWindowsForms"));
        }

        /// <summary>
        /// Confirms Avalonia replaces the WPF-only UI dependencies in the active scaffold.
        /// </summary>
        [Fact]
        public void AppProjectUsesAvaloniaPackagesInsteadOfWpfPackages()
        {
            string[] packages = ProjectDocument.Descendants("PackageReference")
                .Select(reference => reference.Attribute("Include")?.Value)
                .Where(value => value is not null)
                .Cast<string>()
                .ToArray();

            Assert.Contains("Avalonia", packages);
            Assert.Contains("Avalonia.Desktop", packages);
            Assert.Contains("Avalonia.Controls.WebView", packages);
            Assert.Contains("MessageBox.Avalonia", packages);
            Assert.DoesNotContain("ModernWpfUI", packages);
            Assert.DoesNotContain("ModernWpf.MessageBox", packages);
            Assert.DoesNotContain("Microsoft.Web.WebView2", packages);
            Assert.DoesNotContain("Microsoft.Toolkit.Uwp.Notifications", packages);
            Assert.DoesNotContain("Slions.VirtualDesktop.WPF", packages);
        }

        /// <summary>
        /// Confirms shared source folders remain included by the established glob pattern.
        /// </summary>
        [Fact]
        public void AppProjectRetainsSharedSourceGlobs()
        {
            string[] compileIncludes = ProjectDocument.Descendants("Compile")
                .Select(reference => reference.Attribute("Include")?.Value)
                .Where(value => value is not null)
                .Cast<string>()
                .ToArray();

            Assert.Contains(@"..\YASN.Core\**\*.cs", compileIncludes);
            Assert.Contains(@"..\YASN.Infrastructure\**\*.cs", compileIncludes);
        }

        /// <summary>
        /// Confirms release runtime identifiers are declared for parallel restore and publish jobs.
        /// </summary>
        [Fact]
        public void AppProjectDeclaresReleaseRuntimeIdentifiers()
        {
            string[] runtimeIdentifiers = (GetPropertyValue("RuntimeIdentifiers") ?? string.Empty)
                .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            Assert.Contains("win-x64", runtimeIdentifiers);
            Assert.Contains("osx-x64", runtimeIdentifiers);
            Assert.Contains("osx-arm64", runtimeIdentifiers);
        }

        private static string? GetPropertyValue(string propertyName)
        {
            return ProjectDocument.Descendants(propertyName).SingleOrDefault()?.Value;
        }

        private static string FindRepositoryRoot()
        {
            string directory = AppContext.BaseDirectory;

            while (!string.IsNullOrWhiteSpace(directory))
            {
                string candidate = Path.Combine(directory, "YASN.sln");

                if (File.Exists(candidate))
                {
                    return directory;
                }

                string? parent = Directory.GetParent(directory)?.FullName;

                if (parent == directory)
                {
                    break;
                }

                directory = parent ?? string.Empty;
            }

            throw new DirectoryNotFoundException("Could not locate YASN.sln from the test output directory.");
        }
    }
}
