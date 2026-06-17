using System.Runtime.Versioning;
using Microsoft.Win32;

namespace YASN.PlatformServices
{
    /// <summary>
    /// Enables auto-start on Windows through the per-user Run registry key.
    /// </summary>
    [SupportedOSPlatform("windows")]
    public sealed class WindowsAutoStartService : IAutoStartService
    {
        private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string ValueName = "YASN";
        private readonly string executablePath;

        /// <summary>
        /// Initializes the service for the running executable.
        /// </summary>
        /// <param name="executablePath">The fully qualified launcher path stored in the Run key.</param>
        public WindowsAutoStartService(string executablePath)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(executablePath);
            this.executablePath = executablePath;
        }

        /// <summary>
        /// Gets whether auto-start is supported on this platform.
        /// </summary>
        public bool IsSupported => true;

        /// <summary>
        /// Gets whether the Run key holds an auto-start entry for YASN. A present, non-empty value
        /// counts as enabled regardless of the stored path: the executable location changes between a
        /// dev run and the published app (and across reinstalls), and strict path equality would make
        /// the toggle read as off after every such change even though an entry exists. <see cref="Enable"/>
        /// always rewrites the value to the current executable, so a stale path self-heals on next save.
        /// </summary>
        public bool IsEnabled
        {
            get
            {
                using RegistryKey? key = Registry.CurrentUser.OpenSubKey(RunKeyPath);
                return key?.GetValue(ValueName) is string value
                    && !string.IsNullOrWhiteSpace(Unquote(value));
            }
        }

        /// <summary>
        /// Writes the Run key value for the current executable.
        /// </summary>
        public void Enable()
        {
            using RegistryKey key = Registry.CurrentUser.CreateSubKey(RunKeyPath);
            key.SetValue(ValueName, $"\"{executablePath}\"");
        }

        /// <summary>
        /// Removes the Run key value when present.
        /// </summary>
        public void Disable()
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
            if (key?.GetValue(ValueName) is not null)
            {
                key.DeleteValue(ValueName, throwOnMissingValue: false);
            }
        }

        private static string Unquote(string value)
        {
            return value.Trim().Trim('"');
        }
    }
}
