using Avalonia.Media;
using AvaloniaEdit.CodeCompletion;
using AvaloniaEdit.Document;
using AvaloniaEdit.Editing;

namespace YASN.MarkdownEditing
{
    /// <summary>
    /// Adapts a Markdown snippet to AvaloniaEdit's completion-list item contract.
    /// </summary>
    public sealed class MarkdownSnippetCompletionData : ICompletionData
    {
        private readonly MarkdownSnippet snippet;

        /// <summary>
        /// Initializes completion data for a Markdown snippet.
        /// </summary>
        /// <param name="snippet">The snippet represented by this completion row.</param>
        public MarkdownSnippetCompletionData(MarkdownSnippet snippet)
        {
            this.snippet = snippet;
        }

        /// <summary>
        /// Gets the optional completion icon.
        /// </summary>
        public IImage? Image => null;

        /// <summary>
        /// Gets the searchable completion text.
        /// </summary>
        public string Text => snippet.Name;

        /// <summary>
        /// Gets the visual completion-row content.
        /// </summary>
        public object Content => snippet.Name;

        /// <summary>
        /// Gets the completion description displayed by AvaloniaEdit.
        /// </summary>
        public object Description => snippet.Text;

        /// <summary>
        /// Gets the priority used by AvaloniaEdit to order completion entries.
        /// </summary>
        public double Priority => 0;

        /// <summary>
        /// Replaces the completion segment with the snippet text.
        /// </summary>
        /// <param name="textArea">The text area that owns the completion list.</param>
        /// <param name="completionSegment">The document segment being completed.</param>
        /// <param name="insertionRequestEventArgs">The event that requested insertion.</param>
        public void Complete(TextArea textArea, ISegment completionSegment, EventArgs insertionRequestEventArgs)
        {
            MarkdownEditorEdit edit = MarkdownEditorCommandService.InsertSnippet(
                textArea.Document,
                new MarkdownEditorSelection(completionSegment.Offset, completionSegment.Length),
                snippet);
            textArea.Caret.Offset = edit.CaretOffset;
        }
    }
}
