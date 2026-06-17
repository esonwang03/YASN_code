using YASN.PlatformServices;

namespace YASN.Migration.Tests
{
    /// <summary>
    /// Verifies platform service selection exposes capabilities honestly.
    /// </summary>
    public sealed class PlatformServiceTests
    {
        /// <summary>
        /// Creates the platform service bundle for the current operating system.
        /// </summary>
        [Fact]
        public void FactoryCreatesNonNullServices()
        {
            using PlatformServiceBundle services = PlatformServiceFactory.Create();

            Assert.NotNull(services.AutoStart);
            Assert.NotNull(services.SingleInstance);
            Assert.NotNull(services.Notifications);
            Assert.NotNull(services.WindowLevels);
            Assert.NotNull(services.QuickLayout);
        }
    }
}
