using YASN.Native.Notify;

namespace YASN.Notifications
{
    /// <summary>
    /// Sends desktop notifications through the native <c>yasn_notify</c> library, a Rust
    /// <c>cdylib</c> fronting the cross-platform <c>user-notify</c> crate (Windows toasts, macOS
    /// Notification Center, Linux DBus) over a UniFFI-generated P/Invoke surface.
    /// </summary>
    /// <remarks>
    /// Replaces the managed <c>OsNotifications</c> package, whose reflection-based DBus marshalling
    /// could not be annotated for NativeAOT. The native call is synchronous; it is wrapped in a
    /// completed <see cref="Task"/> to satisfy the asynchronous sender contract, matching the prior
    /// implementation. The native library is absent when the app is built without the Rust step
    /// (<c>-p:BuildRustNotify=false</c>); the resulting load failure is caught and reported as
    /// unsupported rather than thrown.
    /// </remarks>
    public sealed class RustNotificationSender : INativeNotificationSender
    {
        // Application identifier: the AppUserModelID on Windows; ignored on macOS (the system uses
        // the running .app bundle's identifier) and on Linux. Matches the macOS bundle id.
        private const string AppId = "io.github.esonwang03.yasn";

        private readonly bool initialized;

        /// <summary>
        /// Initializes the sender and creates the native notification manager once.
        /// </summary>
        public RustNotificationSender()
        {
            try
            {
                initialized = YasnNotifyMethods.Init(AppId);
            }
            catch (Exception ex) when (ex is DllNotFoundException or BadImageFormatException)
            {
                // The native library was not shipped (or is the wrong architecture); degrade to
                // unsupported instead of crashing startup.
                AppLogger.Warn($"Native notification library unavailable: {ex.Message}");
                initialized = false;
            }
        }

        /// <summary>
        /// Gets whether the native notification channel initialized on a supported platform.
        /// </summary>
        public bool IsSupported => initialized
            && (OperatingSystem.IsWindows() || OperatingSystem.IsMacOS() || OperatingSystem.IsLinux());

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
                // The activation argument is carried as the notification thread id (grouping key);
                // click/activation handling is not wired up, matching prior behavior.
                bool sent = YasnNotifyMethods.ShowNotification(
                    request.Title, request.Body, request.ActivationArgument ?? string.Empty);
                return Task.FromResult(sent ? NotificationSendResult.Sent : NotificationSendResult.Unsupported);
            }
            catch (Exception ex) when (ex is DllNotFoundException or BadImageFormatException)
            {
                return Task.FromResult(NotificationSendResult.Unsupported);
            }
        }
    }
}
