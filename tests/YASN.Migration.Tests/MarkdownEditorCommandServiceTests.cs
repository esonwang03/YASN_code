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

        /// <summary>
        /// Regression: a Quote command with an empty caret at the start of a line (offset just after a
        /// newline) must prefix that line rather than throw. The line end previously stepped back over
        /// the preceding newline, producing start &gt; end and an ArgumentOutOfRangeException — the crash
        /// reported for the right-click "Quote" item.
        /// </summary>
        [Fact]
        public void QuoteAtLineStartWithEmptySelectionDoesNotThrow()
        {
            TextDocument document = new TextDocument("first\nsecond");
            // Caret at offset 6 = start of "second", immediately after the '\n'.
            MarkdownEditorSelection selection = new MarkdownEditorSelection(6, 0);

            MarkdownEditorEdit edit = MarkdownEditorCommandService.Apply(document, selection, MarkdownEditorCommand.Quote);

            Assert.Equal("first\n> second", document.Text);
            Assert.Equal(new MarkdownEditorEdit(8, 0), edit);
        }

        /// <summary>
        /// Regression: Quote on an empty document must not throw and inserts the marker.
        /// </summary>
        [Fact]
        public void QuoteOnEmptyDocumentDoesNotThrow()
        {
            TextDocument document = new TextDocument(string.Empty);

            MarkdownEditorEdit edit = MarkdownEditorCommandService.Apply(document, new MarkdownEditorSelection(0, 0), MarkdownEditorCommand.Quote);

            Assert.Equal("> ", document.Text);
            Assert.Equal(new MarkdownEditorEdit(2, 0), edit);
        }

        /// <summary>
        /// Regression: Quote with an empty caret at the very end of the document prefixes the last line.
        /// </summary>
        [Fact]
        public void QuoteAtEndOfDocumentDoesNotThrow()
        {
            TextDocument document = new TextDocument("only line");

            MarkdownEditorCommandService.Apply(
                document,
                new MarkdownEditorSelection(document.TextLength, 0),
                MarkdownEditorCommand.Quote);

            Assert.Equal("> only line", document.Text);
        }

        /// <summary>
        /// A multi-line selection ending exactly on a line boundary prefixes only the spanned lines, not
        /// the empty line after the boundary.
        /// </summary>
        [Fact]
        public void QuoteMultiLineSelectionPrefixesSpannedLines()
        {
            TextDocument document = new TextDocument("one\ntwo\nthree");
            // Selection covers "one\ntwo\n": offsets 0..8 (ends at the start of "three").
            MarkdownEditorSelection selection = new MarkdownEditorSelection(0, 8);

            MarkdownEditorCommandService.Apply(document, selection, MarkdownEditorCommand.Quote);

            Assert.Equal("> one\n> two\nthree", document.Text);
        }

        /// <summary>
        /// The welcome-document inline wrap commands surround the selection with their markers.
        /// </summary>
        [Theory]
        [InlineData(MarkdownEditorCommand.Strikethrough, "~~beta~~")]
        [InlineData(MarkdownEditorCommand.Insert, "++beta++")]
        [InlineData(MarkdownEditorCommand.Highlight, "==beta==")]
        [InlineData(MarkdownEditorCommand.Superscript, "^beta^")]
        [InlineData(MarkdownEditorCommand.Subscript, "~beta~")]
        public void InlineWrapCommandsSurroundSelection(MarkdownEditorCommand command, string expectedWrapped)
        {
            TextDocument document = new TextDocument("alpha beta");
            MarkdownEditorSelection selection = new MarkdownEditorSelection(6, 4);

            MarkdownEditorCommandService.Apply(document, selection, command);

            Assert.Equal($"alpha {expectedWrapped}", document.Text);
        }
    }
}
