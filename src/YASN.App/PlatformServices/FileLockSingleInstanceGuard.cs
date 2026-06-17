namespace YASN.PlatformServices
{
    /// <summary>
    /// Guards a single instance on non-Windows platforms using an exclusively locked file.
    /// </summary>
    public sealed class FileLockSingleInstanceGuard : ISingleInstanceGuard
    {
        private FileStream? lockStream;

        /// <summary>
        /// Acquires an exclusive lock on the supplied path.
        /// </summary>
        /// <param name="lockPath">The lock file path.</param>
        public FileLockSingleInstanceGuard(string lockPath)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(lockPath);
            Directory.CreateDirectory(Path.GetDirectoryName(lockPath)!);

            try
            {
                lockStream = new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
                HasPrimaryInstance = true;
            }
            catch (IOException)
            {
                HasPrimaryInstance = false;
            }
        }

        /// <summary>
        /// Gets whether this process owns the primary instance slot.
        /// </summary>
        public bool HasPrimaryInstance { get; }

        /// <summary>
        /// Releases the exclusive file lock.
        /// </summary>
        public void Dispose()
        {
            lockStream?.Dispose();
            lockStream = null;
        }
    }
}
