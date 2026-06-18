namespace YASN.Infrastructure.Sync
{
    /// <summary>
    /// The direction of a planned destructive change in a <see cref="SyncChangePlan"/>.
    /// </summary>
    public enum SyncDeleteSide
    {
        /// <summary>The local note will be deleted (the remote dropped it).</summary>
        Local,

        /// <summary>The remote note will be deleted (the local side dropped it).</summary>
        Remote
    }

    /// <summary>
    /// One planned deletion surfaced to the user before a pass applies it.
    /// </summary>
    /// <param name="SyncKey">The note's sync key.</param>
    /// <param name="Title">A human-readable note title for display, when known.</param>
    /// <param name="Side">Which replica loses the note.</param>
    public sealed record SyncChangeItem(string SyncKey, string Title, SyncDeleteSide Side);

    /// <summary>
    /// The set of deletions a sync pass intends to perform, computed before any IO so the user can
    /// confirm a bulk delete. Non-destructive actions (uploads, downloads, conflicts) are not listed
    /// because they never lose data.
    /// </summary>
    public sealed class SyncChangePlan
    {
        /// <summary>
        /// Gets the planned deletions.
        /// </summary>
        public IReadOnlyList<SyncChangeItem> Deletions { get; init; } = Array.Empty<SyncChangeItem>();

        /// <summary>
        /// Gets the number of notes that would be deleted locally.
        /// </summary>
        public int LocalDeleteCount => Deletions.Count(d => d.Side == SyncDeleteSide.Local);

        /// <summary>
        /// Gets the number of notes that would be deleted remotely.
        /// </summary>
        public int RemoteDeleteCount => Deletions.Count(d => d.Side == SyncDeleteSide.Remote);
    }
}
