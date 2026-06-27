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

        /// <summary>
        /// A skipped pass whose reason is "disabled" (sync turned off) toasts a distinct message that
        /// points the user at Settings, rather than a misleading "Sync complete".
        /// </summary>
        [Fact]
        public async Task ReportsDisabledNotificationWhenSyncIsOff()
        {
            RecordingNotificationService notifications = new RecordingNotificationService();
            SyncNotificationReporter reporter = new SyncNotificationReporter(notifications);

            await reporter.ReportCompletedAsync(SyncResult.Skipped("disabled"));

            NotificationRequest request = Assert.Single(notifications.Requests);
            Assert.Equal("Sync is disabled", request.Title);
            Assert.Equal("sync:disabled", request.ActivationArgument);
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
