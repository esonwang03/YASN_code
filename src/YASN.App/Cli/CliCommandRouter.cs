using YASN.Application;
using YASN.AvaloniaNotes;
using YASN.Infrastructure.Sync;
using YASN.WindowLayout;

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

        /// <summary>
        /// Replaces or appends a note's Markdown. When the note's window is open the edit composes with
        /// the live editor document (undoable, caret-preserving); when closed it is persisted through
        /// the repository. The window state — never the caller — decides the path, so a direct disk
        /// write can never clobber an open window's eager autosave.
        /// </summary>
        /// <param name="noteId">The note identifier.</param>
        /// <param name="append">Whether to append (<see langword="true"/>) or replace.</param>
        /// <param name="content">The Markdown to apply.</param>
        /// <returns>A status line for the CLI response.</returns>
        public string EditNote(string noteId, bool append, string content)
        {
            AvaloniaNoteDocument? note = repository.LoadAll().FirstOrDefault(n => n.Id == noteId);
            if (note is null)
            {
                throw new InvalidOperationException($"No note with id '{noteId}'.");
            }

            string verb = append ? "Appended to" : "Replaced";
            if (noteWindows.TryEditContent(noteId, current => append ? CliText.AppendContent(current, content) : content))
            {
                return $"{verb} note '{noteId}' (live).";
            }

            note.Content = append ? CliText.AppendContent(note.Content, content) : content;
            repository.Save(note);
            return $"{verb} note '{noteId}'.";
        }

        /// <summary>
        /// Moves a note's window onto a screen at an explicit physical-pixel rectangle, or raises the
        /// quick-layout overlay when <paramref name="coords"/> is null. Opens the note first if needed.
        /// </summary>
        /// <param name="noteId">The note identifier.</param>
        /// <param name="screenIndex">The target screen's index in <see cref="NoteWindowManager.EnumerateScreens"/>.</param>
        /// <param name="coords">The screen-relative rectangle, or null to raise the overlay.</param>
        /// <returns>A status line for the CLI response.</returns>
        public string LayoutNote(string noteId, int screenIndex, CliLayoutCoords? coords)
        {
            AvaloniaNoteDocument? note = repository.LoadAll().FirstOrDefault(n => n.Id == noteId);
            if (note is null)
            {
                throw new InvalidOperationException($"No note with id '{noteId}'.");
            }

            if (coords is null)
            {
                noteWindows.ShowQuickLayout(note);
                return $"Opened layout overlay for note '{noteId}'.";
            }

            IReadOnlyList<ScreenInfo> screens = noteWindows.EnumerateScreens();
            if (screenIndex < 0 || screenIndex >= screens.Count)
            {
                throw new InvalidOperationException(
                    $"No screen with index {screenIndex} (found {screens.Count}).");
            }

            WindowRect bounds = CliLayoutMath.Resolve(
                screens[screenIndex],
                coords.LeftTopX,
                coords.LeftTopY,
                coords.RightBottomX,
                coords.RightBottomY);

            noteWindows.Open(note);
            if (!noteWindows.ApplyLayoutBounds(noteId, bounds))
            {
                throw new InvalidOperationException($"Could not lay out note '{noteId}'.");
            }

            return $"Moved note '{noteId}' to screen {screenIndex}.";
        }

        /// <summary>
        /// Enumerates the desktop's screens as a formatted, multi-line table for the CLI.
        /// </summary>
        /// <returns>The screen table.</returns>
        public string ListScreens()
        {
            IReadOnlyList<ScreenInfo> screens = noteWindows.EnumerateScreens();
            System.Globalization.CultureInfo culture = System.Globalization.CultureInfo.InvariantCulture;
            System.Text.StringBuilder builder = new();
            builder.Append("Index  Bounds (x,y,w,h)              Scaling  Primary");
            foreach (ScreenInfo screen in screens)
            {
                WindowRect b = screen.PhysicalBounds;
                builder.Append('\n');
                builder.Append(culture,
                    $"{screen.Index,5}  {b.Left},{b.Top},{b.Width},{b.Height,-18}  {screen.Scaling,7:0.##}  {(screen.IsPrimary ? "yes" : "no")}");
            }

            return builder.ToString();
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
