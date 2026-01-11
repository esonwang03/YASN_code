using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Microsoft.Toolkit.Uwp.Notifications;

namespace YASN.Logging
{
    /// <summary>
    /// Simple layered logger with file output and toast notifications for info/warn.
    /// </summary>
    public static class AppLogger
    {
        private static readonly object Lock = new();
        private static readonly string LogPath = AppPaths.LogFilePath;
        private static long _maxBytes = 1024 * 1024; // default 1 MB

        static AppLogger()
        {
            LoadMaxSizeFromLocalSettings();
        }

        public static void SetMaxSizeKb(int kb)
        {
            if (kb <= 0)
                return;

            _maxBytes = kb * 1024L;
        }

        public static void Debug(string message) => Write("DEBUG", message, showToast: false);

        public static void Info(string message) => Write("INFO", message, showToast: true);

        public static void Warn(string message) => Write("WARN", message, showToast: true);

        private static void Write(string level, string message, bool showToast)
        {
            try
            {
                lock (Lock)
                {
                    EnsureDirectory();
                    RotateIfNeeded();

                    var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{level}] {message}";
                    File.AppendAllLines(LogPath, new[] { line });
                }

                if (showToast)
                {
                    ShowToast(level, message);
                }
            }
            catch
            {
                // Avoid throwing from logger
            }
        }

        private static void EnsureDirectory()
        {
            var dir = Path.GetDirectoryName(LogPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }
        }

        private static void RotateIfNeeded()
        {
            try
            {
                if (File.Exists(LogPath))
                {
                    var info = new FileInfo(LogPath);
                    if (info.Length >= _maxBytes)
                    {
                        var bakPath = LogPath + ".bak";
                        if (File.Exists(bakPath))
                        {
                            File.Delete(bakPath);
                        }
                        File.Move(LogPath, bakPath);
                    }
                }
            }
            catch
            {
                // ignore rotation failures
            }
        }

        private static void ShowToast(string level, string message)
        {
            try
            {
                new ToastContentBuilder()
                    .AddText(level)
                    .AddText(message)
                    .Show();
            }
            catch
            {
                // Toast failures should not crash the app
            }
        }

        private static void LoadMaxSizeFromLocalSettings()
        {
            try
            {
                if (File.Exists(AppPaths.LocalSettingsPath))
                {
                    var json = File.ReadAllText(AppPaths.LocalSettingsPath);
                    var dict = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
                    if (dict != null && dict.TryGetValue("log.maxSizeKb", out var value) &&
                        int.TryParse(value, out var kb) && kb > 0)
                    {
                        SetMaxSizeKb(kb);
                    }
                }
            }
            catch
            {
                // ignore
            }
        }
    }
}
