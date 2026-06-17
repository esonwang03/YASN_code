namespace YASN.Notifications
{
    /// <summary>
    /// Sends user-visible notifications through the current platform.
    /// </summary>
    public interface INotificationService
    {
        /// <summary>
        /// Gets whether the current platform has a configured notification backend.
        /// </summary>
        bool IsSupported { get; }

        /// <summary>
        /// Sends a notification request.
        /// </summary>
        /// <param name="request">The notification to send.</param>
        /// <returns>The delivery result.</returns>
        Task<NotificationSendResult> SendAsync(NotificationRequest request);
    }
}
