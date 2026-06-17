using YASN.AvaloniaNotes;
using YASN.Core;

namespace YASN.Application
{
    /// <summary>
    /// Manages the live set of open note windows so the tray and the note manager window share one
    /// window set. Abstracted so view models can be tested without real windows.
    /// </summary>
    public interface INoteWindowManager
    {
        /// <summary>
        /// Occurs after a note window is opened or closed.
        /// </summary>
        event EventHandler? NotesChanged;

        /// <summary>
        /// Gets whether a note currently has an open window.
        /// </summary>
        /// <param name="noteId">The note identifier.</param>
        /// <returns><see langword="true"/> when the note window is open.</returns>
        bool IsOpen(int noteId);

        /// <summary>
        /// Opens or activates the window for a note.
        /// </summary>
        /// <param name="note">The note to open.</param>
        void Open(AvaloniaNoteDocument note);

        /// <summary>
        /// Closes the window for a note when it is open.
        /// </summary>
        /// <param name="noteId">The note identifier.</param>
        void Close(int noteId);

        /// <summary>
        /// Applies a stacking level to a note's open window, if any.
        /// </summary>
        /// <param name="noteId">The note identifier.</param>
        /// <param name="level">The level to apply.</param>
        void ApplyLevel(int noteId, WindowLevel level);

        /// <summary>
        /// Opens the quick-layout overlay for a note's window, opening the window first if needed.
        /// </summary>
        /// <param name="note">The note whose window to lay out.</param>
        void ShowQuickLayout(AvaloniaNoteDocument note);

        /// <summary>
        /// Re-applies the global taskbar-visibility setting to every open note window.
        /// </summary>
        void RefreshTaskbarVisibilityForAll();

        /// <summary>
        /// Stores a callback that opens or activates the main note-manager window so that every
        /// note window can invoke it from its "Manage Notes" context menu without holding a
        /// direct reference to the manager window.
        /// </summary>
        /// <param name="action">
        /// The action that opens the note manager, or <see langword="null"/> when unavailable.
        /// </param>
        void SetOpenMainWindowAction(Action? action);

        /// <summary>
        /// Applies externally-changed content to a note's open window, if any, so its editor and
        /// preview refresh live. Returns <see langword="false"/> when no window is open.
        /// </summary>
        /// <param name="noteId">The note identifier.</param>
        /// <param name="content">The new note content.</param>
        /// <returns><see langword="true"/> when an open window handled the update.</returns>
        bool TryApplyExternalContent(int noteId, string content);
    }
}
