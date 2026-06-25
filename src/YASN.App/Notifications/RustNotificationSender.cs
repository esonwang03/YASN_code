using YASN.Native.Notify;
using YASN.PlatformServices;

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
        public const string AppId = "io.github.esonwang03.yasn";

        // Action Center display name for the Windows AUMID identity registered below.
        public const string AppDisplayName = "YASN";

        private readonly bool initialized;

        /// <summary>
        /// Initializes the sender and creates the native notification manager once.
        /// </summary>
        public RustNotificationSender()
        {
            try
            {
                // Windows silently drops toasts from an unpackaged app that has no registered AUMID.
                // Establish that identity before the native manager obtains its toast notifier.
                if (OperatingSystem.IsWindows())
                {
                    WindowsToastRegistration.Ensure(AppId, AppDisplayName);
                }

                initialized = YasnNotifyMethods.Init(AppId);

                // macOS gates notifications behind a first-run authorization prompt (alert + sound +
                // badge). Request it here: startup runs on the UI/main thread, which the underlying
                // authorization API requires. The OS only prompts once and returns the prior decision
                // thereafter; on Windows/Linux this is a no-op.
                if (initialized && OperatingSystem.IsMacOS())
                {
                    RequestPermission();
                }
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
        /// Requests notification permission (alert, sound, and badge) from the user. On macOS this
        /// triggers the first-run system authorization prompt; the OS only asks once and returns the
        /// prior decision thereafter. A no-op on Windows and Linux. Must be called from the main
        /// thread, as the underlying macOS authorization API is main-thread-only.
        /// </summary>
        public void RequestPermission()
        {
            if (!IsSupported)
            {
                return;
            }

            try
            {
                bool granted = YasnNotifyMethods.RequestNotificationPermission();
                if (!granted)
                {
                    AppLogger.Info("Notification permission was not granted.");
                }
            }
            catch (Exception ex) when (ex is DllNotFoundException or BadImageFormatException)
            {
                // Native library absent; nothing to request against. Matches the ctor's degradation.
            }
        }

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
