using Avalonia;
using Avalonia.Controls;
using YASN.Application;
using YASN.Cli;
using YASN.Diagnostics;

namespace YASN
{
    /// <summary>
    /// Starts the Avalonia desktop application.
    /// </summary>
    public static class Program
    {
        /// <summary>
        /// Builds and starts the desktop lifetime for the YASN application.
        /// </summary>
        /// <remarks>
        /// Marked <see cref="STAThreadAttribute"/> because the WebView2 preview control hosts COM
        /// objects that require a single-threaded apartment; without it WebView2 initialization
        /// fails with <c>RPC_E_CHANGED_MODE</c> (0x80010106).
        /// </remarks>
        [STAThread]
        public static int Main(string[] args)
        {
            // Capture process-wide unhandled exceptions before anything else runs, so even early
            // startup and the CLI path leave their crash cause in the log rather than quitting quietly.
            GlobalExceptionHandler.RegisterProcessWide();

            // With arguments, YASN acts as a command-line front-end: it never starts Avalonia, but
            // serves read-only verbs directly or routes UI/state verbs to the running tray instance
            // over IPC (auto-launching it when needed). With no arguments it starts the tray app.
            if (args.Length > 0)
            {
                return CliEntry.Run(args);
            }

            // Redirect native-library loads to Contents/Frameworks on macOS before any native
            // code loads. Must run before AppBuilderFactory.Create(), whose UsePlatformDetect()
            // triggers the first Avalonia/Skia P/Invoke.
            NativeLibraryResolver.Register();

            // Give the process an explicit AppUserModelID so Windows treats this build as its own
            // taskbar identity. Without it, the shell falls back to a path/heuristic identity that a
            // prior YASN install can collide with, causing the old app's cached icon to appear on our
            // windows. A stable, app-specific ID also keeps taskbar grouping correct.
            SetWindowsAppUserModelId("YASN.StickyNotes");

            return AppBuilderFactory.Create()
                .StartWithClassicDesktopLifetime(args, ShutdownMode.OnExplicitShutdown);
        }

        private static void SetWindowsAppUserModelId(string appId)
        {
            if (!OperatingSystem.IsWindows())
            {
                return;
            }

            try
            {
                _ = SetCurrentProcessExplicitAppUserModelID(appId);
            }
            catch (System.Runtime.InteropServices.COMException)
            {
                // Non-fatal: the ID is a shell hint, not required for the app to run.
            }
            catch (DllNotFoundException)
            {
            }
        }

        [System.Runtime.Versioning.SupportedOSPlatform("windows")]
        [System.Runtime.InteropServices.DllImport("shell32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode, PreserveSig = false)]
        private static extern int SetCurrentProcessExplicitAppUserModelID(
            [System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.LPWStr)] string appId);
    }
}
