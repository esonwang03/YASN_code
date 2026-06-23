using System.Runtime.InteropServices;

namespace YASN.Cli
{
    /// <summary>
    /// Console attachment for the CLI path. The app is built as a GUI subsystem executable
    /// (<c>OutputType=WinExe</c>), so on Windows it has no console of its own and
    /// <see cref="System.Console"/> output is discarded when launched from a terminal. Attaching to
    /// the parent process's console makes CLI output visible. A no-op on macOS/Linux, where a GUI
    /// process still inherits the launching terminal's standard streams.
    /// </summary>
    public static class ConsoleInterop
    {
        private const int AttachParentProcess = -1;

        /// <summary>
        /// Attaches the current process to its parent's console on Windows so CLI output is visible.
        /// Best-effort: failure (no parent console, e.g. launched from a GUI shell) is ignored.
        /// </summary>
        public static void AttachToParentConsole()
        {
            if (!OperatingSystem.IsWindows())
            {
                return;
            }

            try
            {
                _ = AttachConsole(AttachParentProcess);
            }
            catch (DllNotFoundException)
            {
                // kernel32 is always present on supported Windows; guard defensively regardless.
            }
        }

        [System.Runtime.Versioning.SupportedOSPlatform("windows")]
        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool AttachConsole(int processId);
    }
}
