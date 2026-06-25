namespace YASN.Notifications
{
    /// <summary>
    /// Sends notifications through a native operating-system notification channel.
    /// </summary>
    public interface INativeNotificationSender
    {
        /// <summary>
        /// Gets whether the native sender can deliver notifications on this platform.
        /// </summary>
        bool IsSupported { get; }

        /// <summary>
        /// Requests notification permission from the user, including sound, on platforms that gate
        /// notifications behind a first-run authorization prompt (macOS). Must be called from the
        /// application's main thread. A no-op where no prompt is required.
        /// </summary>
        void RequestPermission();

        /// <summary>
        /// Sends a notification request through the operating system.
        /// </summary>
        /// <param name="request">The notification to send.</param>
        /// <returns>The delivery result.</returns>
        Task<NotificationSendResult> SendAsync(NotificationRequest request);
    }
}
