namespace YASN.MarkdownEditing
{
    /// <summary>
    /// Describes a source-editor selection by document offset and length.
    /// </summary>
    /// <param name="Start">The zero-based document offset where the selection starts.</param>
    /// <param name="Length">The number of selected characters.</param>
    public readonly record struct MarkdownEditorSelection(int Start, int Length);
}
