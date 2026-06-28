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
        private const string DefaultValueName = "YASN";
        private readonly string valueName;
        private readonly string executablePath;

        /// <summary>
        /// Initializes the service for the running executable.
        /// </summary>
        /// <param name="executablePath">The fully qualified launcher path stored in the Run key.</param>
        /// <param name="valueName">
        /// The Run-key value name to read and write. Defaults to the production name; tests pass an
        /// isolated name so they never read or delete the real install's auto-start entry.
        /// </param>
        public WindowsAutoStartService(string executablePath, string valueName = DefaultValueName)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(executablePath);
            ArgumentException.ThrowIfNullOrWhiteSpace(valueName);
            this.executablePath = executablePath;
            this.valueName = valueName;
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
                return key?.GetValue(valueName) is string value
                    && !string.IsNullOrWhiteSpace(Unquote(value));
            }
        }

        /// <summary>
        /// Writes the Run key value for the current executable.
        /// </summary>
        public void Enable()
        {
            using RegistryKey key = Registry.CurrentUser.CreateSubKey(RunKeyPath);
            key.SetValue(valueName, $"\"{executablePath}\"");
        }

        /// <summary>
        /// Removes the Run key value when present.
        /// </summary>
        public void Disable()
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
            if (key?.GetValue(valueName) is not null)
            {
                key.DeleteValue(valueName, throwOnMissingValue: false);
            }
        }

        private static string Unquote(string value)
        {
            return value.Trim().Trim('"');
        }
    }
}
