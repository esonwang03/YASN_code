using System.Runtime.InteropServices;

namespace YASN.PlatformServices
{
    /// <summary>
    /// Selects the global hotkey service for the current operating system.
    /// </summary>
    public static class GlobalHotkeyServiceFactory
    {
        /// <summary>
        /// Creates the platform global hotkey service. Must be called on the UI thread on Windows so
        /// the message window's <c>WM_HOTKEY</c> messages are pumped by Avalonia's loop.
        /// </summary>
        /// <returns>The global hotkey service for the current platform.</returns>
        public static IGlobalHotkeyService Create()
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                return new WindowsGlobalHotkeyService();
            }

            return new UnsupportedGlobalHotkeyService();
        }
    }
}
