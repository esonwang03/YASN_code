namespace YASN.PlatformServices
{
    /// <summary>
    /// Selects the window-level controller for the current operating system.
    /// </summary>
    public static class WindowLevelControllerFactory
    {
        /// <summary>
        /// Creates the window-level controller for the current platform.
        /// </summary>
        /// <returns>A Windows controller with bottom-most support, or the cross-platform controller otherwise.</returns>
        public static IWindowLevelController Create()
        {
            if (OperatingSystem.IsWindows())
            {
                return new WindowsWindowLevelController();
            }

            return new AvaloniaWindowLevelController();
        }
    }
}
