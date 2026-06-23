using System.Xml.Linq;

namespace YASN.Migration.Tests
{
    /// <summary>
    /// Verifies the floating note window uses structural layout for its title bar and native preview
    /// host.
    /// </summary>
    public sealed class FloatingNoteWindowLayoutTests
    {
        private static readonly XNamespace AvaloniaNamespace = "https://github.com/avaloniaui";
        private static readonly string RepositoryRoot = FindRepositoryRoot();
        private static readonly XDocument WindowDocument = XDocument.Load(
            Path.Combine(RepositoryRoot, "src", "YASN.App", "Views", "FloatingNoteWindow.axaml"));

        /// <summary>
        /// Keeps the native WebView below the title bar by assigning them to separate grid rows.
        /// </summary>
        [Fact]
        public void TitleBarAndBodyUseSeparateRows()
        {
            XElement rootGrid = WindowDocument.Root?.Elements(AvaloniaNamespace + "Grid").Single()
                ?? throw new InvalidOperationException("FloatingNoteWindow root Grid was not found.");

            Assert.Equal("Auto,*", rootGrid.Attribute("RowDefinitions")?.Value);

            XElement body = rootGrid.Elements(AvaloniaNamespace + "Grid")
                .Single(element => element.Attribute("Name")?.Value == "BodyContent"
                    || element.Attribute(XName.Get("Name", "http://schemas.microsoft.com/winfx/2006/xaml"))?.Value == "BodyContent");
            XElement titleBar = rootGrid.Elements(AvaloniaNamespace + "Border")
                .Single(element => element.Attribute("Name")?.Value == "TitleBar"
                    || element.Attribute(XName.Get("Name", "http://schemas.microsoft.com/winfx/2006/xaml"))?.Value == "TitleBar");

            Assert.Equal("1", body.Attribute("Grid.Row")?.Value);
            Assert.Equal("0", titleBar.Attribute("Grid.Row")?.Value);
            Assert.Empty(titleBar.Elements(AvaloniaNamespace + "Border.RenderTransform"));
        }

        /// <summary>
        /// Ensures the Markdown source editor uses AvaloniaEdit instead of the plain Avalonia text box.
        /// </summary>
        [Fact]
        public void EditorUsesAvaloniaEditTextEditor()
        {
            XNamespace editorNamespace = "https://github.com/avaloniaui/avaloniaedit";

            Assert.DoesNotContain(WindowDocument.Descendants(AvaloniaNamespace + "TextBox"),
                element => element.Attribute(XName.Get("Name", "http://schemas.microsoft.com/winfx/2006/xaml"))?.Value == "EditorTextBox");
            Assert.Single(WindowDocument.Descendants(editorNamespace + "TextEditor"),
                element => element.Attribute(XName.Get("Name", "http://schemas.microsoft.com/winfx/2006/xaml"))?.Value == "EditorTextEditor");
        }

        /// <summary>
        /// Ensures the app loads AvaloniaEdit's Fluent control styles.
        /// </summary>
        [Fact]
        public void AppIncludesAvaloniaEditStyles()
        {
            XDocument appDocument = XDocument.Load(
                Path.Combine(RepositoryRoot, "src", "YASN.App", "App.axaml"));

            Assert.Contains(appDocument.Descendants(AvaloniaNamespace + "StyleInclude"),
                element => element.Attribute("Source")?.Value == "avares://AvaloniaEdit/Themes/Fluent/AvaloniaEdit.xaml");
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
