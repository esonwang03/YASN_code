using YASN.AvaloniaNotes;
using YASN.Infrastructure.Reminders;

namespace YASN.Reminders
{
    /// <summary>
    /// Surfaces a fired reminder in an in-app window (rendered as Markdown), in addition to the OS
    /// toast. Implementations marshal to the UI thread as needed.
    /// </summary>
    public interface IReminderPresenter
    {
        /// <summary>
        /// Presents the reminder for a note that just fired.
        /// </summary>
        /// <param name="note">The note that owns the rule.</param>
        /// <param name="rule">The rule that fired.</param>
        void Present(AvaloniaNoteDocument note, NoteReminderRule rule);
    }
}
