using YASN.Notifications;

namespace YASN.Migration.Tests
{
    /// <summary>
    /// Verifies system notification dispatch without in-app overlay state.
    /// </summary>
    public sealed class SystemNotificationServiceTests
    {
        /// <summary>
        /// Sends notification requests through the native OS sender.
        /// </summary>
        [Fact]
        public async Task SendAsyncDelegatesToNativeSystemSender()
        {
            RecordingNativeNotificationSender sender = new RecordingNativeNotificationSender();
            SystemNotificationService service = new SystemNotificationService(sender);
            NotificationRequest request = new NotificationRequest("Sync complete", "2 uploaded / 1 downloaded", "sync:complete");

            NotificationSendResult result = await service.SendAsync(request);

            Assert.Equal(NotificationSendResult.Sent, result);
            Assert.True(service.IsSupported);
            Assert.Same(request, Assert.Single(sender.Requests));
        }

        /// <summary>
        /// Does not report support when the current OS has no native sender.
        /// </summary>
        [Fact]
        public async Task SendAsyncReturnsUnsupportedWhenNativeSenderIsUnavailable()
        {
            SystemNotificationService service = new SystemNotificationService(new UnsupportedNativeNotificationSender());

            NotificationSendResult result = await service.SendAsync(new NotificationRequest("Title", "Body", "arg"));

            Assert.Equal(NotificationSendResult.Unsupported, result);
            Assert.False(service.IsSupported);
        }

        private sealed class RecordingNativeNotificationSender : INativeNotificationSender
        {
            internal List<NotificationRequest> Requests { get; } = new();

            public bool IsSupported => true;

            public void RequestPermission()
            {
            }

            public Task<NotificationSendResult> SendAsync(NotificationRequest request)
            {
                Requests.Add(request);
                return Task.FromResult(NotificationSendResult.Sent);
            }
        }
    }
}
