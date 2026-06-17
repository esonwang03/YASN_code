namespace YASN.Views
{
    /// <summary>
    /// Outcome of the reminder dialog: the chosen reminder time, or null to clear it.
    /// A null dialog result (window closed) means the reminder was left unchanged.
    /// </summary>
    /// <param name="ReminderAt">The selected reminder time, or null when cleared.</param>
    public sealed record ReminderDialogResult(DateTimeOffset? ReminderAt);
}
