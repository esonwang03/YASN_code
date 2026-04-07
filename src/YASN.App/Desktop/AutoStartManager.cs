using System.IO;
using System.Security;
using Microsoft.Win32;
using YASN.Infrastructure.Logging;

namespace YASN.App.Desktop
{
    public static class AutoStartManager
    {
        private const string AppName = "YASN";
        private const string RegistryKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";


        public static bool IsAutoStartEnabled()
        {
            try
            {
                using RegistryKey? key = Registry.CurrentUser.OpenSubKey(RegistryKey, false);
                if (key == null)
                    return false;

                string? value = key.GetValue(AppName) as string;
                if (string.IsNullOrEmpty(value))
                    return false;

                string currentPath = GetApplicationPath();
                return value.Equals(currentPath, StringComparison.OrdinalIgnoreCase);
            }
            catch (IOException ex)
            {
                AppLogger.Warn($"Failed to read auto-start setting: {ex.Message}");
                return false;
            }
            catch (SecurityException ex)
            {
                AppLogger.Warn($"Failed to read auto-start setting: {ex.Message}");
                return false;
            }
            catch (UnauthorizedAccessException ex)
            {
                AppLogger.Warn($"Failed to read auto-start setting: {ex.Message}");
                return false;
            }
        }

        public static bool EnableAutoStart()
        {
            try
            {
                using RegistryKey? key = Registry.CurrentUser.OpenSubKey(RegistryKey, true);
                if (key == null)
                    return false;

                string applicationPath = GetApplicationPath();
                key.SetValue(AppName, applicationPath);
                return true;
            }
            catch (IOException ex)
            {
                AppLogger.Warn($"Failed to enable auto-start: {ex.Message}");
                return false;
            }
            catch (SecurityException ex)
            {
                AppLogger.Warn($"Failed to enable auto-start: {ex.Message}");
                return false;
            }
            catch (UnauthorizedAccessException ex)
            {
                AppLogger.Warn($"Failed to enable auto-start: {ex.Message}");
                return false;
            }
        }

        public static bool DisableAutoStart()
        {
            try
            {
                using RegistryKey? key = Registry.CurrentUser.OpenSubKey(RegistryKey, true);
                if (key == null)
                    return false;

                if (key.GetValue(AppName) != null)
                {
                    key.DeleteValue(AppName, false);
                }
                return true;
            }
            catch (IOException ex)
            {
                AppLogger.Warn($"Failed to disable auto-start: {ex.Message}");
                return false;
            }
            catch (SecurityException ex)
            {
                AppLogger.Warn($"Failed to disable auto-start: {ex.Message}");
                return false;
            }
            catch (UnauthorizedAccessException ex)
            {
                AppLogger.Warn($"Failed to disable auto-start: {ex.Message}");
                return false;
            }
        }

        public static bool ToggleAutoStart()
        {
            return IsAutoStartEnabled() ? DisableAutoStart() : EnableAutoStart();
        }

        private static string GetApplicationPath()
        {
            string? exePath = System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName;
            if (string.IsNullOrEmpty(exePath))
            {
                exePath = System.Reflection.Assembly.GetExecutingAssembly().Location;
            }

            if (exePath.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
            {
                exePath = Path.ChangeExtension(exePath, ".exe");
            }

            return $"\"{exePath}\"";
        }
    }
}
