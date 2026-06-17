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
        /// Sends a notification request through the operating system.
        /// </summary>
        /// <param name="request">The notification to send.</param>
        /// <returns>The delivery result.</returns>
        Task<NotificationSendResult> SendAsync(NotificationRequest request);
    }
}
