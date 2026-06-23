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
        TaskCheckbox
    }
}
