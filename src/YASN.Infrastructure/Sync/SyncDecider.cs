namespace YASN.Infrastructure.Sync
{
    /// <summary>
    /// The action the engine should take for one sync key after three-way comparison.
    /// </summary>
    public enum SyncAction
    {
        /// <summary>Nothing changed; do nothing.</summary>
        None,

        /// <summary>Push the local note to the remote.</summary>
        Upload,

        /// <summary>Pull the remote note to local.</summary>
        Download,

        /// <summary>Both sides diverged; engine must compare content to confirm a true conflict.</summary>
        CompareForConflict,

        /// <summary>Remote was deleted while local was unchanged; delete locally.</summary>
        DeleteLocal,

        /// <summary>Local was deleted while remote was unchanged; delete remotely.</summary>
        DeleteRemote,

        /// <summary>Both sides are gone; drop the stale baseline.</summary>
        DropBaseline
    }

    /// <summary>
    /// Pure three-way decision logic. Compares local state, remote state, and the last-common
    /// baseline to choose a <see cref="SyncAction"/> with no IO. The one case it cannot settle alone
    /// is a both-sides-changed divergence, where content equality must be checked by the engine
    /// (returns <see cref="SyncAction.CompareForConflict"/>).
    /// </summary>
    public static class SyncDecider
    {
        /// <summary>
        /// Decides the action for one sync key.
        /// </summary>
        /// <param name="localHash">Local content hash, or null when the note does not exist locally.</param>
        /// <param name="remoteETag">Remote ETag, or null when the note does not exist remotely.</param>
        /// <param name="baseline">The last-common baseline, or null when never synced.</param>
        /// <returns>The action to take.</returns>
        public static SyncAction Decide(string? localHash, string? remoteETag, SyncBaseline? baseline)
        {
            bool localExists = localHash is not null;
            bool remoteExists = remoteETag is not null;

            if (!localExists && !remoteExists)
            {
                return baseline is not null ? SyncAction.DropBaseline : SyncAction.None;
            }

            if (localExists && !remoteExists)
            {
                if (baseline is null)
                {
                    return SyncAction.Upload;
                }

                bool localUnchanged = baseline.LocalHash == localHash;
                return localUnchanged ? SyncAction.DeleteLocal : SyncAction.CompareForConflict;
            }

            if (!localExists && remoteExists)
            {
                if (baseline is null)
                {
                    return SyncAction.Download;
                }

                bool remoteUnchanged = baseline.RemoteETag == remoteETag;
                return remoteUnchanged ? SyncAction.DeleteRemote : SyncAction.CompareForConflict;
            }

            // Both exist.
            bool localChanged = baseline is null || baseline.LocalHash != localHash;
            bool remoteChanged = baseline is null || baseline.RemoteETag != remoteETag;

            if (!localChanged && !remoteChanged)
            {
                return SyncAction.None;
            }

            if (localChanged && !remoteChanged)
            {
                return SyncAction.Upload;
            }

            if (!localChanged)
            {
                return SyncAction.Download;
            }

            return SyncAction.CompareForConflict;
        }
    }
}
