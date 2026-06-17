namespace YASN.Notifications
{
    /// <summary>
    /// Reports notification absence without pretending delivery occurred.
    /// </summary>
    public sealed class UnsupportedNotificationService : INotificationService
    {
        /// <summary>
        /// Gets a value indicating that desktop notifications are unavailable.
        /// </summary>
        public bool IsSupported => false;

        /// <summary>
        /// Returns an unsupported result without side effects.
        /// </summary>
        /// <param name="request">The notification that could not be sent.</param>
        /// <returns>The unsupported result.</returns>
        public Task<NotificationSendResult> SendAsync(NotificationRequest request)
        {
            return Task.FromResult(NotificationSendResult.Unsupported);
        }
    }
}
