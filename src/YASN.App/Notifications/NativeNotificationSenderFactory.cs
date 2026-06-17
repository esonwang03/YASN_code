namespace YASN.Notifications
{
    /// <summary>
    /// Selects the native notification sender for the current operating system.
    /// </summary>
    public static class NativeNotificationSenderFactory
    {
        /// <summary>
        /// Creates the native notification sender for the current platform.
        /// </summary>
        /// <returns>A platform sender, or an unsupported sender when no native channel exists.</returns>
        public static INativeNotificationSender Create()
        {
            if (OperatingSystem.IsWindows() || OperatingSystem.IsMacOS() || OperatingSystem.IsLinux())
            {
                return new OsNotificationSender();
            }

            return new UnsupportedNativeNotificationSender();
        }
    }
}
