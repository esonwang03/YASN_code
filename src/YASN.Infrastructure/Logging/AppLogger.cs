using System.Diagnostics;
using System.Security;
using System.Text.Json;

namespace YASN.Infrastructure.Logging
{
    /// <summary>
    /// Writes application diagnostics to the local log file and debug console.
    /// </summary>
    public static class AppLogger
    {
        private static readonly Lock Lock = new();
        private static readonly string LogPath = AppPaths.LogFilePath;
        private static long _maxBytes = 1024 * 1024;

        static AppLogger()
        {
            LoadMaxSizeFromLocalSettings();
        }

        /// <summary>
        /// Updates the maximum log-file size before rotation.
        /// </summary>
        /// <param name="kb">The maximum size in kilobytes.</param>
        public static void SetMaxSizeKb(int kb)
        {
            if (kb <= 0)
            {
                return;
            }

            _maxBytes = kb * 1024L;
        }

        /// <summary>
        /// Writes a debug-level diagnostic message.
        /// </summary>
        /// <param name="message">The message to record.</param>
        public static void Debug(string message)
        {
            Write("DEBUG", message);
        }

        /// <summary>
        /// Writes an informational diagnostic message.
        /// </summary>
        /// <param name="message">The message to record.</param>
        public static void Info(string message)
        {
            Write("INFO", message);
        }

        /// <summary>
        /// Writes a warning diagnostic message.
        /// </summary>
        /// <param name="message">The message to record.</param>
        public static void Warn(string message)
        {
            Write("WARN", message);
        }

        private static void Write(string level, string message)
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
                if (!File.Exists(LogPath))
                {
                    return;
                }

                FileInfo info = new FileInfo(LogPath);
                if (info.Length < _maxBytes)
                {
                    return;
                }

                string backupPath = LogPath + ".bak";
                if (File.Exists(backupPath))
                {
                    File.Delete(backupPath);
                }

                File.Move(LogPath, backupPath);
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

        private static void LoadMaxSizeFromLocalSettings()
        {
            try
            {
                if (!File.Exists(AppPaths.LocalSettingsPath))
                {
                    return;
                }

                string json = File.ReadAllText(AppPaths.LocalSettingsPath);
                Dictionary<string, string>? dict = JsonSerializer.Deserialize(json, InfrastructureJsonContext.Default.DictionaryStringString);
                if (dict != null &&
                    dict.TryGetValue("log.maxSizeKb", out string? value) &&
                    int.TryParse(value, out int kb) &&
                    kb > 0)
                {
                    SetMaxSizeKb(kb);
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
