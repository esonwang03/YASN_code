using AvaloniaEdit.Document;

namespace YASN.MarkdownEditing
{
    /// <summary>
    /// Provides lightweight bracket and quote completion for the Markdown source editor.
    /// </summary>
    public static class MarkdownBracketCompletion
    {
        private static readonly IReadOnlyDictionary<string, string> pairs = new Dictionary<string, string>
        {
            ["("] = ")",
            ["["] = "]",
            ["{"] = "}",
            ["\""] = "\"",
            ["'"] = "'",
            ["`"] = "`"
        };

        /// <summary>
        /// Handles a text-input token if it is an editor pair character.
        /// </summary>
        /// <param name="document">The document to edit.</param>
        /// <param name="selection">The current source selection.</param>
        /// <param name="text">The text-input token.</param>
        /// <param name="edit">The caret and selection state to apply when handled.</param>
        /// <returns><see langword="true"/> when the token was handled by bracket completion.</returns>
        public static bool TryHandleTextInput(
            TextDocument document,
            MarkdownEditorSelection selection,
            string text,
            out MarkdownEditorEdit edit)
        {
            MarkdownEditorSelection clamped = ClampSelection(document, selection);

            if (pairs.TryGetValue(text, out string? closing))
            {
                string selected = document.GetText(clamped.Start, clamped.Length);
                document.Replace(clamped.Start, clamped.Length, text + selected + closing);
                edit = clamped.Length == 0
                    ? new MarkdownEditorEdit(clamped.Start + text.Length, 0)
                    : new MarkdownEditorEdit(clamped.Start + text.Length, clamped.Length);
                return true;
            }

            if (pairs.Any(pair => pair.Value == text)
                && clamped.Length == 0
                && clamped.Start < document.TextLength
                && document.GetCharAt(clamped.Start).ToString() == text)
            {
                edit = new MarkdownEditorEdit(clamped.Start + text.Length, 0);
                return true;
            }

            edit = new MarkdownEditorEdit(clamped.Start, clamped.Length);
            return false;
        }

        private static MarkdownEditorSelection ClampSelection(TextDocument document, MarkdownEditorSelection selection)
        {
            int start = Math.Clamp(selection.Start, 0, document.TextLength);
            int length = Math.Clamp(selection.Length, 0, document.TextLength - start);
            return new MarkdownEditorSelection(start, length);
        }
    }
}
