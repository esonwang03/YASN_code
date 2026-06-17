using Avalonia.Threading;
using YASN.AvaloniaNotes;
using YASN.Infrastructure.Reminders;
using YASN.Views;

namespace YASN.Reminders
{
    /// <summary>
    /// Shows the in-app <see cref="ReminderPopupWindow"/> when a reminder fires, marshaling creation
    /// to the UI thread (the scheduler fires on a timer thread).
    /// </summary>
    public sealed class AvaloniaReminderPresenter : IReminderPresenter
    {
        /// <inheritdoc/>
        public void Present(AvaloniaNoteDocument note, NoteReminderRule rule)
        {
            Dispatcher.UIThread.Post(() =>
            {
                ReminderPopupWindow popup = new ReminderPopupWindow(note, rule);
                popup.Show();
                popup.Activate();
            });
        }
    }
}
