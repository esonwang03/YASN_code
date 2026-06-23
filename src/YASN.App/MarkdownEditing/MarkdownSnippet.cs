namespace YASN.MarkdownEditing
{
    /// <summary>
    /// Describes a Markdown snippet that can be inserted by completion or the editor context menu.
    /// </summary>
    public sealed class MarkdownSnippet
    {
        /// <summary>
        /// Initializes a Markdown snippet.
        /// </summary>
        /// <param name="name">The display name used in menus and completion.</param>
        /// <param name="text">The snippet text inserted into the document.</param>
        /// <param name="caretOffset">The caret offset relative to the start of <paramref name="text"/>.</param>
        public MarkdownSnippet(string name, string text, int caretOffset)
        {
            Name = name;
            Text = text;
            CaretOffset = caretOffset;
        }

        /// <summary>
        /// Gets the display name used in menus and completion.
        /// </summary>
        public string Name { get; }

        /// <summary>
        /// Gets the text inserted into the document.
        /// </summary>
        public string Text { get; }

        /// <summary>
        /// Gets the caret offset relative to the start of <see cref="Text"/>.
        /// </summary>
        public int CaretOffset { get; }
    }
}
