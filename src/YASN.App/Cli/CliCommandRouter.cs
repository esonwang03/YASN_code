using YASN.Application;
using YASN.AvaloniaNotes;
using YASN.Infrastructure.Sync;

namespace YASN.Cli
{
    /// <summary>
    /// Executes CLI verbs that must run inside the live tray instance (window raising, in-memory
    /// index mutation, sync). Both the IPC server and direct in-process callers route through here so
    /// there is one implementation of each action. Window-affecting members must be invoked on the
    /// Avalonia UI thread.
    /// </summary>
    public sealed class CliCommandRouter
    {
        private readonly NoteRepository repository;
        private readonly NoteWindowManager noteWindows;
        private readonly SyncComposition? sync;
        private readonly Action openMainWindow;
        private readonly Action openSettingsWindow;

        /// <summary>
        /// Initializes the router over the live application services.
        /// </summary>
        /// <param name="repository">The shared note repository.</param>
        /// <param name="noteWindows">The shared note window manager.</param>
        /// <param name="sync">The sync composition, or null when sync is unavailable.</param>
        /// <param name="openMainWindow">Action that raises the manage-notes window.</param>
        /// <param name="openSettingsWindow">Action that raises the settings window.</param>
        public CliCommandRouter(
            NoteRepository repository,
            NoteWindowManager noteWindows,
            SyncComposition? sync,
            Action openMainWindow,
            Action openSettingsWindow)
        {
            this.repository = repository;
            this.noteWindows = noteWindows;
            this.sync = sync;
            this.openMainWindow = openMainWindow;
            this.openSettingsWindow = openSettingsWindow;
        }

        /// <summary>
        /// Raises the window for the note with the given id. Returns a human-readable status.
        /// </summary>
        /// <param name="noteId">The note identifier.</param>
        /// <returns>A status line for the CLI response.</returns>
        public string OpenNote(string noteId)
        {
            AvaloniaNoteDocument? note = repository.LoadAll().FirstOrDefault(n => n.Id == noteId);
            if (note is null)
            {
                return $"No note with id '{noteId}'.";
            }

            noteWindows.Open(note);
            return $"Opened note '{noteId}'.";
        }

        /// <summary>
        /// Deletes the note with the given id, closing its window first so the live set stays
        /// consistent (mirrors the manage-notes window's delete order).
        /// </summary>
        /// <param name="noteId">The note identifier.</param>
        /// <returns>A status line for the CLI response.</returns>
        public string DeleteNote(string noteId)
        {
            bool exists = repository.LoadAll().Any(n => n.Id == noteId);
            if (!exists)
            {
                return $"No note with id '{noteId}'.";
            }

            noteWindows.Close(noteId);
            repository.Delete(noteId);
            return $"Deleted note '{noteId}'.";
        }

        /// <summary>Raises the manage-notes window.</summary>
        /// <returns>A status line for the CLI response.</returns>
        public string ShowMain()
        {
            openMainWindow();
            return "Opened manage-notes window.";
        }

        /// <summary>Raises the settings window.</summary>
        /// <returns>A status line for the CLI response.</returns>
        public string ShowSettings()
        {
            openSettingsWindow();
            return "Opened settings window.";
        }

        /// <summary>
        /// Triggers one sync pass and reports its outcome.
        /// </summary>
        /// <returns>A status line for the CLI response.</returns>
        public async Task<string> SyncNowAsync()
        {
            if (sync is null)
            {
                return "Sync is unavailable.";
            }

            SyncResult result = await sync.Engine.SyncNowAsync().ConfigureAwait(true);
            return result.Success ? $"Sync: {result.Message}." : $"Sync failed: {result.Message}.";
        }
    }
}
