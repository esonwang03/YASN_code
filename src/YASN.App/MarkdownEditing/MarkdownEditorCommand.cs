namespace YASN.MarkdownEditing
{
    /// <summary>
    /// Lists Markdown source-editing commands available from the floating note editor.
    /// </summary>
    public enum MarkdownEditorCommand
    {
        /// <summary>
        /// Wraps the selection with Markdown bold markers.
        /// </summary>
        Bold,

        /// <summary>
        /// Wraps the selection with Markdown emphasis markers.
        /// </summary>
        Italic,

        /// <summary>
        /// Wraps the selection with inline-code markers.
        /// </summary>
        InlineCode,

        /// <summary>
        /// Converts the selection or caret placeholder to an inline Markdown link.
        /// </summary>
        Link,

        /// <summary>
        /// Prefixes the selected lines with Markdown block quote markers.
        /// </summary>
        Quote,

        /// <summary>
        /// Inserts a Markdown task checkbox line.
        /// </summary>
        TaskCheckbox,

        /// <summary>
        /// Wraps the selection with strikethrough markers (<c>~~ ~~</c>).
        /// </summary>
        Strikethrough,

        /// <summary>
        /// Wraps the selection with inserted-text markers (<c>++ ++</c>).
        /// </summary>
        Insert,

        /// <summary>
        /// Wraps the selection with highlight markers (<c>== ==</c>).
        /// </summary>
        Highlight,

        /// <summary>
        /// Wraps the selection with superscript markers (<c>^ ^</c>).
        /// </summary>
        Superscript,

        /// <summary>
        /// Wraps the selection with subscript markers (<c>~ ~</c>).
        /// </summary>
        Subscript
    }
}
