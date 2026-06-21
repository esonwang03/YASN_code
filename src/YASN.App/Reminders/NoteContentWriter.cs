using Avalonia.Threading;
using YASN.Application;
using YASN.AvaloniaNotes;
using YASN.Infrastructure.Markdown;
using YASN.Infrastructure.Reminders;

namespace YASN.Reminders
{
    /// <summary>
    /// Applies programmatic edits to a note's Markdown. Prefers updating an open note window (so the
    /// editor and preview refresh live and the edit composes with unsaved changes); otherwise persists
    /// through the repository. All work is marshaled to the UI thread because it touches windows and
    /// triggers rescheduling.
    /// </summary>
    public sealed class NoteContentWriter : INoteContentWriter
    {
        private readonly NoteRepository repository;
        private readonly INoteWindowManager windows;

        /// <summary>
        /// Initializes a note content writer.
        /// </summary>
        /// <param name="repository">The note repository for closed-note persistence.</param>
        /// <param name="windows">The window manager used to refresh an open note live.</param>
        public NoteContentWriter(NoteRepository repository, INoteWindowManager windows)
        {
            this.repository = repository;
            this.windows = windows;
        }

        /// <inheritdoc/>
        public void ReduceReminderCounter(string noteId, string ruleId)
        {
            Apply(noteId, content =>
                ReminderControlEditor.TryReduceCounter(content, ruleId, out string updated) ? updated : null);
        }

        /// <inheritdoc/>
        public void SetTaskChecked(string noteId, int sourceLine, bool isChecked)
        {
            Apply(noteId, content =>
                NoteTaskEditor.TrySetChecked(content, sourceLine, isChecked, out string updated) ? updated : null);
        }

        /// <summary>
        /// Runs <paramref name="transform"/> against the note's content on the UI thread. A
        /// <see langword="null"/> result is a no-op. An open window applies the transform to its live
        /// content; otherwise it is applied to the stored content and saved.
        /// </summary>
        private void Apply(string noteId, Func<string, string?> transform)
        {
            Dispatcher.UIThread.Post(() =>
            {
                // An open window owns the live document; route through it so the editor, preview, and
                // scheduler re-arm all stay consistent and the edit composes with unsaved text.
                if (windows.TryEditContent(noteId, transform))
                {
                    return;
                }

                AvaloniaNoteDocument? note = repository.LoadAll().FirstOrDefault(n => n.Id == noteId);
                if (note is null)
                {
                    return;
                }

                if (transform(note.Content) is { } updated)
                {
                    note.Content = updated;
                    repository.Save(note);
                }
            });
        }
    }
}
