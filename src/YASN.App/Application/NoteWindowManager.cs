using YASN.AvaloniaNotes;
using YASN.Hotkeys;
using YASN.PlatformServices;
using YASN.Reminders;
using YASN.ViewModels;
using YASN.Views;

namespace YASN.Application
{
    /// <summary>
    /// Owns the live set of open note windows and their open/close lifecycle so both the tray and
    /// the note manager window drive the same windows.
    /// </summary>
    public sealed class NoteWindowManager : INoteWindowManager
    {
        private readonly NoteRepository repository;
        private readonly PlatformServiceBundle platformServices;
        private readonly ReminderScheduler reminders;
        private readonly KeybindingRegistry keybindings;
        private readonly Infrastructure.Settings.SettingsStore settings;
        private readonly Dictionary<string, FloatingNoteWindow> noteWindows = new(StringComparer.Ordinal);
        private Action? openMainWindowAction;

        /// <summary>
        /// Initializes a new instance of the <see cref="NoteWindowManager"/> class.
        /// </summary>
        /// <param name="repository">The note repository.</param>
        /// <param name="platformServices">The platform service bundle.</param>
        /// <param name="reminders">The reminder scheduler.</param>
        /// <param name="keybindings">The shared keybinding registry for editor hotkeys.</param>
        /// <param name="settings">The shared settings store read by note windows.</param>
        public NoteWindowManager(
            NoteRepository repository,
            PlatformServiceBundle platformServices,
            ReminderScheduler reminders,
            KeybindingRegistry keybindings,
            Infrastructure.Settings.SettingsStore settings)
        {
            this.repository = repository;
            this.platformServices = platformServices;
            this.reminders = reminders;
            this.keybindings = keybindings;
            this.settings = settings;
        }

        /// <summary>
        /// Occurs after a note window is opened or closed.
        /// </summary>
        public event EventHandler? NotesChanged;

        /// <summary>
        /// Gets whether a note currently has an open window.
        /// </summary>
        /// <param name="noteId">The note identifier.</param>
        /// <returns><see langword="true"/> when the note window is open.</returns>
        public bool IsOpen(string noteId)
        {
            return noteWindows.ContainsKey(noteId);
        }

        /// <summary>
        /// Restores the windows for all notes marked open in the repository and notifies any
        /// reminders that are already due.
        /// </summary>
        public void RestoreOpenNotes()
        {
            bool restore = bool.TryParse(
                settings.GetValue(SettingsUi.SettingsSchemaBuilder.RestoreOpenNotesKey, shouldSync: false, "true"),
                out bool parsed) ? parsed : true;

            if (restore)
            {
                foreach (AvaloniaNoteDocument note in repository.LoadOpenNotes())
                {
                    Open(note);
                }
            }

            IReadOnlyList<AvaloniaNoteDocument> allNotes = repository.LoadAll();
            _ = reminders.NotifyDueRemindersAsync(allNotes, DateTimeOffset.UtcNow);

            // Arm crontab rules for every note (not just open ones) so recurring reminders fire and
            // missed occurrences are caught up regardless of window state.
            foreach (AvaloniaNoteDocument note in allNotes)
            {
                reminders.RescheduleCron(note);
            }
        }

        /// <summary>
        /// Opens or activates the window for a note.
        /// </summary>
        /// <param name="note">The note to open.</param>
        public void Open(AvaloniaNoteDocument note)
        {
            if (noteWindows.TryGetValue(note.Id, out FloatingNoteWindow? existingWindow))
            {
                existingWindow.Show();
                existingWindow.Activate();
                return;
            }

            note.IsOpen = true;
            repository.Save(note);

            NoteWindowViewModel viewModel = new NoteWindowViewModel(note, repository, reminders);
            FloatingNoteWindow window = new FloatingNoteWindow(viewModel, platformServices.WindowLevels, platformServices.QuickLayout, keybindings, settings);
            window.SetOpenMainWindowAction(openMainWindowAction);
            noteWindows[note.Id] = window;
            window.Closed += (_, _) =>
            {
                noteWindows.Remove(note.Id);
                NotesChanged?.Invoke(this, EventArgs.Empty);
            };
            window.Show();
            reminders.Reschedule(note);
            reminders.RescheduleCron(note);
            NotesChanged?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>
        /// Closes the window for a note when it is open.
        /// </summary>
        /// <param name="noteId">The note identifier.</param>
        public void Close(string noteId)
        {
            if (noteWindows.TryGetValue(noteId, out FloatingNoteWindow? window))
            {
                window.Close();
            }
        }

        /// <summary>
        /// Applies a stacking level to a note's open window, if any.
        /// </summary>
        /// <param name="noteId">The note identifier.</param>
        /// <param name="level">The level to apply.</param>
        public void ApplyLevel(string noteId, Core.WindowLevel level)
        {
            if (noteWindows.TryGetValue(noteId, out FloatingNoteWindow? window))
            {
                platformServices.WindowLevels.Apply(window, level);
            }
        }

        /// <summary>
        /// Opens the quick-layout overlay for a note's window, opening the window first if needed.
        /// </summary>
        /// <param name="note">The note whose window to lay out.</param>
        public void ShowQuickLayout(AvaloniaNoteDocument note)
        {
            Open(note);
            if (noteWindows.TryGetValue(note.Id, out FloatingNoteWindow? window))
            {
                _ = window.ShowQuickLayoutOverlay();
            }
        }

        /// <summary>
        /// Opens or activates a note's window for a fired reminder and, when a source offset is given,
        /// scrolls the preview to that location in the note.
        /// </summary>
        /// <param name="note">The note that owns the reminder.</param>
        /// <param name="sourceOffset">
        /// The reminder rule's character offset into the note content to scroll to, or
        /// <see langword="null"/> for a note-level reminder with no in-note anchor.
        /// </param>
        public void ActivateForReminder(AvaloniaNoteDocument note, int? sourceOffset)
        {
            Open(note);
            if (sourceOffset is { } offset && noteWindows.TryGetValue(note.Id, out FloatingNoteWindow? window))
            {
                window.ScrollToSourceOffset(offset);
            }
        }

        /// <summary>
        /// Re-applies the global taskbar-visibility setting to every open note window.
        /// </summary>
        public void RefreshTaskbarVisibilityForAll()
        {
            foreach (FloatingNoteWindow window in noteWindows.Values)
            {
                window.RefreshTaskbarVisibility();
            }
        }

        /// <summary>
        /// Applies externally-changed content to a note's open window, if any, so its editor and
        /// preview refresh live. Returns <see langword="false"/> when no window is open (the caller
        /// should then persist through the repository directly).
        /// </summary>
        /// <param name="noteId">The note identifier.</param>
        /// <param name="content">The new note content.</param>
        /// <returns><see langword="true"/> when an open window handled the update.</returns>
        public bool TryApplyExternalContent(string noteId, string content)
        {
            if (noteWindows.TryGetValue(noteId, out FloatingNoteWindow? window))
            {
                window.ApplyExternalContent(content);
                return true;
            }

            return false;
        }

        /// <inheritdoc/>
        public void SetOpenMainWindowAction(Action? action)
        {
            openMainWindowAction = action;
            foreach (FloatingNoteWindow window in noteWindows.Values)
            {
                window.SetOpenMainWindowAction(action);
            }
        }
    }
}
