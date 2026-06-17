namespace YASN.PlatformServices
{
    /// <summary>
    /// Guards the application against multiple active instances.
    /// </summary>
    public interface ISingleInstanceGuard : IDisposable
    {
        /// <summary>
        /// Gets whether this process owns the primary instance slot.
        /// </summary>
        bool HasPrimaryInstance { get; }
    }
}
