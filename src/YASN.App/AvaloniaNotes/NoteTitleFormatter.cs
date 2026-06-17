using YASN.Infrastructure.Reminders;

namespace YASN.AvaloniaNotes
{
    /// <summary>
    /// Derives display titles from Markdown note content.
    /// </summary>
    public static class NoteTitleFormatter
    {
        /// <summary>
        /// Returns the first meaningful Markdown line as a display title.
        /// </summary>
        /// <param name="content">The Markdown content to inspect.</param>
        /// <returns>A non-empty display title.</returns>
        public static string GetTitle(string? content)
        {
            if (string.IsNullOrWhiteSpace(content))
            {
                return "Untitled note";
            }

            foreach (string rawLine in content.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
            {
                // Replace any inline reminder tokens with their display text so a note that starts
                // with a reminder yields a readable title (e.g. "Standup") instead of raw cron markup.
                string line = ReplaceReminderTokens(rawLine).Trim();
                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                string title = line.TrimStart('#').Trim();
                return string.IsNullOrWhiteSpace(title) ? "Untitled note" : title;
            }

            return "Untitled note";
        }

        /// <summary>
        /// Replaces each reminder token in a line with its display text, leaving other text intact.
        /// </summary>
        /// <param name="line">A single line of Markdown content.</param>
        /// <returns>The line with reminder tokens reduced to their display text.</returns>
        private static string ReplaceReminderTokens(string line)
        {
            IReadOnlyList<NoteReminderRule> rules = NoteReminderParser.Parse(line);
            if (rules.Count == 0)
            {
                return line;
            }

            System.Text.StringBuilder builder = new System.Text.StringBuilder(line.Length);
            int cursor = 0;
            foreach (NoteReminderRule rule in rules)
            {
                builder.Append(line, cursor, rule.SourceStart - cursor);
                builder.Append(rule.DisplayText);
                cursor = rule.SourceStart + rule.SourceLength;
            }

            builder.Append(line, cursor, line.Length - cursor);
            return builder.ToString();
        }
    }
}
