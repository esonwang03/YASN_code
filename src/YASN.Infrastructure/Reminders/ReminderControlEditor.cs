namespace YASN.Infrastructure.Reminders
{
    /// <summary>
    /// Rewrites reminder tokens inside note Markdown. Used by the scheduler to auto-disable a
    /// fire-once rule after it fires, by splicing an <c>X</c> into its control segment in place.
    /// Pure string transformation: no parsing of cron, no I/O.
    /// </summary>
    public static class ReminderControlEditor
    {
        /// <summary>
        /// Disables the live fire-once rule identified by <paramref name="ruleId"/> by rewriting its
        /// control segment to include <c>X</c> (e.g. <c>[1]</c> becomes <c>[X1]</c>), preserving the
        /// display, cron, content, and all surrounding text.
        /// </summary>
        /// <param name="content">The note Markdown content.</param>
        /// <param name="ruleId">The <see cref="NoteReminderRule.RuleId"/> of the rule to disable.</param>
        /// <param name="updated">The rewritten content when a matching live once-rule was found.</param>
        /// <returns>
        /// <see langword="true"/> when a still-enabled once-rule with the given id was found and
        /// rewritten; <see langword="false"/> (with <paramref name="updated"/> set to the original
        /// content) otherwise, so callers can treat the operation as idempotent.
        /// </returns>
        public static bool TryDisableOnce(string? content, string ruleId, out string updated)
        {
            updated = content ?? string.Empty;
            if (string.IsNullOrEmpty(content) || string.IsNullOrEmpty(ruleId))
            {
                return false;
            }

            foreach (NoteReminderRule rule in NoteReminderParser.Parse(content))
            {
                if (!rule.Once || !rule.Enabled || rule.RuleId != ruleId)
                {
                    continue;
                }

                if (!ReminderSyntax.TryMatch(content, rule.SourceStart, out ReminderTokenMatch match))
                {
                    continue;
                }

                string newControl = DisableControl(match.Control);
                string newToken = $"[!{match.DisplayText}][{newControl}]{{{match.CronText}}}{{{match.Content}}}";
                updated = string.Concat(
                    content.AsSpan(0, match.Start),
                    newToken,
                    content.AsSpan(match.Start + match.Length));
                return true;
            }

            return false;
        }

        private static string DisableControl(string control)
        {
            // Already disabled (defensive: parser filters these out, but stay idempotent).
            if (control.Contains('X', StringComparison.OrdinalIgnoreCase))
            {
                return control;
            }

            // Prefix X so a "1" once-flag becomes "X1"; an empty control becomes "X".
            return "X" + control;
        }
    }
}
