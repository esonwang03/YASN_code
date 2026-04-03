using System;
using System.IO;
using Microsoft.Win32;

namespace YASN
{
    /// <summary>
    /// ����Ӧ�ó��򿪻�����������
    /// </summary>
    public static class AutoStartManager
    {
        private const string AppName = "YASN";
        private const string RegistryKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";


        public static bool IsAutoStartEnabled()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(RegistryKey, false);
                if (key == null)
                    return false;

                var value = key.GetValue(AppName) as string;
                if (string.IsNullOrEmpty(value))
                    return false;

                var currentPath = GetApplicationPath();
                return value.Equals(currentPath, StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }
        
        public static bool EnableAutoStart()
        {
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(RegistryKey, true))
                {
                    if (key == null)
                        return false;

                    var applicationPath = GetApplicationPath();
                    key.SetValue(AppName, applicationPath);
                    return true;
                }
            }
            catch (Exception)
            {
                return false;
            }
        }
        
        public static bool DisableAutoStart()
        {
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(RegistryKey, true))
                {
                    if (key == null)
                        return false;
                    
                    if (key.GetValue(AppName) != null)
                    {
                        key.DeleteValue(AppName, false);
                    }
                    return true;
                }
            }
            catch (Exception)
            {
                return false;
            }
        }
        
        public static bool ToggleAutoStart()
        {
            if (IsAutoStartEnabled())
            {
                return DisableAutoStart();
            }
            else
            {
                return EnableAutoStart();
            }
        }
        
        private static string GetApplicationPath()
        {
            var exePath = System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName;
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
