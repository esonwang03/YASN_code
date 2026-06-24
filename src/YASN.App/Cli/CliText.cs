namespace YASN.Cli
{
    /// <summary>
    /// Pure text helpers shared by the CLI content verbs (<c>note edit</c>, <c>note glance</c>). Kept
    /// free of IO and Avalonia types so the append and line-range rules are unit-testable and behave
    /// identically whether an edit runs against a live editor document or the on-disk repository.
    /// </summary>
    public static class CliText
    {
        /// <summary>
        /// Joins appended text onto existing content, inserting a single newline separator when the
        /// existing content is non-empty and does not already end with one. This keeps an append
        /// against a closed note (repository) byte-identical to an append against an open note (editor).
        /// </summary>
        /// <param name="existing">The current note content.</param>
        /// <param name="appended">The text to append.</param>
        /// <returns>The combined content.</returns>
        public static string AppendContent(string existing, string appended)
        {
            existing ??= string.Empty;
            appended ??= string.Empty;
            if (existing.Length == 0)
            {
                return appended;
            }

            string separator = existing.EndsWith('\n') ? string.Empty : "\n";
            return existing + separator + appended;
        }

        /// <summary>
        /// Splits content into lines on <c>\r\n</c>, <c>\r</c>, or <c>\n</c> without dropping empties,
        /// so line indices match what an editor would show.
        /// </summary>
        /// <param name="content">The content to split.</param>
        /// <returns>The lines, in order.</returns>
        public static string[] SplitLines(string content)
        {
            content ??= string.Empty;
            return content.Split(new[] { "\r\n", "\r", "\n" }, System.StringSplitOptions.None);
        }

        /// <summary>
        /// Parses a 1-based inclusive line range of the form <c>start-end</c> (e.g. <c>2-4</c>).
        /// A single number <c>n</c> is treated as <c>n-n</c>.
        /// </summary>
        /// <param name="text">The range token.</param>
        /// <param name="range">The parsed (start, end) range when successful.</param>
        /// <returns>
        /// <see langword="true"/> when <paramref name="text"/> is a valid range with
        /// <c>1 &lt;= start &lt;= end</c>; otherwise <see langword="false"/>.
        /// </returns>
        public static bool TryParseLineRange(string text, out (int Start, int End) range)
        {
            range = default;
            if (string.IsNullOrWhiteSpace(text))
            {
                return false;
            }

            int dash = text.IndexOf('-', System.StringComparison.Ordinal);
            if (dash < 0)
            {
                if (!int.TryParse(text, out int single) || single < 1)
                {
                    return false;
                }

                range = (single, single);
                return true;
            }

            if (!int.TryParse(text[..dash], out int start)
                || !int.TryParse(text[(dash + 1)..], out int end)
                || start < 1
                || end < start)
            {
                return false;
            }

            range = (start, end);
            return true;
        }
    }
}
