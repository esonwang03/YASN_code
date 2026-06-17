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
            WindowsAutoStartService service = new WindowsAutoStartService(executablePath);

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

            WindowsAutoStartService writer = new WindowsAutoStartService(originalPath);
            WindowsAutoStartService reader = new WindowsAutoStartService(currentPath);

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
