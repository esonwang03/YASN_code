using YASN.Infrastructure;

namespace YASN.PlatformServices
{
    /// <summary>
    /// Selects the single-instance guard for the current operating system.
    /// </summary>
    public static class SingleInstanceGuardFactory
    {
        private const string MutexName = "YASN.Avalonia.SingleInstance";

        /// <summary>
        /// Creates the single-instance guard for the current platform.
        /// </summary>
        /// <returns>A named-mutex guard on Windows, or a file-lock guard otherwise.</returns>
        public static ISingleInstanceGuard Create()
        {
            if (OperatingSystem.IsWindows())
            {
                return new MutexSingleInstanceGuard(MutexName);
            }

            return new FileLockSingleInstanceGuard(AppPaths.SingleInstanceLockPath);
        }
    }
}
