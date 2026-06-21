using System.Security.Cryptography;
using System.Text;

namespace YASN.Infrastructure.Reminders
{
    /// <summary>
    /// One reminder declared inline in note Markdown using the
    /// <c>[!display][control]{cron}{content}</c> syntax.
    /// </summary>
    public sealed class NoteReminderRule
    {
        /// <summary>Gets the Markdown shown inline in the preview (the <c>[!display]</c> segment).</summary>
        public required string DisplayText { get; init; }

        /// <summary>Gets whether the rule is active. The control segment <c>[X]</c>/<c>[x]</c> disables it.</summary>
        public required bool Enabled { get; init; }

        /// <summary>
        /// Gets whether the rule fires only once. A once rule (remaining count <c>1</c>) auto-disables
        /// after it fires, leaving a spent <c>X1</c> control in the note Markdown.
        /// </summary>
        public bool Once => RemainingCount == 1;

        /// <summary>
        /// Gets the remaining number of times this rule will fire, or <see langword="null"/> for an
        /// always-recurring rule (no digit in the control segment). Decremented after each fire; the
        /// rule is disabled once it reaches zero.
        /// </summary>
        public int? RemainingCount { get; init; }

        /// <summary>Gets whether the rule fires a finite number of times (its control carries a count).</summary>
        public bool IsFinite => RemainingCount is not null;

        /// <summary>Gets the raw cron text (the first <c>{}</c> segment).</summary>
        public required string CronText { get; init; }

        /// <summary>Gets the parsed schedule, or <see langword="null"/> when the cron text is invalid.</summary>
        public CronExpression? Schedule { get; init; }

        /// <summary>Gets the notification body (the second <c>{}</c> segment).</summary>
        public required string Content { get; init; }

        /// <summary>Gets the start offset of the token in the source content.</summary>
        public required int SourceStart { get; init; }

        /// <summary>Gets the length of the token in the source content.</summary>
        public required int SourceLength { get; init; }

        /// <summary>Gets whether this rule is eligible to be scheduled (enabled and parseable).</summary>
        public bool IsSchedulable => Enabled && Schedule is not null;

        /// <summary>
        /// Gets a stable identifier derived from the cron text and content, used to track last-fired
        /// state across edits and restarts. Independent of display text and enabled state, so toggling
        /// the control or editing the label does not reset delivery history.
        /// </summary>
        public string RuleId
        {
            get
            {
                byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes($"{CronText.Trim()}\n{Content}"));
                return Convert.ToHexString(hash, 0, 8);
            }
        }
    }
}
