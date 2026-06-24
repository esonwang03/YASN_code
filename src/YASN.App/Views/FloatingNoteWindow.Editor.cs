using Avalonia.Input;
using Avalonia.Interactivity;
using AvaloniaEdit.CodeCompletion;
using AvaloniaEdit.Document;
using YASN.MarkdownEditing;

namespace YASN.Views
{
    /// <summary>
    /// Markdown source-editing commands for <see cref="FloatingNoteWindow"/>.
    /// </summary>
    public sealed partial class FloatingNoteWindow
    {
        /// <summary>
        /// Cuts the current editor selection to the clipboard.
        /// </summary>
        private void HandleEditorCutClick(object? sender, RoutedEventArgs e)
        {
            editorTextEditor.Cut();
        }

        /// <summary>
        /// Copies the current editor selection to the clipboard.
        /// </summary>
        private void HandleEditorCopyClick(object? sender, RoutedEventArgs e)
        {
            editorTextEditor.Copy();
        }

        /// <summary>
        /// Pastes clipboard text into the editor.
        /// </summary>
        private void HandleEditorPasteClick(object? sender, RoutedEventArgs e)
        {
            editorTextEditor.Paste();
        }

        /// <summary>
        /// Applies Markdown bold markers to the current selection.
        /// </summary>
        private void HandleEditorBoldClick(object? sender, RoutedEventArgs e)
        {
            ApplyMarkdownCommand(MarkdownEditorCommand.Bold);
        }

        /// <summary>
        /// Applies Markdown emphasis markers to the current selection.
        /// </summary>
        private void HandleEditorItalicClick(object? sender, RoutedEventArgs e)
        {
            ApplyMarkdownCommand(MarkdownEditorCommand.Italic);
        }

        /// <summary>
        /// Applies Markdown inline-code markers to the current selection.
        /// </summary>
        private void HandleEditorInlineCodeClick(object? sender, RoutedEventArgs e)
        {
            ApplyMarkdownCommand(MarkdownEditorCommand.InlineCode);
        }

        /// <summary>
        /// Wraps the current selection with strikethrough markers.
        /// </summary>
        private void HandleEditorStrikethroughClick(object? sender, RoutedEventArgs e)
        {
            ApplyMarkdownCommand(MarkdownEditorCommand.Strikethrough);
        }

        /// <summary>
        /// Wraps the current selection with inserted-text markers.
        /// </summary>
        private void HandleEditorInsertClick(object? sender, RoutedEventArgs e)
        {
            ApplyMarkdownCommand(MarkdownEditorCommand.Insert);
        }

        /// <summary>
        /// Wraps the current selection with highlight markers.
        /// </summary>
        private void HandleEditorHighlightClick(object? sender, RoutedEventArgs e)
        {
            ApplyMarkdownCommand(MarkdownEditorCommand.Highlight);
        }

        /// <summary>
        /// Wraps the current selection with superscript markers.
        /// </summary>
        private void HandleEditorSuperscriptClick(object? sender, RoutedEventArgs e)
        {
            ApplyMarkdownCommand(MarkdownEditorCommand.Superscript);
        }

        /// <summary>
        /// Wraps the current selection with subscript markers.
        /// </summary>
        private void HandleEditorSubscriptClick(object? sender, RoutedEventArgs e)
        {
            ApplyMarkdownCommand(MarkdownEditorCommand.Subscript);
        }

        /// <summary>
        /// Converts the current selection into a Markdown link.
        /// </summary>
        private void HandleEditorLinkClick(object? sender, RoutedEventArgs e)
        {
            ApplyMarkdownCommand(MarkdownEditorCommand.Link);
        }

        /// <summary>
        /// Prefixes the current selected lines with Markdown quote markers.
        /// </summary>
        private void HandleEditorQuoteClick(object? sender, RoutedEventArgs e)
        {
            ApplyMarkdownCommand(MarkdownEditorCommand.Quote);
        }

        /// <summary>
        /// Inserts a Markdown task checkbox at the current caret.
        /// </summary>
        private void HandleEditorTaskCheckboxClick(object? sender, RoutedEventArgs e)
        {
            ApplyMarkdownCommand(MarkdownEditorCommand.TaskCheckbox);
        }

        /// <summary>
        /// Inserts a paragraph break above the caret.
        /// </summary>
        private void HandleEditorParagraphAboveClick(object? sender, RoutedEventArgs e)
        {
            InsertMarkdownSnippet("Paragraph Above");
        }

        /// <summary>
        /// Inserts a paragraph break below the caret.
        /// </summary>
        private void HandleEditorParagraphBelowClick(object? sender, RoutedEventArgs e)
        {
            InsertMarkdownSnippet("Paragraph Below");
        }

        /// <summary>
        /// Inserts a Markdown table snippet.
        /// </summary>
        private void HandleEditorInsertTableClick(object? sender, RoutedEventArgs e)
        {
            InsertMarkdownSnippet("Table");
        }

        /// <summary>
        /// Inserts a fenced code-block snippet.
        /// </summary>
        private void HandleEditorInsertCodeBlockClick(object? sender, RoutedEventArgs e)
        {
            InsertMarkdownSnippet("Fenced Code Block");
        }

