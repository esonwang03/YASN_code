using Avalonia.Threading;
using YASN.Application;
using YASN.AvaloniaNotes;
using YASN.Infrastructure.Reminders;

namespace YASN.Reminders
{
    /// <summary>
    /// Auto-disables a fire-once reminder after it fires by rewriting the note's Markdown to a spent
    /// <c>X1</c> control. Prefers updating an open note window (so the editor and preview refresh
    /// live); otherwise persists the change through the repository. All work is marshaled to the UI
    /// thread because it touches windows and triggers rescheduling.
    /// </summary>
    public sealed class ReminderContentWriter : IReminderContentWriter
    {
        private readonly NoteRepository repository;
        private readonly INoteWindowManager windows;

        /// <summary>
        /// Initializes a reminder content writer.
        /// </summary>
        /// <param name="repository">The note repository for closed-note persistence.</param>
        /// <param name="windows">The window manager used to refresh an open note live.</param>
        public ReminderContentWriter(NoteRepository repository, INoteWindowManager windows)
        {
            this.repository = repository;
            this.windows = windows;
        }

        /// <inheritdoc/>
        public void DisableOnceRule(string noteId, string ruleId)
        {
            Dispatcher.UIThread.Post(() => Apply(noteId, ruleId));
        }

        private void Apply(string noteId, string ruleId)
        {
            AvaloniaNoteDocument? note = repository.LoadAll().FirstOrDefault(n => n.Id == noteId);
            if (note is null)
            {
                return;
            }

            if (!ReminderControlEditor.TryDisableOnce(note.Content, ruleId, out string updated))
            {
                return;
            }

            // An open window owns the live document; route through it so the editor, preview, and
            // its scheduler re-arm all stay consistent. Otherwise persist directly.
            if (!windows.TryApplyExternalContent(noteId, updated))
            {
                note.Content = updated;
                repository.Save(note);
            }
        }
    }
}
