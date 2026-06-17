using YASN.AvaloniaNotes;

namespace YASN.Infrastructure.Sync
{
    /// <summary>
    /// Per-key three-way processing for <see cref="ThreeWaySyncEngine"/>: turns a decision into the
    /// concrete upload/download/conflict/delete IO and baseline updates.
    /// </summary>
    public sealed partial class ThreeWaySyncEngine
    {
        private async Task<bool> ProcessKeyAsync(
            ISyncClient client,
            string syncKey,
            AvaloniaNoteDocument? local,
            RemoteEntry? remote,
            CancellationToken cancellationToken)
        {
            SyncBaseline? baseline = state.GetBaseline(syncKey);
            string? localHash = local is null ? null : NoteWireSerializer.ComputeContentHash(NoteSyncMapper.ToWire(local, clock()));
            string? remoteETag = remote?.ETag ?? (remote is not null ? "present" : null);

            SyncAction action = SyncDecider.Decide(localHash, remoteETag, baseline);
            switch (action)
            {
                case SyncAction.None:
                    return false;

                case SyncAction.DropBaseline:
                    state.DeleteBaseline(syncKey);
                    state.Dequeue(syncKey);
                    return false;

                case SyncAction.Upload:
                    return await UploadAsync(client, syncKey, local!, cancellationToken).ConfigureAwait(false);

                case SyncAction.Download:
                    return await DownloadAsync(client, syncKey, local, cancellationToken).ConfigureAwait(false);

                case SyncAction.DeleteLocal:
                    return DeleteLocal(syncKey, local);

                case SyncAction.DeleteRemote:
                    return await DeleteRemoteAsync(client, syncKey, cancellationToken).ConfigureAwait(false);

                case SyncAction.CompareForConflict:
                    return await ResolveDivergenceAsync(client, syncKey, local, localHash, cancellationToken).ConfigureAwait(false);

                default:
                    return false;
            }
        }

        private bool DeleteLocal(string syncKey, AvaloniaNoteDocument? local)
        {
            if (local is not null)
            {
                repository.Delete(local.Id);
            }

            state.DeleteBaseline(syncKey);
            state.Dequeue(syncKey);
            return true;
        }
    }
}
