namespace YASN.Reminders
{
    /// <summary>
    /// Performs programmatic edits to a note's Markdown source, routing each edit to the note's open
    /// window (so its editor and preview refresh live) or to the repository when the note is closed.
    /// One surface for every non-editor content mutation: reminder countdown, task-checkbox toggles,
    /// and future operations.
    /// </summary>
    public interface INoteContentWriter
    {
        /// <summary>
        /// Reduces the remaining fire count of a finite reminder rule (decrement, or mark spent at one)
        /// and persists the change.
        /// </summary>
        /// <param name="noteId">The note identifier.</param>
        /// <param name="ruleId">The stable rule identifier of the rule that fired.</param>
        void ReduceReminderCounter(string noteId, string ruleId);

        /// <summary>
        /// Toggles the Markdown task-list checkbox on the given 0-based source line and persists the
        /// change. No-op when the line is not a task item or already in the requested state.
        /// </summary>
        /// <param name="noteId">The note identifier.</param>
        /// <param name="sourceLine">The 0-based source line of the task item, matching <c>data-source-line</c>.</param>
        /// <param name="isChecked">Whether the item should become checked.</param>
        void SetTaskChecked(string noteId, int sourceLine, bool isChecked);
    }
}
