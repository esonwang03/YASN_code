namespace YASN.Infrastructure.Sync
{
    /// <summary>
    /// One baseline row: the last common state of a note shared by this device and the remote, used
    /// as the third point in three-way comparison.
    /// </summary>
    /// <param name="SyncKey">The note's stable sync key.</param>
    /// <param name="LocalHash">The content hash at last sync, or null if unknown.</param>
    /// <param name="RemoteETag">The remote ETag at last sync, or null if unknown.</param>
    /// <param name="RemotePath">The remote relative path for the note document.</param>
    /// <param name="LastSyncUtc">When the baseline was last updated (UTC).</param>
    /// <param name="Deleted">Whether the baseline records a tombstone.</param>
    public sealed record SyncBaseline(
        string SyncKey,
        string? LocalHash,
        string? RemoteETag,
        string RemotePath,
        DateTimeOffset LastSyncUtc,
        bool Deleted);

    /// <summary>
    /// A pending local change awaiting upload, coalesced by sync key.
    /// </summary>
    /// <param name="SyncKey">The changed note's sync key.</param>
    /// <param name="Operation">Either <c>upsert</c> or <c>delete</c>.</param>
    public sealed record SyncQueueItem(string SyncKey, string Operation);
}
