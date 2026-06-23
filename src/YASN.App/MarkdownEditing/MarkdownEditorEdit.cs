namespace YASN.MarkdownEditing
{
    /// <summary>
    /// Describes the caret and selection state to apply after a Markdown editor command.
    /// </summary>
    /// <param name="CaretOffset">The zero-based document offset where the caret should land.</param>
    /// <param name="SelectionLength">The number of characters to select from <paramref name="CaretOffset"/>.</param>
    public readonly record struct MarkdownEditorEdit(int CaretOffset, int SelectionLength);
}
