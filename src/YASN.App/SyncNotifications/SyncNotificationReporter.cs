using System.Globalization;
using YASN.Infrastructure.Sync;
using YASN.Localization;
using YASN.Notifications;

namespace YASN.SyncNotifications
{
    /// <summary>
    /// Emits discrete sync status notifications.
    /// </summary>
    public sealed class SyncNotificationReporter
    {
        private readonly INotificationService notifications;
        private readonly LocalizationService localization;

        /// <summary>
        /// Initializes a sync notification reporter.
        /// </summary>
        /// <param name="notifications">The notification service to use.</param>
        /// <param name="localization">The localization service for notification text, or the shared instance when omitted.</param>
        public SyncNotificationReporter(INotificationService notifications, LocalizationService? localization = null)
        {
            this.notifications = notifications;
            this.localization = localization ?? LocalizationService.Current;
        }

        /// <summary>
        /// Reports that a sync run has started.
        /// </summary>
        public Task<NotificationSendResult> ReportStartedAsync()
        {
            return notifications.SendAsync(new NotificationRequest(
                localization["Sync.Notify.Started.Title"],
                localization["Sync.Notify.Started.Body"],
                "sync:started"));
        }

        /// <summary>
        /// Reports that a sync run completed or failed.
        /// </summary>
        /// <param name="result">The sync result to summarize.</param>
        public Task<NotificationSendResult> ReportCompletedAsync(SyncResult result)
        {
            if (!result.Success)
            {
                // The message is a server/exception detail and is not localizable; the title is.
                return notifications.SendAsync(new NotificationRequest(
                    localization["Sync.Notify.Failed.Title"],
                    result.Message,
                    "sync:failed"));
            }

            // A skipped pass with the "disabled" reason means sync is turned off; point the user at Settings.
            if (string.Equals(result.Message, "disabled", StringComparison.Ordinal))
            {
                return notifications.SendAsync(new NotificationRequest(
                    localization["Sync.Notify.Disabled.Title"],
                    localization["Sync.Notify.Disabled.Body"],
                    "sync:disabled"));
            }

            string body = string.Format(
                CultureInfo.CurrentCulture,
                localization["Sync.Notify.Complete.Body"],
                result.FilesUploaded,
                result.FilesDownloaded,
                result.FilesDeleted);
            return notifications.SendAsync(new NotificationRequest(
                localization["Sync.Notify.Complete.Title"],
                body,
                "sync:complete"));
        }
    }
}
