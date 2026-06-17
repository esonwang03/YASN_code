using YASN.Infrastructure.Sync;
using YASN.Notifications;
using YASN.SyncNotifications;

namespace YASN.Migration.Tests
{
    /// <summary>
    /// Verifies discrete sync notifications.
    /// </summary>
    public sealed class SyncNotificationReporterTests
    {
        /// <summary>
        /// Sends started and completed sync notifications through the notification abstraction.
        /// </summary>
        [Fact]
        public async Task ReportsStartedAndCompletedNotifications()
        {
            RecordingNotificationService notifications = new RecordingNotificationService();
            SyncNotificationReporter reporter = new SyncNotificationReporter(notifications);

            await reporter.ReportStartedAsync();
            await reporter.ReportCompletedAsync(new SyncResult { Success = true, FilesUploaded = 2, FilesDownloaded = 3 });

            Assert.Equal(["Sync started", "Sync complete"], notifications.Requests.Select(request => request.Title));
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
