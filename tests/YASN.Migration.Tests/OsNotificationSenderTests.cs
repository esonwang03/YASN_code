using YASN.Notifications;

namespace YASN.Migration.Tests
{
    /// <summary>
    /// Verifies the cross-platform OsNotifications-backed sender reports support and delivers
    /// notifications on the host platform.
    /// </summary>
    public sealed class OsNotificationSenderTests
    {
        /// <summary>
        /// Reports support on Windows, macOS, and Linux desktop platforms.
        /// </summary>
        [Fact]
        public void IsSupportedMatchesDesktopPlatform()
        {
            OsNotificationSender sender = new OsNotificationSender();

            bool expected = OperatingSystem.IsWindows() || OperatingSystem.IsMacOS() || OperatingSystem.IsLinux();
            Assert.Equal(expected, sender.IsSupported);
        }

        /// <summary>
        /// Returns a terminal result without throwing when dispatching a notification.
        /// </summary>
        [Fact]
        public async Task SendAsyncReturnsTerminalResult()
        {
            OsNotificationSender sender = new OsNotificationSender();
            NotificationRequest request = new NotificationRequest("YASN", "Reminder body", "note:1");

            NotificationSendResult result = await sender.SendAsync(request);

            Assert.True(result == NotificationSendResult.Sent || result == NotificationSendResult.Unsupported);
        }
    }
}
