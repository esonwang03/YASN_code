using YASN.Notifications;

namespace YASN.PlatformServices
{
    /// <summary>
    /// Selects platform services for the current operating system.
    /// </summary>
    public static class PlatformServiceFactory
    {
        /// <summary>
        /// Creates the platform service bundle.
        /// </summary>
        /// <returns>The service bundle for the current platform.</returns>
        public static PlatformServiceBundle Create()
        {
#pragma warning disable CA2000
            ISingleInstanceGuard singleInstance = SingleInstanceGuardFactory.Create();
            IGlobalHotkeyService globalHotkeys = GlobalHotkeyServiceFactory.Create();
#pragma warning restore CA2000

            try
            {
                return new PlatformServiceBundle(
                    AutoStartServiceFactory.Create(),
                    singleInstance,
                    new SystemNotificationService(NativeNotificationSenderFactory.Create()),
                    WindowLevelControllerFactory.Create(),
                    new AvaloniaQuickWindowLayoutController(),
                    globalHotkeys);
            }
            catch
            {
                globalHotkeys.Dispose();
                singleInstance.Dispose();
                throw;
            }
        }
    }
}
