namespace YASN.Infrastructure.Reminders
{
    /// <summary>
    /// Rewrites reminder tokens inside note Markdown. Used by the scheduler to count down a finite
    /// reminder after it fires, by editing its control segment in place: decrementing the remaining
    /// count, and splicing an <c>X</c> in when the count reaches zero. Pure string transformation: no
    /// parsing of cron, no I/O.
    /// </summary>
    public static class ReminderControlEditor
    {
        /// <summary>
        /// Reduces the remaining fire count of the finite rule identified by <paramref name="ruleId"/>,
        /// rewriting its control segment in place and preserving the display, cron, content, and all
        /// surrounding text. A count above one is decremented (e.g. <c>[3]</c> becomes <c>[2]</c>); a
        /// count of one becomes spent by gaining an <c>X</c> (e.g. <c>[1]</c> becomes <c>[X1]</c>).
        /// </summary>
        /// <param name="content">The note Markdown content.</param>
        /// <param name="ruleId">The <see cref="NoteReminderRule.RuleId"/> of the rule to reduce.</param>
        /// <param name="updated">The rewritten content when a matching live finite rule was found.</param>
        /// <returns>
        /// <see langword="true"/> when a still-enabled finite rule with the given id was found and
        /// rewritten; <see langword="false"/> (with <paramref name="updated"/> set to the original
        /// content) for a recurring rule, an already-disabled rule, or no match, so callers can treat
        /// the operation as idempotent.
        /// </returns>
        public static bool TryReduceCounter(string? content, string ruleId, out string updated)
        {
            updated = content ?? string.Empty;
            if (string.IsNullOrEmpty(content) || string.IsNullOrEmpty(ruleId))
            {
                return false;
            }

            foreach (NoteReminderRule rule in NoteReminderParser.Parse(content))
            {
                if (!rule.IsFinite || !rule.Enabled || rule.RuleId != ruleId)
                {
                    continue;
                }

                if (!ReminderSyntax.TryMatch(content, rule.SourceStart, out ReminderTokenMatch match))
                {
                    continue;
                }

                string newControl = ReduceControl(match.Control, rule.RemainingCount!.Value);
                string newToken = $"[!{match.DisplayText}][{newControl}]{{{match.CronText}}}{{{match.Content}}}";
                updated = string.Concat(
                    content.AsSpan(0, match.Start),
                    newToken,
                    content.AsSpan(match.Start + match.Length));
                return true;
            }

            return false;
        }

        /// <summary>
        /// Enables or disables the rule identified by <paramref name="ruleId"/> by adding or removing the
        /// <c>X</c> flag in its control segment, preserving the display, cron, content, count, and all
        /// surrounding text.
        /// </summary>
        /// <param name="content">The note Markdown content.</param>
        /// <param name="ruleId">The <see cref="NoteReminderRule.RuleId"/> of the rule to toggle.</param>
        /// <param name="enabled">Whether the rule should become enabled.</param>
        /// <param name="updated">The rewritten content when the rule's state changed.</param>
        /// <returns>
        /// <see langword="true"/> when a matching rule was found whose enabled state changed;
        /// <see langword="false"/> (with <paramref name="updated"/> set to the original content) when
        /// there is no match or the rule is already in the requested state.
        /// </returns>
        public static bool TrySetEnabled(string? content, string ruleId, bool enabled, out string updated)
        {
            updated = content ?? string.Empty;
            if (string.IsNullOrEmpty(content) || string.IsNullOrEmpty(ruleId))
            {
                return false;
            }

            foreach (NoteReminderRule rule in NoteReminderParser.Parse(content))
            {
                if (rule.RuleId != ruleId)
                {
                    continue;
                }

                if (rule.Enabled == enabled)
                {
                    return false;
                }

                if (!ReminderSyntax.TryMatch(content, rule.SourceStart, out ReminderTokenMatch match))
                {
                    continue;
                }

                string newControl = enabled ? RemoveDisable(match.Control) : AddDisable(match.Control);
                string newToken = $"[!{match.DisplayText}][{newControl}]{{{match.CronText}}}{{{match.Content}}}";
                updated = string.Concat(
                    content.AsSpan(0, match.Start),
                    newToken,
                    content.AsSpan(match.Start + match.Length));
                return true;
            }

            return false;
        }

        /// <summary>
        /// Removes the rule identified by <paramref name="ruleId"/> from the content entirely, deleting
        /// its token and one immediately-trailing space if present so the surrounding text does not keep
        /// a dangling gap.
        /// </summary>
        /// <param name="content">The note Markdown content.</param>
        /// <param name="ruleId">The <see cref="NoteReminderRule.RuleId"/> of the rule to delete.</param>
        /// <param name="updated">The rewritten content when the rule was found and removed.</param>
        /// <returns><see langword="true"/> when a matching rule was removed; otherwise <see langword="false"/>.</returns>
        public static bool TryDelete(string? content, string ruleId, out string updated)
        {
            updated = content ?? string.Empty;
            if (string.IsNullOrEmpty(content) || string.IsNullOrEmpty(ruleId))
            {
                return false;
            }

            foreach (NoteReminderRule rule in NoteReminderParser.Parse(content))
            {
                if (rule.RuleId != ruleId)
                {
                    continue;
                }

                if (!ReminderSyntax.TryMatch(content, rule.SourceStart, out ReminderTokenMatch match))
                {
                    continue;
                }

                int end = match.Start + match.Length;

                // Absorb a single trailing space so "a [token] b" collapses to "a b", not "a  b". A
                // trailing newline is left intact so list/line structure is preserved.
                if (end < content.Length && content[end] == ' ')
                {
                    end++;
                }

                updated = string.Concat(content.AsSpan(0, match.Start), content.AsSpan(end));
                return true;
            }

            return false;
        }

        /// <summary>Adds the <c>X</c> disable flag to a control segment if not already present.</summary>
        private static string AddDisable(string control)
        {
            return control.Contains('X', StringComparison.OrdinalIgnoreCase) ? control : "X" + control;
        }

        /// <summary>Removes every <c>X</c>/<c>x</c> disable flag from a control segment.</summary>
        private static string RemoveDisable(string control)
        {
            return new string(control.Where(c => c is not ('X' or 'x')).ToArray());
        }
        /// so any surrounding flags keep their position.
        /// </summary>
        private static string ReduceControl(string control, int remaining)
        {
            // Already disabled (defensive: parser filters these out, but stay idempotent).
            if (control.Contains('X', StringComparison.OrdinalIgnoreCase))
            {
                return control;
            }

            if (remaining <= 1)
            {
                // Last fire: prefix X so a "1" once-flag becomes "X1".
                return "X" + control;
            }

            // Decrement the digit run in place (e.g. "3" -> "2"), preserving any other flags.
            string decremented = (remaining - 1).ToString(System.Globalization.CultureInfo.InvariantCulture);
            return ReplaceDigitRun(control, decremented);
        }

        /// <summary>
        /// Replaces the first contiguous run of digits in <paramref name="control"/> with
        /// <paramref name="replacement"/>, leaving non-digit flags untouched.
        /// </summary>
        private static string ReplaceDigitRun(string control, string replacement)
        {
            int start = -1;
            int end = -1;
            for (int i = 0; i < control.Length; i++)
            {
                if (char.IsDigit(control[i]))
                {
                    if (start < 0)
                    {
                        start = i;
                    }

                    end = i;
                }
                else if (start >= 0)
                {
                    break;
                }
            }

            if (start < 0)
            {
                return control;
            }

            return string.Concat(control.AsSpan(0, start), replacement, control.AsSpan(end + 1));
        }
    }
}
