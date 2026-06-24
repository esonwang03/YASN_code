using System.Runtime.InteropServices;

namespace YASN.Diagnostics
{
    /// <summary>
    /// Process-wide diagnose state. While enabled the app raises a console window and streams
    /// <see cref="AppLogger"/> output to it, so a Release build can be observed live. The app is a
    /// GUI-subsystem executable with no console of its own, so the window is allocated on demand and
    /// freed when diagnose is turned off. Windows-only; a no-op on other platforms.
    /// </summary>
    public static class DiagnoseMode
    {
        private static readonly Lock Gate = new();
        private static bool _consoleAllocated;
        private static StreamWriter? _consoleWriter;

        /// <summary>
        /// Gets whether diagnose mode is currently enabled.
        /// </summary>
        public static bool IsEnabled { get; private set; }

        /// <summary>
        /// Enables or disables diagnose mode. Enabling raises a console (Windows) and turns on the
        /// logger's console echo; disabling turns the echo off and frees the console. Idempotent and
        /// failure-tolerant: a console that cannot be allocated leaves diagnose logically enabled so
        /// callers (e.g. the preview DevTools hook) still react.
        /// </summary>
        /// <param name="enabled">The desired diagnose state.</param>
        public static void SetEnabled(bool enabled)
        {
            lock (Gate)
            {
                if (enabled == IsEnabled)
                {
                    return;
                }

                IsEnabled = enabled;
                if (enabled)
                {
                    RaiseConsole();
                    AppLogger.ConsoleEchoEnabled = true;
                    AppLogger.Info("Diagnose mode enabled.");
                }
                else
                {
                    AppLogger.Info("Diagnose mode disabled.");
                    AppLogger.ConsoleEchoEnabled = false;
                    ReleaseConsole();
                }
            }
        }

        /// <summary>
        /// Allocates a console for the current GUI process and rebinds <see cref="Console.Out"/>/
        /// <see cref="Console.Error"/> to it so managed writes are visible. No-op when one is already
        /// allocated or off Windows.
        /// </summary>
        private static void RaiseConsole()
        {
            if (!OperatingSystem.IsWindows() || _consoleAllocated)
            {
                return;
            }

            try
            {
                if (!AllocConsole())
                {
                    return;
                }

                _consoleAllocated = true;
                RebindStandardStreams();
                TrySetTitle("YASN diagnostics");
            }
            catch (DllNotFoundException)
            {
                // kernel32 is always present on supported Windows; guard defensively regardless.
            }
        }

        /// <summary>
        /// Frees a console previously allocated by <see cref="RaiseConsole"/>. No-op when none was
        /// allocated or off Windows.
        /// </summary>
        private static void ReleaseConsole()
        {
            if (!OperatingSystem.IsWindows() || !_consoleAllocated)
            {
                return;
            }

            try
            {
                _ = FreeConsole();
            }
            catch (DllNotFoundException)
            {
            }
            finally
            {
                _consoleWriter?.Dispose();
                _consoleWriter = null;
                _consoleAllocated = false;
            }
        }

        /// <summary>
        /// Points <see cref="Console.Out"/> and <see cref="Console.Error"/> at the freshly allocated
        /// console's <c>CONOUT$</c>. After <see cref="AllocConsole"/> the runtime's cached standard
        /// handles still refer to the original (discarded) streams, so writes would otherwise vanish.
        /// </summary>
        [System.Runtime.Versioning.SupportedOSPlatform("windows")]
        private static void RebindStandardStreams()
        {
            try
            {
                StreamWriter writer = new(new FileStream("CONOUT$", FileMode.Open, FileAccess.Write))
                {
                    AutoFlush = true
                };
                _consoleWriter = writer;
                Console.SetOut(writer);
                Console.SetError(writer);
            }
            catch (IOException)
            {
                // The console exists but its output device could not be opened; logging still reaches
                // the file. Leave the cached streams in place rather than fail the toggle.
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

        [System.Runtime.Versioning.SupportedOSPlatform("windows")]
        private static void TrySetTitle(string title)
        {
            try
            {
                Console.Title = title;
            }
            catch (IOException)
            {
            }
        }

        [System.Runtime.Versioning.SupportedOSPlatform("windows")]
        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool AllocConsole();

        [System.Runtime.Versioning.SupportedOSPlatform("windows")]
        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool FreeConsole();
    }
}
