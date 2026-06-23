using YASN.MarkdownEditing;

namespace YASN.Migration.Tests
{
    /// <summary>
    /// Verifies the floating editor snippet catalog exposes only the Markdown snippets supported by
    /// the AvaloniaEdit source editor.
    /// </summary>
    public sealed class MarkdownEditorSnippetCatalogTests
    {
        /// <summary>
        /// Keeps explicitly excluded snippets out of the completion and insert menus.
        /// </summary>
        [Fact]
        public void CatalogExcludesDeferredSnippets()
        {
            string[] names = MarkdownSnippetCatalog.All.Select(snippet => snippet.Name).ToArray();

            Assert.DoesNotContain("Image", names);
            Assert.DoesNotContain("Table of Contents", names);
            Assert.DoesNotContain("YAML Front Matter", names);
            Assert.DoesNotContain("Unordered List", names);
            Assert.DoesNotContain("Ordered List", names);
        }

        /// <summary>
        /// Keeps the requested YASN-specific Markdown snippets available to editor commands.
        /// </summary>
        [Fact]
        public void CatalogIncludesYasnSnippets()
        {
            string[] names = MarkdownSnippetCatalog.All.Select(snippet => snippet.Name).ToArray();

            Assert.Contains("Color Text", names);
            Assert.Contains("Reminder", names);
            Assert.Contains("Task Checkbox", names);
        }
    }
}
