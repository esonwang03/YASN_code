namespace YASN.Notifications
{
    /// <summary>
    /// Reports absence of a native operating-system notification channel.
    /// </summary>
    public sealed class UnsupportedNativeNotificationSender : INativeNotificationSender
    {
        /// <summary>
        /// Gets a value indicating that native notifications are unavailable.
        /// </summary>
        public bool IsSupported => false;

        /// <summary>
        /// Returns an unsupported result without showing an in-app notification.
        /// </summary>
        /// <param name="request">The notification that could not be sent.</param>
        /// <returns>The unsupported result.</returns>
        public Task<NotificationSendResult> SendAsync(NotificationRequest request)
        {
            return Task.FromResult(NotificationSendResult.Unsupported);
        }
    }
}
