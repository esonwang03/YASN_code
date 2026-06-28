using YASN.PlatformServices;

namespace YASN.Migration.Tests
{
    /// <summary>
    /// Verifies the Windows auto-start service round-trips the per-user Run key.
    /// </summary>
    public sealed class AutoStartServiceTests
    {
        /// <summary>
        /// Enables and disables auto-start through the registry on Windows.
        /// </summary>
        [Fact]
        public void WindowsServiceRoundTripsRunKey()
        {
            if (!OperatingSystem.IsWindows())
            {
                return;
            }

            string executablePath = Path.Combine(Path.GetTempPath(), $"yasn-autostart-{Guid.NewGuid():N}.exe");
            // Isolated Run-key value name so the test never reads or deletes the real install's "YASN"
            // auto-start entry.
            WindowsAutoStartService service = new WindowsAutoStartService(executablePath, $"YASN__test_{Guid.NewGuid():N}");

            try
            {
                Assert.True(service.IsSupported);
                Assert.False(service.IsEnabled);

                service.Enable();
                Assert.True(service.IsEnabled);
            }
            finally
            {
                service.Disable();
            }

            Assert.False(service.IsEnabled);
        }

        /// <summary>
        /// An existing Run entry written by a different executable path (a prior install or a dev vs
        /// published build) still reads as enabled, so the toggle survives the path changing.
        /// </summary>
        [Fact]
        public void WindowsServiceReportsEnabledWhenPathDiffers()
        {
            if (!OperatingSystem.IsWindows())
            {
                return;
            }

            string originalPath = Path.Combine(Path.GetTempPath(), $"yasn-old-{Guid.NewGuid():N}.exe");
            string currentPath = Path.Combine(Path.GetTempPath(), $"yasn-new-{Guid.NewGuid():N}.exe");

            // Both services share one isolated value name (distinct from the real "YASN" entry) so the
            // reader observes what the writer set without touching the developer's actual auto-start.
            string valueName = $"YASN__test_{Guid.NewGuid():N}";
            WindowsAutoStartService writer = new WindowsAutoStartService(originalPath, valueName);
            WindowsAutoStartService reader = new WindowsAutoStartService(currentPath, valueName);

            try
            {
                writer.Enable();
                Assert.True(reader.IsEnabled);
            }
            finally
            {
                writer.Disable();
            }
        }

        /// <summary>
        /// Confirms the unsupported service never claims support.
        /// </summary>
        [Fact]
        public void UnsupportedServiceReportsNoSupport()
        {
            UnsupportedAutoStartService service = new UnsupportedAutoStartService();

            Assert.False(service.IsSupported);
            Assert.False(service.IsEnabled);
            service.Enable();
            Assert.False(service.IsEnabled);
        }

        /// <summary>
        /// Writes and removes the macOS LaunchAgent plist.
        /// </summary>
        [Fact]
        public void MacServiceRoundTripsPlist()
        {
            if (!OperatingSystem.IsMacOS())
            {
                return;
            }

            string agentsDir = Path.Combine(Path.GetTempPath(), $"yasn-agents-{Guid.NewGuid():N}");
            MacOsAutoStartService service = new MacOsAutoStartService("/Applications/YASN.app", agentsDir);

            try
            {
                service.Enable();
                Assert.True(service.IsEnabled);

                service.Disable();
                Assert.False(service.IsEnabled);
            }
            finally
            {
                if (Directory.Exists(agentsDir))
                {
                    Directory.Delete(agentsDir, recursive: true);
                }
            }
        }
    }
}
