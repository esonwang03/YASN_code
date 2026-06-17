using YASN.AvaloniaNotes;

namespace YASN.Infrastructure.Sync
{
    /// <summary>
    /// Remote IO helpers for <see cref="ThreeWaySyncEngine"/>: transfer wire documents over the
    /// <see cref="ISyncClient"/> using temp files and keep the baseline in step.
    /// </summary>
    public sealed partial class ThreeWaySyncEngine
    {
        private async Task<bool> UploadAsync(ISyncClient client, string syncKey, AvaloniaNoteDocument local, CancellationToken cancellationToken)
        {
            SyncNoteDocument wire = NoteSyncMapper.ToWire(local, clock());
            string? etag = await PutWireAsync(client, syncKey, wire, cancellationToken).ConfigureAwait(false);
            state.UpsertBaseline(new SyncBaseline(syncKey, NoteWireSerializer.ComputeContentHash(wire), etag, RemoteNotePath(syncKey), clock(), false));
            state.Dequeue(syncKey);
            return true;
        }

        private async Task<bool> DownloadAsync(ISyncClient client, string syncKey, AvaloniaNoteDocument? local, CancellationToken cancellationToken)
        {
            (SyncNoteDocument? wire, string? etag) = await GetWireAsync(client, syncKey, cancellationToken).ConfigureAwait(false);
            if (wire is null)
            {
                return false;
            }

            return ApplyRemote(syncKey, wire, etag, local);
        }

        private bool ApplyRemote(string syncKey, SyncNoteDocument wire, string? etag, AvaloniaNoteDocument? local)
        {
            if (wire.Deleted)
            {
                if (local is not null)
                {
                    repository.Delete(local.Id);
                }

                state.UpsertBaseline(new SyncBaseline(syncKey, null, etag, RemoteNotePath(syncKey), clock(), true));
                state.Dequeue(syncKey);
                return true;
            }

            AvaloniaNoteDocument document = NoteSyncMapper.ToDocument(wire);
            if (local is not null)
            {
                document.Id = local.Id;
                document.IsOpen = local.IsOpen;
            }
            else
            {
                document = repository.CreateConflictCopy(document);
            }

            repository.Save(document);
            state.UpsertBaseline(new SyncBaseline(syncKey, NoteWireSerializer.ComputeContentHash(wire), etag, RemoteNotePath(syncKey), clock(), false));
            state.Dequeue(syncKey);
            return true;
        }

        private async Task<bool> DeleteRemoteAsync(ISyncClient client, string syncKey, CancellationToken cancellationToken)
        {
            SyncNoteDocument tombstone = NoteSyncMapper.Tombstone(syncKey, clock());
            string? etag = await PutWireAsync(client, syncKey, tombstone, cancellationToken).ConfigureAwait(false);
            state.UpsertBaseline(new SyncBaseline(syncKey, null, etag, RemoteNotePath(syncKey), clock(), true));
            state.Dequeue(syncKey);
            return true;
        }

        private async Task<bool> ResolveDivergenceAsync(ISyncClient client, string syncKey, AvaloniaNoteDocument? local, string? localHash, CancellationToken cancellationToken)
        {
            (SyncNoteDocument? remoteWire, string? etag) = await GetWireAsync(client, syncKey, cancellationToken).ConfigureAwait(false);

            // Delete-vs-edit: the side that still has content wins (resurrect), no stuck conflict.
            if (local is not null && (remoteWire is null || remoteWire.Deleted))
            {
                return await UploadAsync(client, syncKey, local, cancellationToken).ConfigureAwait(false);
            }

            if (local is null && remoteWire is not null && !remoteWire.Deleted)
            {
                return ApplyRemote(syncKey, remoteWire, etag, local: null);
            }

            if (local is null || remoteWire is null)
            {
                return false;
            }

            // Both changed: converge silently when content is identical, else raise a real conflict.
            string remoteHash = NoteWireSerializer.ComputeContentHash(remoteWire);
            if (remoteHash == localHash)
            {
                state.UpsertBaseline(new SyncBaseline(syncKey, localHash, etag, RemoteNotePath(syncKey), clock(), false));
                state.Dequeue(syncKey);
                return false;
            }

            // True conflict: keep local untouched, materialize the remote side as a second row.
            AvaloniaNoteDocument copy = NoteSyncMapper.ToDocument(remoteWire);
            repository.CreateConflictCopy(copy);
            state.MarkConflict(syncKey, "local and remote diverged");
            state.Dequeue(syncKey);
            return true;
        }

        private async Task<string?> PutWireAsync(ISyncClient client, string syncKey, SyncNoteDocument wire, CancellationToken cancellationToken)
        {
            string remotePath = RemoteNotePath(syncKey);
            string tempPath = Path.Combine(Path.GetTempPath(), $"yasn-sync-{Guid.NewGuid():N}.json");
            try
            {
                await File.WriteAllBytesAsync(tempPath, NoteWireSerializer.Serialize(wire), cancellationToken).ConfigureAwait(false);
                await client.UploadFileAsync(tempPath, remotePath).ConfigureAwait(false);
            }
            finally
            {
                TryDeleteTemp(tempPath);
            }

            return await client.GetFileETagAsync(remotePath).ConfigureAwait(false);
        }

        private async Task<(SyncNoteDocument? Wire, string? ETag)> GetWireAsync(ISyncClient client, string syncKey, CancellationToken cancellationToken)
        {
            string remotePath = RemoteNotePath(syncKey);
            string tempPath = Path.Combine(Path.GetTempPath(), $"yasn-sync-{Guid.NewGuid():N}.json");
            try
            {
                if (!await client.DownloadFileAsync(remotePath, tempPath).ConfigureAwait(false) || !File.Exists(tempPath))
                {
                    return (null, null);
                }

                byte[] bytes = await File.ReadAllBytesAsync(tempPath, cancellationToken).ConfigureAwait(false);
                SyncNoteDocument? wire = NoteWireSerializer.Deserialize(bytes);
                string? etag = await client.GetFileETagAsync(remotePath).ConfigureAwait(false);
                return (wire, etag);
            }
            finally
            {
                TryDeleteTemp(tempPath);
            }
        }

        private static void TryDeleteTemp(string path)
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch (IOException)
            {
                // Best-effort cleanup of a temp file; ignore.
            }
        }
    }
}
