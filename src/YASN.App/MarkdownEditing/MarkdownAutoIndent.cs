using AvaloniaEdit.Document;

namespace YASN.MarkdownEditing
{
    /// <summary>
    /// Handles deterministic Markdown indentation for the source editor.
    /// </summary>
    public static class MarkdownAutoIndent
    {
        /// <summary>
        /// Inserts the newline and indentation that should follow the current line.
        /// </summary>
        /// <param name="document">The document to edit.</param>
        /// <param name="selection">The current source selection.</param>
        /// <param name="edit">The caret and selection state to apply when handled.</param>
        /// <returns><see langword="true"/> when Enter was handled.</returns>
        public static bool TryHandleEnter(
            TextDocument document,
            MarkdownEditorSelection selection,
            out MarkdownEditorEdit edit)
        {
            MarkdownEditorSelection clamped = ClampSelection(document, selection);
            string text = document.Text;
            int lineStart = LineStartForOffset(text, clamped.Start);
            int lineEnd = LineEndForOffset(text, clamped.Start);
            string currentLine = text[lineStart..lineEnd];
            string indentation = ReadIndentation(currentLine);
            string continuation = indentation + ReadMarkdownPrefix(currentLine[indentation.Length..]);
            string insertion = "\n" + continuation;

            document.Replace(clamped.Start, clamped.Length, insertion);
            edit = new MarkdownEditorEdit(clamped.Start + insertion.Length, 0);
            return true;
        }

        private static string ReadIndentation(string line)
        {
            int index = 0;
            while (index < line.Length && (line[index] == ' ' || line[index] == '\t'))
            {
                index++;
            }

            return line[..index];
        }

        private static string ReadMarkdownPrefix(string lineAfterIndent)
        {
            if (lineAfterIndent.StartsWith("> ", StringComparison.Ordinal))
            {
                return "> ";
            }

            if (lineAfterIndent == ">")
            {
                return "> ";
            }

            if (lineAfterIndent.StartsWith("```", StringComparison.Ordinal)
                || lineAfterIndent.StartsWith("~~~", StringComparison.Ordinal))
            {
                return string.Empty;
            }

            return string.Empty;
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

        private static int LineEndForOffset(string text, int offset)
        {
            int clamped = Math.Clamp(offset, 0, text.Length);
            int newline = text.IndexOf('\n', clamped);
            return newline < 0 ? text.Length : newline;
        }
    }
}
