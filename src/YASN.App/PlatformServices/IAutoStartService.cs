namespace YASN.PlatformServices
{
    /// <summary>
    /// Controls whether the application launches automatically when the user signs in.
    /// </summary>
    public interface IAutoStartService
    {
        /// <summary>
        /// Gets whether the current platform has an auto-start implementation.
        /// </summary>
        bool IsSupported { get; }

        /// <summary>
        /// Gets whether auto-start is currently enabled.
        /// </summary>
        bool IsEnabled { get; }

        /// <summary>
        /// Enables auto-start for the current user. No-op when unsupported.
        /// </summary>
        void Enable();

        /// <summary>
        /// Disables auto-start for the current user. No-op when unsupported.
        /// </summary>
        void Disable();
    }
}
