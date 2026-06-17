namespace YASN.Notifications
{
    /// <summary>
    /// Sends desktop notifications through the cross-platform <c>OsNotifications</c> library, which
    /// drives Windows toasts, macOS Notification Center, and the Linux DBus notification service.
    /// </summary>
    public sealed class OsNotificationSender : INativeNotificationSender
    {
        // OsNotifications uses this bundle identifier for the macOS notification source.
        private const string MacBundleIdentifier = "com.yasn.stickynotes";

        /// <summary>
        /// Initializes the sender and applies platform-specific configuration.
        /// </summary>
        public OsNotificationSender()
        {
            if (OperatingSystem.IsMacOS())
            {
                OsNotifications.Notifications.BundleIdentifier = MacBundleIdentifier;
            }

            if (OperatingSystem.IsLinux())
            {
                // Marks the process as a GUI application so the DBus notifier targets the desktop session.
                OsNotifications.Notifications.SetGuiApplication(true);
            }
        }

        /// <summary>
        /// Gets whether the current platform has a supported native notification channel.
        /// </summary>
        public bool IsSupported => OperatingSystem.IsWindows() || OperatingSystem.IsMacOS() || OperatingSystem.IsLinux();

        /// <summary>
        /// Sends a notification through the native operating-system channel.
        /// </summary>
        /// <param name="request">The notification to send.</param>
        /// <returns>The delivery result.</returns>
        public Task<NotificationSendResult> SendAsync(NotificationRequest request)
        {
            if (!IsSupported)
            {
                return Task.FromResult(NotificationSendResult.Unsupported);
            }

            try
            {
                // ShowNotification is synchronous; the body maps to the macOS "message" line.
                OsNotifications.Notifications.ShowNotification(request.Title, request.Body, string.Empty);
                return Task.FromResult(NotificationSendResult.Sent);
            }
            catch (Exception ex) when (ex is InvalidOperationException or PlatformNotSupportedException or System.ComponentModel.Win32Exception)
            {
                return Task.FromResult(NotificationSendResult.Unsupported);
            }
        }
    }
}
