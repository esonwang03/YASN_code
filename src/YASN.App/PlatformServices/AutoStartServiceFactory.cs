using System.Diagnostics;
using YASN.Infrastructure;

namespace YASN.PlatformServices
{
    /// <summary>
    /// Selects the auto-start service for the current operating system.
    /// </summary>
    public static class AutoStartServiceFactory
    {
        /// <summary>
        /// Creates the auto-start service for the current platform.
        /// </summary>
        /// <returns>A platform service, or an unsupported service when no launcher integration exists.</returns>
        public static IAutoStartService Create()
        {
            string executablePath = ResolveExecutablePath();

            if (OperatingSystem.IsWindows())
            {
                return new WindowsAutoStartService(executablePath);
            }

            if (OperatingSystem.IsMacOS())
            {
                return new MacOsAutoStartService(executablePath, AppPaths.MacLaunchAgentsDirectory);
            }

            return new UnsupportedAutoStartService();
        }

        private static string ResolveExecutablePath()
        {
            return Environment.ProcessPath
                ?? Process.GetCurrentProcess().MainModule?.FileName
                ?? AppContext.BaseDirectory;
        }
    }
}
