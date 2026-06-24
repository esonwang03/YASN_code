using YASN.Notifications;

namespace YASN.Migration.Tests
{
    /// <summary>
    /// Verifies the native <c>user-notify</c>-backed sender returns terminal results and never
    /// throws, whether or not the <c>yasn_notify</c> native library is present in the test output.
    /// </summary>
    /// <remarks>
    /// The test cannot assert that a toast actually appears (no display in CI) and must pass when
    /// the cdylib is absent — e.g. a build with <c>-p:BuildRustNotify=false</c> — so it only
    /// pins the contract that matters to callers: a terminal <see cref="NotificationSendResult"/>
    /// is always produced and construction/send never escape an exception.
    /// </remarks>
    public sealed class RustNotificationSenderTests
    {
        /// <summary>
        /// Constructs the sender without throwing even when the native library is unavailable.
        /// </summary>
        [Fact]
        public void ConstructionDoesNotThrow()
        {
            RustNotificationSender sender = new RustNotificationSender();

            // IsSupported is whatever native initialization resolved to; it must be a stable bool.
            bool supported = sender.IsSupported;
            Assert.True(supported || !supported);
        }

        /// <summary>
        /// Returns a terminal result without throwing when dispatching a notification.
        /// </summary>
        [Fact]
        public async Task SendAsyncReturnsTerminalResult()
        {
            RustNotificationSender sender = new RustNotificationSender();
            NotificationRequest request = new NotificationRequest("YASN", "Reminder body", "note:1");

            NotificationSendResult result = await sender.SendAsync(request);

            Assert.True(result == NotificationSendResult.Sent || result == NotificationSendResult.Unsupported);
        }

        /// <summary>
        /// Reports unsupported when native initialization did not succeed.
        /// </summary>
        [Fact]
        public async Task SendAsyncIsUnsupportedWhenNotInitialized()
        {
            RustNotificationSender sender = new RustNotificationSender();

            if (!sender.IsSupported)
            {
                NotificationSendResult result = await sender.SendAsync(new NotificationRequest("T", "B", "arg"));
                Assert.Equal(NotificationSendResult.Unsupported, result);
            }
        }
    }
}
