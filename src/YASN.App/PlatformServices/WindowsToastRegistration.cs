using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Avalonia.Platform;
using Microsoft.Win32;
using YASN.Infrastructure;

namespace YASN.PlatformServices
{
    /// <summary>
    /// Registers the application's AppUserModelID (AUMID) so Windows displays toast notifications
    /// for this unpackaged Win32 process, and tears that registration back down.
    /// </summary>
    /// <remarks>
    /// An unpackaged desktop app must establish an explicit AUMID for the toast subsystem to attribute
    /// notifications to it; without one, <c>CreateToastNotifierWithId</c> and <c>Show</c> both succeed
    /// but the OS silently drops the toast — there is no error to observe. Two things are required:
    /// <list type="number">
    /// <item>The process must declare the AUMID via <c>SetCurrentProcessExplicitAppUserModelID</c>.</item>
    /// <item>The AUMID must exist under <c>HKCU\Software\Classes\AppUserModelId\&lt;aumid&gt;</c> with a
    /// <c>DisplayName</c>, which is what makes the shell recognize it as a notifying app. An optional
    /// <c>IconUri</c> pointing at an image file gives the toast and Action Center the app icon.</item>
    /// </list>
    /// This mirrors the per-user registry approach already used by <see cref="WindowsAutoStartService"/>.
    /// In addition, <see cref="Ensure"/> creates a Start Menu shortcut stamped with the AUMID via
    /// <see cref="WindowsStartMenuShortcut"/> — the shell's canonical AUMID anchor — so both anchors
    /// agree on this app's notifying identity.
    /// </remarks>
    [SupportedOSPlatform("windows")]
    public static class WindowsToastRegistration
    {
        // HKCU subtree the shell consults to resolve an AUMID to a notifying app identity.
        private const string AppUserModelIdRoot = @"Software\Classes\AppUserModelId";

        // The packaged application icon, materialized to disk so IconUri can reference a real file
        // (the icon ships only as an embedded Avalonia resource — the registry needs a path).
        private static readonly Uri IconResource = new("avares://YASN/Resources/YASN.ico");

        /// <summary>
        /// Declares <paramref name="appUserModelId"/> for the current process and ensures the matching
        /// per-user registry identity exists, so toasts sent under this AUMID are actually shown.
        /// </summary>
        /// <param name="appUserModelId">The AppUserModelID used to obtain the toast notifier.</param>
        /// <param name="displayName">The app name shown in Action Center for this AUMID.</param>
        public static void Ensure(string appUserModelId, string displayName)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(appUserModelId);
            ArgumentException.ThrowIfNullOrWhiteSpace(displayName);

            try
            {
                using RegistryKey key = Registry.CurrentUser.CreateSubKey(
                    $@"{AppUserModelIdRoot}\{appUserModelId}");
                // The shell keys off DisplayName; write it every time so a renamed app self-heals.
                key.SetValue("DisplayName", displayName, RegistryValueKind.String);

                string? iconPath = MaterializeIcon();
                if (iconPath is not null)
                {
                    key.SetValue("IconUri", iconPath, RegistryValueKind.String);
                }
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or IOException)
            {
                // Registration is best-effort: log and continue so a locked-down registry does not
                // crash startup. Toasts simply stay invisible, matching the prior behavior.
                AppLogger.Warn($"Failed to register toast AppUserModelID: {ex.Message}");
                return;
            }

            // A Start Menu shortcut carrying this AUMID is the shell's canonical anchor for an
            // unpackaged app's toast identity; pair it with the registry identity written above.
            WindowsStartMenuShortcut.Ensure(appUserModelId, displayName);

            int hr = SetCurrentProcessExplicitAppUserModelID(appUserModelId);
            if (hr != 0)
            {
                AppLogger.Warn($"SetCurrentProcessExplicitAppUserModelID failed: 0x{hr:X8}");
            }
        }

        /// <summary>
        /// Removes the per-user AUMID registration and Start Menu shortcut written by <see cref="Ensure"/>.
        /// </summary>
        /// <param name="appUserModelId">The AppUserModelID whose registry identity to remove.</param>
        /// <param name="displayName">The display name used to name the Start Menu shortcut at creation.</param>
        /// <returns><c>true</c> when a registration or shortcut existed and was removed; <c>false</c> when neither was present.</returns>
        public static bool Unregister(string appUserModelId, string displayName)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(appUserModelId);
            ArgumentException.ThrowIfNullOrWhiteSpace(displayName);

            // Remove the shortcut regardless of the registry outcome so neither anchor is orphaned.
            bool shortcutRemoved = WindowsStartMenuShortcut.Remove(displayName);

            string subKey = $@"{AppUserModelIdRoot}\{appUserModelId}";
            try
            {
                using (RegistryKey? existing = Registry.CurrentUser.OpenSubKey(subKey))
                {
                    if (existing is null)
                    {
                        return shortcutRemoved;
                    }
                }

                Registry.CurrentUser.DeleteSubKeyTree(subKey, throwOnMissingSubKey: false);
                return true;
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or IOException)
            {
                AppLogger.Warn($"Failed to unregister toast AppUserModelID: {ex.Message}");
                return shortcutRemoved;
            }
        }

        /// <summary>
        /// Writes the embedded application icon to a stable per-user path on first use and returns it,
        /// so <c>IconUri</c> can point at a real file. Best-effort: returns <c>null</c> on failure.
        /// </summary>
        private static string? MaterializeIcon()
        {
            try
            {
                string iconPath = Path.Combine(AppPaths.PersistentRoot, "YASN.ico");
                if (!File.Exists(iconPath))
                {
                    Directory.CreateDirectory(AppPaths.PersistentRoot);
                    using Stream source = AssetLoader.Open(IconResource);
                    using FileStream destination = File.Create(iconPath);
                    source.CopyTo(destination);
                }

                return iconPath;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
            {
                // InvalidOperationException covers AssetLoader running before the Avalonia locator is
                // initialized (e.g. headless tests); the icon is optional, so degrade without it.
                AppLogger.Warn($"Failed to materialize toast icon: {ex.Message}");
                return null;
            }
        }

        // shell32: associates the calling process with an AUMID. Returns an HRESULT (S_OK == 0).
        [DllImport("shell32.dll", PreserveSig = true, CharSet = CharSet.Unicode)]
        private static extern int SetCurrentProcessExplicitAppUserModelID(
            [MarshalAs(UnmanagedType.LPWStr)] string appID);
    }
}
