using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Security;
using System.Text.Json;
using Microsoft.Toolkit.Uwp.Notifications;

namespace YASN.Infrastructure.Logging
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
            catch (IOException ex)
            {
                ReportInternalFailure("Write", ex);
            }
            catch (UnauthorizedAccessException ex)
            {
                ReportInternalFailure("Write", ex);
            }
            catch (NotSupportedException ex)
            {
                ReportInternalFailure("Write", ex);
            }
            catch (SecurityException ex)
            {
                ReportInternalFailure("Write", ex);
            }
        }

        private static void WriteToTerminalInDebug(string line)
        {
#if DEBUG
            try
            {
                Console.WriteLine(line);
            }
            catch (IOException ex)
            {
                ReportInternalFailure("WriteToTerminalInDebug", ex);
            }
            catch (ObjectDisposedException ex)
            {
                ReportInternalFailure("WriteToTerminalInDebug", ex);
            }
            catch (SecurityException ex)
            {
                ReportInternalFailure("WriteToTerminalInDebug", ex);
            }
#endif
        }

        private static void EnsureDirectory()
        {
            string? dir = Path.GetDirectoryName(LogPath);
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
                    FileInfo info = new FileInfo(LogPath);
                    if (info.Length >= _maxBytes)
                    {
                        string bakPath = LogPath + ".bak";
                        if (File.Exists(bakPath))
                        {
                            File.Delete(bakPath);
                        }
                        File.Move(LogPath, bakPath);
                    }
                }
            }
            catch (IOException ex)
            {
                ReportInternalFailure("RotateIfNeeded", ex);
            }
            catch (UnauthorizedAccessException ex)
            {
                ReportInternalFailure("RotateIfNeeded", ex);
            }
            catch (NotSupportedException ex)
            {
                ReportInternalFailure("RotateIfNeeded", ex);
            }
            catch (SecurityException ex)
            {
                ReportInternalFailure("RotateIfNeeded", ex);
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
            catch (COMException ex)
            {
                ReportInternalFailure("ShowToast", ex);
            }
            catch (InvalidOperationException ex)
            {
                ReportInternalFailure("ShowToast", ex);
            }
            catch (ArgumentException ex)
            {
                ReportInternalFailure("ShowToast", ex);
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
                    string json = File.ReadAllText(AppPaths.LocalSettingsPath);
                    var dict = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
                    if (dict != null && dict.TryGetValue("log.maxSizeKb", out string? value) &&
                        int.TryParse(value, out int kb) && kb > 0)
                    {
                        SetMaxSizeKb(kb);
                    }

                    if (dict != null && dict.TryGetValue("log.toastExpirationSeconds", out string? toastSeconds) &&
                        int.TryParse(toastSeconds, out int seconds) && seconds > 0)
                    {
                        SetToastExpirationSeconds(seconds);
                    }
                }
            }
            catch (IOException ex)
            {
                ReportInternalFailure("LoadMaxSizeFromLocalSettings", ex);
            }
            catch (UnauthorizedAccessException ex)
            {
                ReportInternalFailure("LoadMaxSizeFromLocalSettings", ex);
            }
            catch (JsonException ex)
            {
                ReportInternalFailure("LoadMaxSizeFromLocalSettings", ex);
            }
            catch (NotSupportedException ex)
            {
                ReportInternalFailure("LoadMaxSizeFromLocalSettings", ex);
            }
            catch (SecurityException ex)
            {
                ReportInternalFailure("LoadMaxSizeFromLocalSettings", ex);
            }
        }

        [Conditional("DEBUG")]
        private static void ReportInternalFailure(string operation, Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"AppLogger.{operation} failed: {ex}");
        }
    }
}
