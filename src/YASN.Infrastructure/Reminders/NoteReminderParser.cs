namespace YASN.Infrastructure.Reminders
{
    /// <summary>
    /// Extracts <see cref="NoteReminderRule"/>s from note Markdown content using
    /// <see cref="ReminderSyntax"/>. Used by the scheduler to arm reminders; the Markdig extension
    /// shares the same matcher for rendering.
    /// </summary>
    public static class NoteReminderParser
    {
        /// <summary>
        /// Parses all reminder tokens in <paramref name="content"/>, in document order. Invalid cron
        /// expressions still yield a rule (with a null <see cref="NoteReminderRule.Schedule"/>) so
        /// callers can surface them; use <see cref="NoteReminderRule.IsSchedulable"/> to filter.
        /// </summary>
        /// <param name="content">The note Markdown content.</param>
        /// <returns>The reminder rules found.</returns>
        public static IReadOnlyList<NoteReminderRule> Parse(string? content)
        {
            List<NoteReminderRule> rules = new();
            if (string.IsNullOrEmpty(content))
            {
                return rules;
            }

            int i = 0;
            while (i < content.Length)
            {
                if (content[i] != '[' || !ReminderSyntax.TryMatch(content, i, out ReminderTokenMatch match))
                {
                    i++;
                    continue;
                }

                CronExpression.TryParse(match.CronText, out CronExpression? schedule);
                rules.Add(new NoteReminderRule
                {
                    DisplayText = match.DisplayText,
                    Enabled = match.Enabled,
                    Once = match.Once,
                    CronText = match.CronText,
                    Schedule = schedule,
                    Content = match.Content,
                    SourceStart = match.Start,
                    SourceLength = match.Length
                });

                i = match.Start + match.Length;
            }

            return rules;
        }
    }
}
