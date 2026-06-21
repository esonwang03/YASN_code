using System.Text.RegularExpressions;

namespace YASN.Infrastructure.Markdown
{
    /// <summary>
    /// Rewrites Markdown task-list checkboxes (<c>- [ ]</c> / <c>- [x]</c>) on a specific source line.
    /// Used by the preview's interactive checkboxes to toggle the underlying source. Pure string
    /// transformation: no parsing beyond the single target line, no I/O.
    /// </summary>
    public static partial class NoteTaskEditor
    {
        /// <summary>
        /// Sets the checked state of the task-list item on <paramref name="sourceLine"/>, matching the
        /// 0-based <c>data-source-line</c> the preview annotates. Splits content on line boundaries,
        /// rewrites only the marker character (<c>[ ]</c> ↔ <c>[x]</c>), and preserves the bullet,
        /// indentation, and label.
        /// </summary>
        /// <param name="content">The note Markdown content.</param>
        /// <param name="sourceLine">The 0-based line index of the task item.</param>
        /// <param name="isChecked">Whether the item should become checked.</param>
        /// <param name="updated">The rewritten content when a toggle was applied.</param>
        /// <returns>
        /// <see langword="true"/> when the target line is a task item whose state changed;
        /// <see langword="false"/> (with <paramref name="updated"/> set to the original content) when the
        /// line is out of range, is not a task item, or is already in the requested state.
        /// </returns>
        public static bool TrySetChecked(string? content, int sourceLine, bool isChecked, out string updated)
        {
            updated = content ?? string.Empty;
            if (string.IsNullOrEmpty(content) || sourceLine < 0)
            {
                return false;
            }

            // Split on \n while preserving \r so CRLF content round-trips unchanged; the marker edit
            // never touches line endings.
            string[] lines = content.Split('\n');
            if (sourceLine >= lines.Length)
            {
                return false;
            }

            Match match = TaskItemRegex().Match(lines[sourceLine]);
            if (!match.Success)
            {
                return false;
            }

            Group marker = match.Groups["mark"];
            bool currentlyChecked = marker.Value is "x" or "X";
            if (currentlyChecked == isChecked)
            {
                return false;
            }

            char newMark = isChecked ? 'x' : ' ';
            lines[sourceLine] = string.Concat(
                lines[sourceLine].AsSpan(0, marker.Index),
                newMark.ToString(),
                lines[sourceLine].AsSpan(marker.Index + marker.Length));
            updated = string.Join('\n', lines);
            return true;
        }

        // A list item (-, *, + or "1." / "1)") followed by a [ ] / [x] checkbox. The marker char is the
        // capture we rewrite; leading whitespace and the bullet are preserved verbatim.
        [GeneratedRegex(@"^\s*(?:[-*+]|\d+[.)])\s+\[(?<mark>[ xX])\]")]
        private static partial Regex TaskItemRegex();
    }
}