        /// <summary>
        /// Inserts a block math snippet.
        /// </summary>
        private void HandleEditorInsertMathBlockClick(object? sender, RoutedEventArgs e)
        {
            InsertMarkdownSnippet("Math Block");
        }

        /// <summary>
        /// Inserts a Markdown footnote snippet.
        /// </summary>
        private void HandleEditorInsertFootnoteClick(object? sender, RoutedEventArgs e)
        {
            InsertMarkdownSnippet("Footnote");
        }

        /// <summary>
        /// Inserts a horizontal rule snippet.
        /// </summary>
        private void HandleEditorInsertHorizontalRuleClick(object? sender, RoutedEventArgs e)
        {
            InsertMarkdownSnippet("Horizontal Rule");
        }

        /// <summary>
        /// Inserts a YASN color-text snippet.
        /// </summary>
        private void HandleEditorInsertColorTextClick(object? sender, RoutedEventArgs e)
        {
            InsertMarkdownSnippet("Color Text");
        }

        /// <summary>
        /// Inserts a YASN reminder syntax snippet.
        /// </summary>
        private void HandleEditorInsertReminderClick(object? sender, RoutedEventArgs e)
        {
            InsertMarkdownSnippet("Reminder");
        }

        /// <summary>
        /// Applies an editor command to the current AvaloniaEdit document.
        /// </summary>
        /// <param name="command">The Markdown command to apply.</param>
        private void ApplyMarkdownCommand(MarkdownEditorCommand command)
        {
            TextDocument document = RequireEditorDocument();
            MarkdownEditorEdit edit = MarkdownEditorCommandService.Apply(document, CurrentEditorSelection(), command);
            ApplyEditorEdit(edit);
        }

        /// <summary>
        /// Inserts a named Markdown snippet from the shared snippet catalog.
        /// </summary>
        /// <param name="name">The snippet display name.</param>
        private void InsertMarkdownSnippet(string name)
        {
            MarkdownSnippet snippet = MarkdownSnippetCatalog.All.Single(item => item.Name == name);
            InsertMarkdownSnippet(snippet);
        }

        /// <summary>
        /// Inserts a Markdown snippet at the current selection.
        /// </summary>
        /// <param name="snippet">The snippet to insert.</param>
        private void InsertMarkdownSnippet(MarkdownSnippet snippet)
        {
            TextDocument document = RequireEditorDocument();
            MarkdownEditorEdit edit = MarkdownEditorCommandService.InsertSnippet(document, CurrentEditorSelection(), snippet);
            ApplyEditorEdit(edit);
        }

        /// <summary>
        /// Returns the current AvaloniaEdit document or fails loudly when editor setup is incomplete.
        /// </summary>
        /// <returns>The editor document.</returns>
        private TextDocument RequireEditorDocument()
        {
            return editorTextEditor.Document
                ?? throw new InvalidOperationException("The floating note editor document is not available.");
        }

        /// <summary>
        /// Captures the current source editor selection.
        /// </summary>
        /// <returns>The current editor selection.</returns>
        private MarkdownEditorSelection CurrentEditorSelection()
        {
            return new MarkdownEditorSelection(editorTextEditor.SelectionStart, editorTextEditor.SelectionLength);
        }

        /// <summary>
        /// Applies a caret and selection result after a document edit.
        /// </summary>
        /// <param name="edit">The post-edit caret and selection state.</param>
        private void ApplyEditorEdit(MarkdownEditorEdit edit)
        {
            editorTextEditor.Select(edit.CaretOffset, edit.SelectionLength);
            editorTextEditor.Focus();
            viewModel.Content = editorTextEditor.Text;
            OnCaretMoved();
        }

        /// <summary>
        /// Applies bracket completion for text-input characters before AvaloniaEdit inserts them.
        /// </summary>
        /// <param name="sender">The editor raising the text input.</param>
        /// <param name="e">The text-input event data.</param>
        private void HandleEditorTextInput(object? sender, TextInputEventArgs e)
        {
            if (string.IsNullOrEmpty(e.Text))
            {
                return;
            }

            if (MarkdownBracketCompletion.TryHandleTextInput(
                RequireEditorDocument(),
                CurrentEditorSelection(),
                e.Text,
                out MarkdownEditorEdit edit))
            {
                ApplyEditorEdit(edit);
                e.Handled = true;
            }
        }

        /// <summary>
        /// Opens the manual Markdown snippet completion list.
        /// </summary>
        private void ShowSnippetCompletion()
        {
            completionWindow?.Close();
            completionWindow = new CompletionWindow(editorTextEditor.TextArea);
            IList<ICompletionData> completionData = completionWindow.CompletionList.CompletionData;
            foreach (MarkdownSnippet snippet in MarkdownSnippetCatalog.All)
            {
                completionData.Add(new MarkdownSnippetCompletionData(snippet));
            }

            completionWindow.Closed += (_, _) => completionWindow = null;
            completionWindow.Show();
        }
    }
}
