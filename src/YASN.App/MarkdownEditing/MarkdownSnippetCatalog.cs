namespace YASN.MarkdownEditing
{
    /// <summary>
    /// Provides the Markdown snippets offered by the floating note source editor.
    /// </summary>
    public static class MarkdownSnippetCatalog
    {
        private static readonly IReadOnlyList<MarkdownSnippet> snippets = new List<MarkdownSnippet>
        {
            new MarkdownSnippet("Table", "| Header | Header |\n| --- | --- |\n| Cell | Cell |\n", 2),
            new MarkdownSnippet("Fenced Code Block", "```\n\n```\n", 4),
            new MarkdownSnippet("Math Block", "$$\n\n$$\n", 3),
            new MarkdownSnippet("Footnote", "[^1]\n\n[^1]: ", 11),
            new MarkdownSnippet("Horizontal Rule", "\n---\n", 5),
            new MarkdownSnippet("Paragraph Above", "\n", 0),
            new MarkdownSnippet("Paragraph Below", "\n\n", 2),
            new MarkdownSnippet("Color Text", "{#g|text}", 4),
            new MarkdownSnippet("Reminder", "[!display][]{cron}{content}", 2),
            new MarkdownSnippet("Task Checkbox", "- [ ] ", 6)
        };

        /// <summary>
        /// Gets all snippets supported by completion and insert menus.
        /// </summary>
        public static IReadOnlyList<MarkdownSnippet> All => snippets;
    }
}
