namespace YASN.Notifications
{
    /// <summary>
    /// Sends notifications through the current operating system.
    /// </summary>
    public sealed class SystemNotificationService : INotificationService
    {
        private readonly INativeNotificationSender sender;

        /// <summary>
        /// Initializes a system notification service.
        /// </summary>
        /// <param name="sender">The native operating-system sender.</param>
        public SystemNotificationService(INativeNotificationSender sender)
        {
            this.sender = sender;
        }

        /// <summary>
        /// Gets whether the current operating system sender is supported.
        /// </summary>
        public bool IsSupported => sender.IsSupported;

        /// <summary>
        /// Sends a notification through the native operating-system channel.
        /// </summary>
        /// <param name="request">The notification to send.</param>
        /// <returns>The delivery result.</returns>
        public Task<NotificationSendResult> SendAsync(NotificationRequest request)
        {
            return sender.SendAsync(request);
        }
    }
}
