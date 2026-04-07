namespace YASN.Core
{
    /// <summary>
    /// Describes the outcome of repairing the note index from local markdown files.
    /// </summary>
    public sealed class NoteIndexRepairResult
    {
        /// <summary>
        /// Gets whether the local index content was changed.
        /// </summary>
        public bool WasChanged { get; init; }

        /// <summary>
        /// Gets the number of local markdown files that were added back into the index.
        /// </summary>
        public int AddedNoteCount { get; init; }

        /// <summary>
        /// Gets a human-readable summary of the repair outcome.
        /// </summary>
        public string Message { get; init; } = string.Empty;
    }
}
