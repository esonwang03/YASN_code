using AvaloniaEdit.Document;
using YASN.MarkdownEditing;

namespace YASN.Migration.Tests
{
    /// <summary>
    /// Verifies local Markdown source-editing commands used by the floating note editor.
    /// </summary>
    public sealed class MarkdownEditorCommandServiceTests
    {
        /// <summary>
        /// Bold formatting wraps the selected document range and selects the original text.
        /// </summary>
        [Fact]
        public void BoldWrapsSelection()
        {
            TextDocument document = new TextDocument("alpha beta");
            MarkdownEditorSelection selection = new MarkdownEditorSelection(6, 4);

            MarkdownEditorEdit edit = MarkdownEditorCommandService.Apply(document, selection, MarkdownEditorCommand.Bold);

            Assert.Equal("alpha **beta**", document.Text);
            Assert.Equal(new MarkdownEditorEdit(8, 4), edit);
        }

        /// <summary>
        /// Snippet insertion places the snippet at the caret and moves the caret to the snippet's
        /// declared edit point.
        /// </summary>
        [Fact]
        public void InsertsSnippetAtCaret()
        {
            TextDocument document = new TextDocument("before\nafter");
            MarkdownSnippet snippet = MarkdownSnippetCatalog.All.Single(item => item.Name == "Fenced Code Block");

            MarkdownEditorEdit edit = MarkdownEditorCommandService.InsertSnippet(
                document,
                new MarkdownEditorSelection(7, 0),
                snippet);

            Assert.Equal("before\n```\n\n```\nafter", document.Text);
            Assert.Equal(new MarkdownEditorEdit(11, 0), edit);
        }

        /// <summary>
        /// Bracket completion wraps selected text when an opening pair is typed.
        /// </summary>
        [Fact]
        public void OpeningPairWrapsSelection()
        {
            TextDocument document = new TextDocument("call value");

            bool handled = MarkdownBracketCompletion.TryHandleTextInput(
                document,
                new MarkdownEditorSelection(5, 5),
                "(",
                out MarkdownEditorEdit edit);

            Assert.True(handled);
            Assert.Equal("call (value)", document.Text);
            Assert.Equal(new MarkdownEditorEdit(6, 5), edit);
        }

        /// <summary>
        /// Enter copies current indentation and continues block quote prefixes.
        /// </summary>
        [Fact]
        public void EnterContinuesQuoteIndentation()
        {
            TextDocument document = new TextDocument("> quoted");

            bool handled = MarkdownAutoIndent.TryHandleEnter(
                document,
                new MarkdownEditorSelection(document.TextLength, 0),
                out MarkdownEditorEdit edit);

            Assert.True(handled);
            Assert.Equal("> quoted\n> ", document.Text);
            Assert.Equal(new MarkdownEditorEdit(document.TextLength, 0), edit);
        }
    }
}
