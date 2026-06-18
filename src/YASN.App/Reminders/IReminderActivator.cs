using YASN.AvaloniaNotes;
using YASN.Infrastructure.Reminders;

namespace YASN.Reminders
{
    /// <summary>
    /// Brings a note to the foreground when one of its reminders fires and, for an inline rule,
    /// scrolls the preview to the reminder's location. This is the in-app counterpart to the OS toast:
    /// the toast always fires, while activation is gated by a user setting. Implementations marshal to
    /// the UI thread as needed.
    /// </summary>
    public interface IReminderActivator
    {
        /// <summary>
        /// Activates the note window for a fired reminder.
        /// </summary>
        /// <param name="note">The note that owns the reminder.</param>
        /// <param name="rule">
        /// The inline rule that fired, used to locate the scroll target, or <see langword="null"/> for
        /// a note-level one-shot reminder that has no in-note anchor.
        /// </param>
        void Activate(AvaloniaNoteDocument note, NoteReminderRule? rule);
    }
}
