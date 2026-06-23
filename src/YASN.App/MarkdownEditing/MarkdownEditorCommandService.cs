using AvaloniaEdit.Document;

namespace YASN.MarkdownEditing
{
    /// <summary>
    /// Applies Markdown source-editing commands to an AvaloniaEdit document.
    /// </summary>
    public static class MarkdownEditorCommandService
    {
        /// <summary>
        /// Applies a Markdown command and returns the caret or selection state that should follow it.
        /// </summary>
        /// <param name="document">The document to edit.</param>
        /// <param name="selection">The current source selection.</param>
        /// <param name="command">The command to apply.</param>
        /// <returns>The caret and selection state to apply after the edit.</returns>
        public static MarkdownEditorEdit Apply(
            TextDocument document,
            MarkdownEditorSelection selection,
            MarkdownEditorCommand command)
        {
            MarkdownEditorSelection clamped = ClampSelection(document, selection);

            return command switch
            {
                MarkdownEditorCommand.Bold => Wrap(document, clamped, "**", "**"),
                MarkdownEditorCommand.Italic => Wrap(document, clamped, "*", "*"),
                MarkdownEditorCommand.InlineCode => Wrap(document, clamped, "`", "`"),
                MarkdownEditorCommand.Link => ApplyLink(document, clamped),
                MarkdownEditorCommand.Quote => PrefixSelectedLines(document, clamped, "> "),
                MarkdownEditorCommand.TaskCheckbox => InsertSnippet(
                    document,
                    clamped,
                    MarkdownSnippetCatalog.All.Single(snippet => snippet.Name == "Task Checkbox")),
                _ => new MarkdownEditorEdit(clamped.Start, clamped.Length)
            };
        }

        /// <summary>
        /// Inserts a snippet at the selection, replacing selected text when present.
        /// </summary>
        /// <param name="document">The document to edit.</param>
        /// <param name="selection">The current source selection.</param>
        /// <param name="snippet">The snippet to insert.</param>
        /// <returns>The caret and selection state to apply after the edit.</returns>
        public static MarkdownEditorEdit InsertSnippet(
            TextDocument document,
            MarkdownEditorSelection selection,
            MarkdownSnippet snippet)
        {
            MarkdownEditorSelection clamped = ClampSelection(document, selection);
            document.Replace(clamped.Start, clamped.Length, snippet.Text);
            int caret = clamped.Start + Math.Clamp(snippet.CaretOffset, 0, snippet.Text.Length);
            return new MarkdownEditorEdit(caret, 0);
        }

        private static MarkdownEditorEdit ApplyLink(TextDocument document, MarkdownEditorSelection selection)
        {
            string selected = document.GetText(selection.Start, selection.Length);
            string label = string.IsNullOrEmpty(selected) ? "text" : selected;
            string replacement = $"[{label}](url)";
            document.Replace(selection.Start, selection.Length, replacement);
            return new MarkdownEditorEdit(selection.Start + 1, label.Length);
        }

        private static MarkdownEditorEdit Wrap(
            TextDocument document,
            MarkdownEditorSelection selection,
            string prefix,
            string suffix)
        {
            string selected = document.GetText(selection.Start, selection.Length);
            document.Replace(selection.Start, selection.Length, prefix + selected + suffix);
            return new MarkdownEditorEdit(selection.Start + prefix.Length, selection.Length);
        }

        private static MarkdownEditorEdit PrefixSelectedLines(
            TextDocument document,
            MarkdownEditorSelection selection,
            string prefix)
        {
            string text = document.Text;
            int start = LineStartForOffset(text, selection.Start);
            int end = LineEndForSelection(text, selection.Start + selection.Length);
            string block = text[start..end];
            string[] lines = block.Split('\n');
            string replacement = string.Join("\n", lines.Select(line => prefix + line));

            document.Replace(start, end - start, replacement);
            return new MarkdownEditorEdit(selection.Start + prefix.Length, selection.Length);
        }

        private static MarkdownEditorSelection ClampSelection(TextDocument document, MarkdownEditorSelection selection)
        {
            int start = Math.Clamp(selection.Start, 0, document.TextLength);
            int length = Math.Clamp(selection.Length, 0, document.TextLength - start);
            return new MarkdownEditorSelection(start, length);
        }

        private static int LineStartForOffset(string text, int offset)
        {
            int clamped = Math.Clamp(offset, 0, text.Length);
            int newline = text.LastIndexOf('\n', Math.Max(0, clamped - 1));
            return newline < 0 ? 0 : newline + 1;
        }

        private static int LineEndForSelection(string text, int selectionEnd)
        {
            int clamped = Math.Clamp(selectionEnd, 0, text.Length);
            if (clamped > 0 && clamped <= text.Length && text[clamped - 1] == '\n')
            {
                return clamped - 1;
            }

            int newline = text.IndexOf('\n', clamped);
            return newline < 0 ? text.Length : newline;
        }
    }
}
