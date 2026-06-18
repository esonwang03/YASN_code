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

        private async Task<bool> DownloadAsync(ISyncClient client, string syncKey, AvaloniaNoteDocument? local, string? plannedHash, ApplyContext context, CancellationToken cancellationToken)
        {
            // Reuse the document prefetched for deletion-gating when available, else fetch now.
            RemoteFetch fetch = context.PrefetchedByKey.TryGetValue(syncKey, out RemoteFetch? prefetched)
                ? prefetched
                : await FetchRemoteAsync(client, syncKey, cancellationToken).ConfigureAwait(false);

            if (fetch.Wire is null)
            {
                return false;
            }

            // When a local note exists this download either overwrites it (remote edit) or deletes it
            // (remote tombstone). Both lose a local edit the user may have made while the confirm dialog
            // was open, so re-validate against the post-dialog snapshot before applying.
            if (local is not null && !LiveStateMatchesPlan(syncKey, local, plannedHash, context))
            {
                return false;
            }

            return ApplyRemote(syncKey, fetch.Wire, fetch.ETag, local);
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

        private async Task<bool> DeleteRemoteAsync(ISyncClient client, string syncKey, AvaloniaNoteDocument? plannedLocal, string? plannedHash, ApplyContext context, CancellationToken cancellationToken)
        {
            // The plan assumed this note was deleted locally. If the user recreated/edited a note for
            // this key while the confirm dialog was open, do not propagate the (now stale) deletion —
            // the next pass re-evaluates and uploads the resurrected note instead.
            if (!LiveStateMatchesPlan(syncKey, plannedLocal, plannedHash, context))
            {
                return false;
            }

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

            return await FetchValidatorAsync(client, remotePath).ConfigureAwait(false);
        }

        /// <summary>
        /// Reads the change-detection validator for a remote file just transferred, from the source the
        /// active <see cref="ChangeDetection"/> mode selects (ETag or Last-Modified). Coalesced to the
        /// "present" sentinel when the server supplies neither, matching the listing path so a baseline
        /// written here compares equal to the one a later listing produces.
        /// </summary>
        private async Task<string?> FetchValidatorAsync(ISyncClient client, string remotePath)
        {
            if (ChangeDetection == ChangeDetectionMode.LastModified)
            {
                DateTime? modified = await client.GetFileLastModifiedAsync(remotePath).ConfigureAwait(false);
                return ValidatorFor(null, modified);
            }

            return ValidatorFor(await client.GetFileETagAsync(remotePath).ConfigureAwait(false), null);
        }

        private async Task<RemoteFetch> FetchRemoteAsync(ISyncClient client, string syncKey, CancellationToken cancellationToken)
        {
            (SyncNoteDocument? wire, string? etag) = await GetWireAsync(client, syncKey, cancellationToken).ConfigureAwait(false);
            return new RemoteFetch(wire, etag);
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
                string? etag = await FetchValidatorAsync(client, remotePath).ConfigureAwait(false);
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
