using System.Diagnostics;

namespace YASN.Cli
{
    /// <summary>
    /// Reveals a directory in the platform file manager (Explorer on Windows, Finder on macOS,
    /// the default handler on Linux). Used by the <c>open-data</c> and <c>open-cache</c> CLI verbs.
    /// </summary>
    public static class ShellFolderOpener
    {
        /// <summary>
        /// Opens the given directory in the platform file manager.
        /// </summary>
        /// <param name="path">The absolute directory path to reveal.</param>
        /// <returns><see langword="true"/> when the file manager was launched.</returns>
        public static bool Open(string path)
        {
            try
            {
                ProcessStartInfo startInfo = BuildStartInfo(path);
                using Process? process = Process.Start(startInfo);
                return true;
            }
            catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or PlatformNotSupportedException)
            {
                AppLogger.Warn($"Could not open folder '{path}': {ex.Message}");
                return false;
            }
        }

        private static ProcessStartInfo BuildStartInfo(string path)
        {
            if (OperatingSystem.IsWindows())
            {
                // explorer.exe takes the folder as a bare argument; UseShellExecute is unnecessary.
                return new ProcessStartInfo("explorer.exe", path) { UseShellExecute = false };
            }

            if (OperatingSystem.IsMacOS())
            {
                return new ProcessStartInfo("open", path) { UseShellExecute = false };
            }

            return new ProcessStartInfo("xdg-open", path) { UseShellExecute = false };
        }
    }
}
