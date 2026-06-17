using YASN.Infrastructure.Sync;
using YASN.Notifications;

namespace YASN.SyncNotifications
{
    /// <summary>
    /// Emits discrete sync status notifications.
    /// </summary>
    public sealed class SyncNotificationReporter
    {
        private readonly INotificationService notifications;

        /// <summary>
        /// Initializes a sync notification reporter.
        /// </summary>
        /// <param name="notifications">The notification service to use.</param>
        public SyncNotificationReporter(INotificationService notifications)
        {
            this.notifications = notifications;
        }

        /// <summary>
        /// Reports that a sync run has started.
        /// </summary>
        public Task<NotificationSendResult> ReportStartedAsync()
        {
            return notifications.SendAsync(new NotificationRequest("Sync started", "YASN is syncing notes.", "sync:started"));
        }

        /// <summary>
        /// Reports that a sync run completed or failed.
        /// </summary>
        /// <param name="result">The sync result to summarize.</param>
        public Task<NotificationSendResult> ReportCompletedAsync(SyncResult result)
        {
            if (!result.Success)
            {
                return notifications.SendAsync(new NotificationRequest("Sync failed", result.Message, "sync:failed"));
            }

            string body = $"{result.FilesUploaded} uploaded / {result.FilesDownloaded} downloaded";
            return notifications.SendAsync(new NotificationRequest("Sync complete", body, "sync:complete"));
        }
    }
}
