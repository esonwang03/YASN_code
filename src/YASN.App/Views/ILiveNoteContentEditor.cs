namespace YASN.Views
{
    /// <summary>
    /// The single content read/write surface for a note that has an open window. Every live content
    /// mutation — interactive editor commands, checklist toggles, reminder edits, file inserts, and
    /// externally-pushed updates — flows through this interface onto the window's AvaloniaEdit document,
    /// so they share one undo stack and one caret/selection model. Closed notes have no implementation;
    /// callers persist those through the repository instead.
    /// </summary>
    public interface ILiveNoteContentEditor
    {
        /// <summary>
        /// Gets the note's current live content (the editor document text, including unsaved edits).
        /// </summary>
        string LiveContent { get; }

        /// <summary>
        /// Applies a whole-string transform to the live content as a single minimal document edit, so
        /// the change is undoable and leaves the caret/selection outside the changed span intact.
        /// </summary>
        /// <param name="transform">
        /// Receives the current content and returns the new content, or <see langword="null"/> to make
        /// no change.
        /// </param>
        /// <returns><see langword="true"/> when the transform produced a change that was applied.</returns>
        bool ApplyTransform(Func<string, string?> transform);

        /// <summary>
        /// Replaces the entire live content as a single minimal document edit. Used for externally
        /// authored content (e.g. a fire-once reminder disabling itself). A no-op when unchanged.
        /// </summary>
        /// <param name="content">The new content.</param>
        void ReplaceAll(string content);
    }
}
