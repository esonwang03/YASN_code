using YASN.Notifications;

namespace YASN.PlatformServices
{
    /// <summary>
    /// Groups platform-specific services selected at startup.
    /// </summary>
    public sealed record PlatformServiceBundle(
            IAutoStartService AutoStart,
            ISingleInstanceGuard SingleInstance,
            INotificationService Notifications,
            IWindowLevelController WindowLevels,
            IQuickWindowLayoutController QuickLayout,
            IGlobalHotkeyService GlobalHotkeys) : IDisposable
    {
        /// <summary>
        /// Releases platform service resources.
        /// </summary>
        public void Dispose()
        {
            GlobalHotkeys.Dispose();
            SingleInstance.Dispose();
        }
    }
}
