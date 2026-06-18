namespace YASN.Reminders
{
    /// <summary>
    /// Persists the auto-disable of a fire-once reminder rule after it fires, rewriting the note's
    /// Markdown so the spent rule never fires again, and refreshing any open window for that note.
    /// </summary>
    public interface IReminderContentWriter
    {
        /// <summary>
        /// Disables the fire-once rule in the note's content and persists the change.
        /// </summary>
        /// <param name="noteId">The note identifier.</param>
        /// <param name="ruleId">The stable rule identifier of the once-rule that fired.</param>
        void DisableOnceRule(string noteId, string ruleId);
    }
}
