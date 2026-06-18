using YASN.AvaloniaNotes;

namespace YASN.Infrastructure.Sync
{
    /// <summary>
    /// Per-key three-way processing for <see cref="ThreeWaySyncEngine"/>: turns a decision into the
    /// concrete upload/download/conflict/delete IO and baseline updates.
    /// </summary>
    public sealed partial class ThreeWaySyncEngine
    {
        /// <summary>
        /// One key resolved to its local/remote state, baseline, content hash, and decided action,
        /// computed once during the pure prescan so it is not recomputed when applying the change.
        /// </summary>
        private sealed record PlannedKey(
            string SyncKey,
            AvaloniaNoteDocument? Local,
            RemoteEntry? Remote,
            string? LocalHash,
            SyncAction Action);

        /// <summary>
        /// A remote note document already fetched during the pre-confirmation prescan, cached so the
        /// apply step does not download it a second time. <see cref="Wire"/> is null when the fetch
        /// failed or the remote file vanished.
        /// </summary>
        private sealed record RemoteFetch(SyncNoteDocument? Wire, string? ETag);

        /// <summary>
        /// State shared across one pass's apply loop: a single snapshot of the live notes taken after
        /// the confirm dialog returned (so destructive actions can be re-validated against edits made
        /// while the dialog was open, without re-reading the repository per key), and any remote
        /// documents prefetched for deletion-gating (reused instead of re-downloaded).
        /// </summary>
        private sealed record ApplyContext(
            IReadOnlyDictionary<string, AvaloniaNoteDocument> LiveById,
            IReadOnlyDictionary<string, RemoteFetch> PrefetchedByKey);

        /// <summary>
        /// Resolves each eligible key to a <see cref="PlannedKey"/> using the pure
        /// <see cref="SyncDecider"/>, skipping conflicted keys and freshly duplicated pairs. No IO.
        /// </summary>
        private List<PlannedKey> BuildWorkItems(
            HashSet<string> keys,
            HashSet<string> conflicted,
            Dictionary<string, List<AvaloniaNoteDocument>> localByKey,
            Dictionary<string, RemoteEntry> remoteByKey)
        {
            List<PlannedKey> work = new List<PlannedKey>();
            foreach (string key in keys)
            {
                if (conflicted.Contains(key))
                {
                    continue;
                }

                // A key with two local rows but no recorded conflict is a freshly duplicated pair; skip.
                localByKey.TryGetValue(key, out List<AvaloniaNoteDocument>? rows);
                if (rows is { Count: > 1 })
                {
                    continue;
                }

                AvaloniaNoteDocument? local = rows?.FirstOrDefault();
                RemoteEntry? remote = remoteByKey.GetValueOrDefault(key);
                SyncBaseline? baseline = state.GetBaseline(key);
                string? localHash = local is null
                    ? null
                    : NoteWireSerializer.ComputeContentHash(NoteSyncMapper.ToWire(local, clock()));

                // Entries in remoteByKey were already ETag-normalized in RunPassAsync, so a present file
                // always carries a non-null ETag (the "present" sentinel when the server omits one).
                string? remoteETag = remote?.ETag;

                SyncAction action = SyncDecider.Decide(localHash, remoteETag, baseline);
                work.Add(new PlannedKey(key, local, remote, localHash, action));
            }

            return work;
        }

        private async Task<bool> ProcessKeyAsync(ISyncClient client, PlannedKey item, ApplyContext context, CancellationToken cancellationToken)
        {
            string syncKey = item.SyncKey;
            AvaloniaNoteDocument? local = item.Local;

            switch (item.Action)
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
                    return await DownloadAsync(client, syncKey, local, item.LocalHash, context, cancellationToken).ConfigureAwait(false);

                case SyncAction.DeleteLocal:
                    return DeleteLocal(syncKey, local, item.LocalHash, context);

                case SyncAction.DeleteRemote:
                    return await DeleteRemoteAsync(client, syncKey, local, item.LocalHash, context, cancellationToken).ConfigureAwait(false);

                case SyncAction.CompareForConflict:
                    return await ResolveDivergenceAsync(client, syncKey, local, item.LocalHash, cancellationToken).ConfigureAwait(false);

                default:
                    return false;
            }
        }

        /// <summary>
        /// Confirms a note the pass intends to destroy or overwrite has not changed since the plan was
        /// built. The plan is computed before <c>ConfirmDeletionsAsync</c> awaits the (possibly
        /// long-lived) confirm dialog, during which the user may edit a note on the UI thread. Applying
        /// the stale decision would silently lose that edit. Returns true when the live note still
        /// matches <paramref name="plannedHash"/> (safe to apply); false when it changed or was
        /// recreated (skip; the next pass re-evaluates with the new state).
        /// </summary>
        private bool LiveStateMatchesPlan(string syncKey, AvaloniaNoteDocument? plannedLocal, string? plannedHash, ApplyContext context)
        {
            // The decision assumed a specific local state (a note with plannedHash, or — when plannedHash
            // is null — no local note at all). Compare against the post-dialog snapshot.
            context.LiveById.TryGetValue(plannedLocal?.Id ?? string.Empty, out AvaloniaNoteDocument? liveById);

            if (plannedLocal is null)
            {
                // Plan assumed no local note for this key. If one exists now (recreated during the
                // dialog), do not propagate the deletion.
                bool recreated = context.LiveById.Values.Any(n => string.Equals(n.SyncKey, syncKey, StringComparison.Ordinal));
                if (recreated)
                {
                    Logging.AppLogger.Warn($"Sync skipped destructive action for key '{syncKey}': a note was created after the pass was planned; re-evaluating next pass.");
                    return false;
                }

                return true;
            }

            if (liveById is null)
            {
                // The planned note is already gone locally; nothing left to destroy/overwrite.
                return false;
            }

            string liveHash = NoteWireSerializer.ComputeContentHash(NoteSyncMapper.ToWire(liveById, clock()));
            if (liveHash != plannedHash)
            {
                Logging.AppLogger.Warn($"Sync skipped destructive action for '{liveById.Title}' (key {syncKey}): it changed after the pass was planned; re-evaluating next pass.");
                return false;
            }

            return true;
        }

        private bool DeleteLocal(string syncKey, AvaloniaNoteDocument? local, string? plannedHash, ApplyContext context)
        {
            if (local is not null)
            {
                if (!LiveStateMatchesPlan(syncKey, local, plannedHash, context))
                {
                    // Keep the note and its baseline; a local edit during the dialog means the next pass
                    // sees local-changed + remote-gone and uploads the note back instead of losing it.
                    return false;
                }

                Logging.AppLogger.Info($"Sync deleting local note '{local.Title}' (key {syncKey}): remote dropped it.");
                repository.Delete(local.Id);
            }

            state.DeleteBaseline(syncKey);
            state.Dequeue(syncKey);
            return true;
        }
    }
}
