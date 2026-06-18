using YASN.AvaloniaNotes;
using YASN.Notifications;
using YASN.Reminders;

namespace YASN.Migration.Tests
{
    /// <summary>
    /// Verifies reminder catch-up behavior.
    /// </summary>
    public sealed class ReminderSchedulerTests
    {
        /// <summary>
        /// Sends notifications for notes whose reminders are due.
        /// </summary>
        [Fact]
        public async Task NotifyDueRemindersAsyncNotifiesOnlyDueNotes()
        {
            RecordingNotificationService notifications = new RecordingNotificationService();
            using ReminderScheduler scheduler = new ReminderScheduler(notifications);
            AvaloniaNoteDocument due = new AvaloniaNoteDocument { Id = "1", Content = "# Due", ReminderAt = DateTimeOffset.UtcNow.AddMinutes(-1) };
            AvaloniaNoteDocument future = new AvaloniaNoteDocument { Id = "2", Content = "# Future", ReminderAt = DateTimeOffset.UtcNow.AddMinutes(5) };

            await scheduler.NotifyDueRemindersAsync([due, future], DateTimeOffset.UtcNow);

            NotificationRequest request = Assert.Single(notifications.Requests);
            Assert.Equal("Due", request.Title);
            Assert.Equal("note:1", request.ActivationArgument);
        }

        /// <summary>
        /// Fires a near-immediate reminder through the live timer path.
        /// </summary>
        [Fact]
        public async Task RescheduleFiresDueReminderThroughTimer()
        {
            RecordingNotificationService notifications = new RecordingNotificationService();
            using ReminderScheduler scheduler = new ReminderScheduler(notifications);
            AvaloniaNoteDocument note = new AvaloniaNoteDocument { Id = "7", Content = "# Soon", ReminderAt = DateTimeOffset.UtcNow.AddMilliseconds(50) };

            scheduler.Reschedule(note);
            await WaitForAsync(() => notifications.Requests.Count > 0);

            NotificationRequest request = Assert.Single(notifications.Requests);
            Assert.Equal("note:7", request.ActivationArgument);
        }

        /// <summary>
        /// Cancels a pending reminder so the timer never fires.
        /// </summary>
        [Fact]
        public async Task CancelStopsPendingReminder()
        {
            RecordingNotificationService notifications = new RecordingNotificationService();
            using ReminderScheduler scheduler = new ReminderScheduler(notifications);
            AvaloniaNoteDocument note = new AvaloniaNoteDocument { Id = "9", Content = "# Later", ReminderAt = DateTimeOffset.UtcNow.AddMilliseconds(200) };

            scheduler.Reschedule(note);
            scheduler.Cancel(note.Id);
            await Task.Delay(350).ConfigureAwait(true);

            Assert.Empty(notifications.Requests);
        }

        private static async Task WaitForAsync(Func<bool> condition)
        {
            for (int attempt = 0; attempt < 50 && !condition(); attempt++)
            {
                await Task.Delay(20).ConfigureAwait(true);
            }
        }

        private sealed class RecordingNotificationService : INotificationService
        {
            internal List<NotificationRequest> Requests { get; } = new();

            public bool IsSupported => true;

            public Task<NotificationSendResult> SendAsync(NotificationRequest request)
            {
                Requests.Add(request);
                return Task.FromResult(NotificationSendResult.Sent);
            }
        }
    }
}
