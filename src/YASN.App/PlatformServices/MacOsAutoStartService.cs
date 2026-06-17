using System.Runtime.Versioning;

namespace YASN.PlatformServices
{
    /// <summary>
    /// Enables auto-start on macOS through a per-user LaunchAgent plist.
    /// </summary>
    [SupportedOSPlatform("macos")]
    public sealed class MacOsAutoStartService : IAutoStartService
    {
        private const string AgentLabel = "com.yasn.autostart";
        private readonly string executablePath;
        private readonly string plistPath;

        /// <summary>
        /// Initializes the service for the running executable.
        /// </summary>
        /// <param name="executablePath">The launcher path written into the LaunchAgent.</param>
        /// <param name="launchAgentsDirectory">The user LaunchAgents directory that holds the plist.</param>
        public MacOsAutoStartService(string executablePath, string launchAgentsDirectory)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(executablePath);
            ArgumentException.ThrowIfNullOrWhiteSpace(launchAgentsDirectory);
            this.executablePath = executablePath;
            plistPath = Path.Combine(launchAgentsDirectory, $"{AgentLabel}.plist");
        }

        /// <summary>
        /// Gets whether auto-start is supported on this platform.
        /// </summary>
        public bool IsSupported => true;

        /// <summary>
        /// Gets whether the LaunchAgent plist exists.
        /// </summary>
        public bool IsEnabled => File.Exists(plistPath);

        /// <summary>
        /// Writes the LaunchAgent plist for the current executable.
        /// </summary>
        public void Enable()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(plistPath)!);
            File.WriteAllText(plistPath, BuildPlist());
        }

        /// <summary>
        /// Removes the LaunchAgent plist when present.
        /// </summary>
        public void Disable()
        {
            if (File.Exists(plistPath))
            {
                File.Delete(plistPath);
            }
        }

        private string BuildPlist()
        {
            string encodedPath = System.Security.SecurityElement.Escape(executablePath);
            return $"""
                <?xml version="1.0" encoding="UTF-8"?>
                <!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
                <plist version="1.0">
                <dict>
                  <key>Label</key>
                  <string>{AgentLabel}</string>
                  <key>ProgramArguments</key>
                  <array>
                    <string>{encodedPath}</string>
                  </array>
                  <key>RunAtLoad</key>
                  <true/>
                </dict>
                </plist>
                """;
        }
    }
}
