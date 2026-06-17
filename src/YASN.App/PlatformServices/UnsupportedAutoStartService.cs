namespace YASN.PlatformServices
{
    /// <summary>
    /// Auto-start service for platforms without a launcher integration.
    /// </summary>
    public sealed class UnsupportedAutoStartService : IAutoStartService
    {
        /// <summary>
        /// Gets whether auto-start is supported on this platform.
        /// </summary>
        public bool IsSupported => false;

        /// <summary>
        /// Always reports disabled because no backend exists.
        /// </summary>
        public bool IsEnabled => false;

        /// <summary>
        /// No-op because auto-start is unsupported.
        /// </summary>
        public void Enable()
        {
        }

        /// <summary>
        /// No-op because auto-start is unsupported.
        /// </summary>
        public void Disable()
        {
        }
    }
}
