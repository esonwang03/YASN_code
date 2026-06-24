using Avalonia.Threading;

namespace YASN.Diagnostics
{
    /// <summary>
    /// Captures otherwise-unhandled exceptions and writes them to <see cref="AppLogger"/> so the app
    /// never terminates without a recorded cause. Covers three sources: the Avalonia UI thread, faulted
    /// fire-and-forget <see cref="Task"/>s, and the process-wide last-resort <see cref="AppDomain"/>
    /// hook. UI-thread faults are logged, surfaced to the user, and then swallowed so a single bad
    /// operation does not kill the tray; the AppDomain hook only logs, since the runtime still tears
    /// the process down when the fault is terminating.
    /// </summary>
    public static class GlobalExceptionHandler
    {
        private static readonly Lock Gate = new();
        private static bool _processWideRegistered;
        private static bool _uiThreadRegistered;

        /// <summary>
        /// Registers the process-wide handlers that need neither the UI nor app services, so they are
        /// live as early as possible (including the CLI path). Idempotent.
        /// </summary>
        public static void RegisterProcessWide()
        {
            lock (Gate)
            {
                if (_processWideRegistered)
                {
                    return;
                }

                _processWideRegistered = true;
                AppDomain.CurrentDomain.UnhandledException += OnAppDomainUnhandled;
                TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
            }
        }

        /// <summary>
        /// Registers the Avalonia UI-thread handler. Faults are logged, reported through
        /// <paramref name="notify"/>, then marked handled so the app keeps running. Call once the
        /// dispatcher and the notification service exist. Idempotent.
        /// </summary>
        /// <param name="notify">Callback invoked with each handled UI-thread exception, used to surface
        /// it to the user (e.g. a tray notification). May be null. Its own failures are ignored.</param>
        public static void RegisterUiThread(Action<Exception>? notify)
        {
            lock (Gate)
            {
                if (_uiThreadRegistered)
                {
                    return;
                }

                _uiThreadRegistered = true;
                Dispatcher.UIThread.UnhandledException += (_, e) =>
                {
                    AppLogger.Error($"Unhandled UI-thread exception: {e.Exception}");
                    SafeNotify(notify, e.Exception);

                    // Keep the app alive: a swallowed UI fault is preferable to the tray vanishing.
                    e.Handled = true;
                };
            }
        }

        private static void OnAppDomainUnhandled(object sender, UnhandledExceptionEventArgs e)
        {
            // Last resort: the runtime still terminates when IsTerminating is true, but the cause is
            // now on disk. The synchronous AppLogger write flushes before this handler returns.
            string detail = e.ExceptionObject is Exception ex ? ex.ToString() : e.ExceptionObject.ToString() ?? "unknown";
            AppLogger.Error($"Unhandled exception (terminating={e.IsTerminating}): {detail}");
        }

        private static void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
        {
            AppLogger.Error($"Unobserved task exception: {e.Exception}");

            // Observe it so a faulted fire-and-forget Task does not escalate to process termination.
            e.SetObserved();
        }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031:Do not catch general exception types",
            Justification = "Outermost safety net for an opaque notify callback: it must never re-throw " +
            "into the unhandled-exception handler. The original exception is already logged.")]
        private static void SafeNotify(Action<Exception>? notify, Exception ex)
        {
            if (notify is null)
            {
                return;
            }

            try
            {
                notify(ex);
            }
            catch (Exception notifyError)
            {
                // The notification path itself failed; log and move on rather than re-entering the
                // handler. The original exception is already recorded above.
                AppLogger.Warn($"Failed to surface unhandled exception to the user: {notifyError.Message}");
            }
        }
    }
}
