using System.Diagnostics;
using System.Runtime.InteropServices;

namespace YASN.PlatformServices
{
    /// <summary>
    /// Opens files and URLs in the operating system's default application. Used so attachment links
    /// in the note preview launch their associated app rather than navigating the embedded WebView.
    /// </summary>
    public static class SystemShellLauncher
    {
        /// <summary>
        /// Opens a local file path or absolute URL with the default OS handler.
        /// </summary>
        /// <param name="target">The file path or URL to open.</param>
        /// <returns><c>true</c> when the launch was started; otherwise <c>false</c>.</returns>
        public static bool Open(string target)
        {
            if (string.IsNullOrWhiteSpace(target))
            {
                return false;
            }

            try
            {
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    // ShellExecute resolves the registered handler for the file type or protocol.
                    Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
                    return true;
                }

                if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                {
                    Process.Start(new ProcessStartInfo("open", QuoteArgument(target)) { UseShellExecute = false });
                    return true;
                }

                if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                {
                    Process.Start(new ProcessStartInfo("xdg-open", QuoteArgument(target)) { UseShellExecute = false });
                    return true;
                }

                return false;
            }
            catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or FileNotFoundException)
            {
                return false;
            }
        }

        private static string QuoteArgument(string value)
        {
            return "\"" + value.Replace("\"", "\\\"", StringComparison.Ordinal) + "\"";
        }
    }
}
