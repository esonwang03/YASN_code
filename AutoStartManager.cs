using System;
using System.IO;
using Microsoft.Win32;

namespace YASN
{
    /// <summary>
    /// 管理应用程序开机自启动的类
    /// </summary>
    public static class AutoStartManager
    {
        private const string AppName = "YASN";
        private const string RegistryKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";

        /// <summary>
        /// 检查是否已设置开机自启动
        /// </summary>
        public static bool IsAutoStartEnabled()
        {
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(RegistryKey, false))
                {
                    if (key == null)
                        return false;

                    var value = key.GetValue(AppName) as string;
                    if (string.IsNullOrEmpty(value))
                        return false;

                    // 检查注册表中的路径是否与当前程序路径匹配
                    var currentPath = GetApplicationPath();
                    return value.Equals(currentPath, StringComparison.OrdinalIgnoreCase);
                }
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// 启用开机自启动
        /// </summary>
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

        /// <summary>
        /// 禁用开机自启动
        /// </summary>
        public static bool DisableAutoStart()
        {
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(RegistryKey, true))
                {
                    if (key == null)
                        return false;

                    // 如果值存在，则删除
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

        /// <summary>
        /// 切换开机自启动状态
        /// </summary>
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

        /// <summary>
        /// 获取当前应用程序的完整路径
        /// </summary>
        private static string GetApplicationPath()
        {
            var exePath = System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName;
            if (string.IsNullOrEmpty(exePath))
            {
                exePath = System.Reflection.Assembly.GetExecutingAssembly().Location;
            }
            
            // 如果是 .dll，尝试获取对应的 .exe
            if (exePath.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
            {
                exePath = Path.ChangeExtension(exePath, ".exe");
            }
            
            return $"\"{exePath}\"";
        }
    }
}
