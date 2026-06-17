namespace YASN.PlatformServices
{
    /// <summary>
    /// Uses a named mutex to guard a desktop application instance.
    /// </summary>
    public sealed class MutexSingleInstanceGuard : ISingleInstanceGuard
    {
        private readonly Mutex mutex;

        /// <summary>
        /// Initializes a named mutex single-instance guard.
        /// </summary>
        /// <param name="name">The mutex name.</param>
        public MutexSingleInstanceGuard(string name)
        {
            mutex = new Mutex(initiallyOwned: true, name, out bool createdNew);
            HasPrimaryInstance = createdNew;
        }

        /// <summary>
        /// Gets whether this process owns the primary instance slot.
        /// </summary>
        public bool HasPrimaryInstance { get; }

        /// <summary>
        /// Releases the mutex.
        /// </summary>
        public void Dispose()
        {
            mutex.Dispose();
        }
    }
}
