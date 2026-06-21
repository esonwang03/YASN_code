namespace YASN.Infrastructure.Reminders
{
    /// <summary>
    /// The raw segments of a reminder token <c>[!display][control]{cron}{content}</c>, before cron
    /// parsing. Shared by the Markdig inline parser and <see cref="NoteReminderParser"/> so rendering
    /// and scheduling always agree on what is a reminder token. The control segment is a set of
    /// order-independent flags: <c>X</c> disables, <c>1</c> marks fire-once; empty (or <c>on</c>)
    /// means enabled and always-recurring.
    /// </summary>
    public readonly record struct ReminderTokenMatch(
        string DisplayText,
        string Control,
        string CronText,
        string Content,
        int Start,
        int Length)
    {
        /// <summary>Gets whether the control segment marks the rule as disabled (contains <c>X</c>).</summary>
        public bool Enabled => !Control.Contains('X', StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// Gets the remaining fire count parsed from the digit run in the control segment, or
        /// <see langword="null"/> when the control has no digits (an always-recurring rule). The count
        /// is decremented after each fire; at zero the rule is spent (its control gains an <c>X</c>).
        /// </summary>
        public int? RemainingCount =>
            int.TryParse(new string(Control.Where(char.IsDigit).ToArray()), out int count) ? count : null;

        /// <summary>Gets whether the rule fires a finite number of times (its control carries a count).</summary>
        public bool IsFinite => RemainingCount is not null;

        /// <summary>
        /// Gets whether the control segment marks the rule as fire-once (remaining count is <c>1</c>). A
        /// once rule auto-disables after firing, leaving a spent <c>X1</c> control.
        /// </summary>
        public bool Once => RemainingCount == 1;
    }

    /// <summary>
    /// Scans for the reminder token syntax. The grammar is strictly
    /// <c>[!display][control]{cron}{content}</c> with no whitespace permitted between segments, so
    /// ordinary Markdown links (<c>[text](url)</c>) and images never match.
    /// </summary>
    public static class ReminderSyntax
    {
        /// <summary>
        /// Attempts to match a reminder token beginning at <paramref name="start"/> in <paramref name="text"/>.
        /// </summary>
        /// <param name="text">The source text.</param>
        /// <param name="start">The index of the opening <c>[</c>.</param>
        /// <param name="match">The matched segments when successful.</param>
        /// <returns><see langword="true"/> when a complete token was matched.</returns>
        public static bool TryMatch(string text, int start, out ReminderTokenMatch match)
        {
            match = default;
            int i = start;

            // [!display]
            if (i >= text.Length || text[i] != '[' || i + 1 >= text.Length || text[i + 1] != '!')
            {
                return false;
            }

            i++; // past '['
            i++; // past '!'
            if (!ReadUntil(text, ref i, ']', out string display))
            {
                return false;
            }

            // [control]
            if (i >= text.Length || text[i] != '[' || !ReadBracket(text, ref i, out string control))
            {
                return false;
            }

            // {cron}
            if (i >= text.Length || text[i] != '{' || !ReadBrace(text, ref i, out string cron))
            {
                return false;
            }

            // {content}
            if (i >= text.Length || text[i] != '{' || !ReadBrace(text, ref i, out string content))
            {
                return false;
            }

            match = new ReminderTokenMatch(display, control, cron, content, start, i - start);
            return true;
        }

        private static bool ReadBracket(string text, ref int i, out string value)
        {
            // text[i] == '['
            i++;
            return ReadUntil(text, ref i, ']', out value);
        }

        private static bool ReadBrace(string text, ref int i, out string value)
        {
            // text[i] == '{'
            i++;
            return ReadUntil(text, ref i, '}', out value);
        }

        private static bool ReadUntil(string text, ref int i, char close, out string value)
        {
            int contentStart = i;
            while (i < text.Length && text[i] != close)
            {
                // Reject tokens that span newlines so a stray '[' can't swallow the rest of a note.
                if (text[i] == '\n' || text[i] == '\r')
                {
                    value = string.Empty;
                    return false;
                }

                i++;
            }

            if (i >= text.Length)
            {
                value = string.Empty;
                return false;
            }

            value = text.Substring(contentStart, i - contentStart);
            i++; // past the closing delimiter
            return true;
        }
    }
}
