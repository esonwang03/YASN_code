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
        private static int _toastExpirationSeconds = 8; // default 8 seconds

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

        public static void DebugToast(string message) => Write("DEBUG", message, showToast: true);

        public static void Info(string message) => Write("INFO", message, showToast: true);

        public static void Warn(string message) => Write("WARN", message, showToast: true);

        public static void SetToastExpirationSeconds(int seconds)
        {
            if (seconds <= 0)
                return;

            _toastExpirationSeconds = Math.Clamp(seconds, 1, 120);
        }

        private static void Write(string level, string message, bool showToast)
        {
            try
            {
                string line;
                lock (Lock)
                {
                    EnsureDirectory();
                    RotateIfNeeded();

                    line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{level}] {message}";
                    File.AppendAllLines(LogPath, [line]);
                }

                WriteToTerminalInDebug(line);

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

        private static void WriteToTerminalInDebug(string line)
        {
#if DEBUG
            try
            {
                Console.WriteLine(line);
            }
            catch
            {
                // ignore terminal output failures
            }
#endif
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
                var (icon, title) = GetToastHeader(level);
                new ToastContentBuilder()
                    .AddText($"{icon} {title}")
                    .AddText(message)
                    .Show(toast => { toast.ExpirationTime = DateTimeOffset.Now.AddSeconds(_toastExpirationSeconds); });
            }
            catch
            {
                // Toast failures should not crash the app
            }
        }

        private static (string Icon, string Title) GetToastHeader(string level)
        {
            return level switch
            {
                "INFO" => ("\u2139", "Info"),
                "WARN" => ("\u26A0", "Warning"),
                "DEBUG" => ("\u2699", "Debug"),
                _ => ("\u2022", level)
            };
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

                    if (dict != null && dict.TryGetValue("log.toastExpirationSeconds", out var toastSeconds) &&
                        int.TryParse(toastSeconds, out var seconds) && seconds > 0)
                    {
                        SetToastExpirationSeconds(seconds);
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
