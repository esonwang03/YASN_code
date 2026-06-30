using YASN.AvaloniaNotes;

namespace YASN.Infrastructure.Sync
{
    /// <summary>
    /// Three-way note sync: compares local last-write state, remote WebDAV ETags, and a SQLite
    /// baseline to converge two replicas. Diverged notes are duplicated locally (two rows sharing one
    /// sync key) and excluded from sync until the user resolves the conflict.
    /// </summary>
    public sealed partial class ThreeWaySyncEngine : IDisposable
    {
        private const string RemoteNotesDir = "notes";

        private readonly NoteRepository repository;
        private readonly SyncStateStore state;
        private readonly Func<DateTimeOffset> clock;
        private readonly SemaphoreSlim passGate = new(1, 1);
        private readonly Lock timerGate = new();
        private System.Threading.Timer? timer;
        private Func<ISyncClient?>? clientFactory;
        private string remoteRoot = string.Empty;
        private bool disposed;

        /// <summary>
        /// Initializes the engine over a repository and sync-state store.
        /// </summary>
        /// <param name="repository">The local note repository.</param>
        /// <param name="state">The SQLite baseline/queue/conflict store.</param>
        /// <param name="clock">A UTC clock, injected for tests.</param>
        public ThreeWaySyncEngine(NoteRepository repository, SyncStateStore state, Func<DateTimeOffset>? clock = null)
        {
            this.repository = repository;
            this.state = state;
            this.clock = clock ?? (() => DateTimeOffset.UtcNow);
        }

        /// <summary>
        /// Raised after a pass changes local state, so the UI can refresh.
        /// </summary>
        public event EventHandler? SyncCompleted;

        /// <summary>
        /// Gets or sets an optional gate invoked before a pass applies two or more deletions. It
        /// receives the planned deletions and returns whether to proceed; returning
        /// <see langword="false"/> aborts the pass without touching any note. When null, passes
        /// proceed unconditionally (the default for headless/test use). Single deletions are never
        /// gated, since they are the normal outcome of deleting one note on another device.
        /// </summary>
        public Func<SyncChangePlan, Task<bool>>? ConfirmBulkChanges { get; set; }

        /// <summary>
        /// Gets or sets the deletion count at or above which <see cref="ConfirmBulkChanges"/> is
        /// invoked. Defaults to 2 so a routine single-note deletion syncs silently.
        /// </summary>
        public int BulkDeleteThreshold { get; set; } = 2;

        /// <summary>
        /// Gets or sets how remote changes are detected. Defaults to <see cref="ChangeDetectionMode.ETag"/>;
        /// set to <see cref="ChangeDetectionMode.LastModified"/> for servers that omit ETags. The
        /// validator token stored in the baseline is derived from this source, so changing it makes the
        /// next pass re-evaluate every note once (a safe re-download, never a delete).
        /// </summary>
        public ChangeDetectionMode ChangeDetection { get; set; } = ChangeDetectionMode.ETag;

        /// <summary>
        /// Gets the sync keys currently in conflict (excluded from sync until resolved).
        /// </summary>
        public IReadOnlyCollection<string> ConflictedSyncKeys => state.GetConflictedKeys();

        /// <summary>
        /// Enqueues a local note change for the next pass.
        /// </summary>
        /// <param name="note">The saved note.</param>
        public void OnNoteSaved(AvaloniaNoteDocument note)
        {
            if (note is not null && !string.IsNullOrWhiteSpace(note.SyncKey))
            {
                state.Enqueue(note.SyncKey, "upsert");
            }
        }

        /// <summary>
        /// Enqueues a local note deletion (tombstone) for the next pass.
        /// </summary>
        /// <param name="syncKey">The deleted note's sync key.</param>
        public void OnNoteDeleted(string syncKey)
        {
            if (!string.IsNullOrWhiteSpace(syncKey))
            {
                state.Enqueue(syncKey, "delete");
            }
        }

        /// <summary>
        /// Runs one full sync pass. Serialized so periodic and manual triggers never overlap; a
        /// trigger that arrives mid-pass is skipped rather than queued.
        /// </summary>
        /// <param name="cancellationToken">Cancels the pass.</param>
        /// <returns>The pass result.</returns>
        public async Task<SyncResult> SyncNowAsync(CancellationToken cancellationToken = default)
        {
            Func<ISyncClient?>? factory = clientFactory;
            if (disposed || factory is null)
            {
                return SyncResult.Skipped("disabled");
            }

            if (!await passGate.WaitAsync(0, cancellationToken).ConfigureAwait(false))
            {
                return SyncResult.Skipped("busy");
            }

            try
            {
                return await RunPassAsync(factory, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is HttpRequestException or IOException or InvalidOperationException)
            {
                Logging.AppLogger.Debug($"Sync pass failed: {ex.Message}");
                return SyncResult.Failed(ex.Message);
            }
            finally
            {
                passGate.Release();
            }
        }

        private async Task<SyncResult> RunPassAsync(Func<ISyncClient?> factory, CancellationToken cancellationToken)
        {
            ISyncClient? client = factory();
            if (client is null)
            {
                return SyncResult.Skipped("no-client");
            }

            try
            {
                string notesDir = RemotePath(RemoteNotesDir);

                // Ensure the remote tree exists. A failure here means we cannot trust a later empty
                // listing, so abort before any key processing rather than risk inferring deletions.
                if (!await client.EnsureDirectoryAsync(remoteRoot).ConfigureAwait(false) ||
                    !await client.EnsureDirectoryAsync(notesDir).ConfigureAwait(false))
                {
                    Logging.AppLogger.Warn($"Sync aborted: could not ensure remote directory '{notesDir}'.");
                    return SyncResult.Failed("ensure-directory-failed");
                }

                Dictionary<string, List<AvaloniaNoteDocument>> localByKey = repository.LoadAll()
                    .GroupBy(n => n.SyncKey, StringComparer.Ordinal)
                    .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);

                // A failed listing is indistinguishable from "empty" at the entry level, so we check the
                // explicit Ok flag. Treating a failed listing as empty would delete every synced note.
                RemoteListing listing = await client.ListDirectoryAsync(notesDir).ConfigureAwait(false);
                if (!listing.Ok)
                {
                    Logging.AppLogger.Warn(
                        $"Sync aborted: remote listing of '{notesDir}' failed; not inferring deletions for {localByKey.Count} local note(s).");
                    return SyncResult.Failed("list-failed");
                }

                Dictionary<string, RemoteEntry> remoteByKey = new Dictionary<string, RemoteEntry>(StringComparer.Ordinal);
                foreach (RemoteEntry entry in listing.Entries)
                {
                    string key = KeyFromRemotePath(entry.RelativePath);
                    if (key.Length > 0)
                    {
                        // Collapse the entry to the validator token for the active mode, stored in the ETag
                        // slot so the decider compares one opaque string regardless of source.
                        remoteByKey[key] = entry with { ETag = ValidatorFor(entry.ETag, entry.LastModified) };
                    }
                }

                HashSet<string> conflicted = new HashSet<string>(state.GetConflictedKeys(), StringComparer.Ordinal);
                HashSet<string> forced = new HashSet<string>(
                    state.GetQueue().Where(q => q.Operation == ForceUploadOp).Select(q => q.SyncKey),
                    StringComparer.Ordinal);
                HashSet<string> keys = new HashSet<string>(StringComparer.Ordinal);
                keys.UnionWith(localByKey.Keys);
                keys.UnionWith(remoteByKey.Keys);
                keys.UnionWith(state.GetAllBaselines().Select(b => b.SyncKey));

                // Decide every eligible key up front (pure, no IO) so a bulk delete can be confirmed
                // before anything is applied.
                List<PlannedKey> work = BuildWorkItems(keys, conflicted, forced, localByKey, remoteByKey);
                Logging.AppLogger.Info(
                    $"Sync pass: {localByKey.Count} local, {remoteByKey.Count} remote, {work.Count} key(s) to process.");

                // Classify the destructive effects (including deletions that arrive as a tombstone Download)
                // and gate the pass on them. Any remote document fetched here is cached for reuse at apply.
                (List<SyncChangeItem> deletions, Dictionary<string, RemoteFetch> prefetched) =
                    await BuildDeletionPlanAsync(client, work, cancellationToken).ConfigureAwait(false);

                // When a human will be prompted, drop the open WebDAV/HTTP connection first: the dialog can
                // sit unanswered for minutes, and there is no reason to hold a socket (or the server's
                // connection slot) the whole time. The plan is already fixed and the prefetched tombstones
                // are cached, so the wait needs no client. A fresh one is opened for the apply phase.
                bool willPrompt = WillPromptForDeletions(deletions);
                if (willPrompt)
                {
                    client.Dispose();
                }

                if (!await ConfirmDeletionsAsync(deletions).ConfigureAwait(false))
                {
                    Logging.AppLogger.Warn("Sync aborted: user declined the pending deletions.");
                    return SyncResult.Skipped("delete-declined");
                }

                if (willPrompt)
                {
                    client = factory();
                    if (client is null)
                    {
                        // Sync was disabled (or the client factory went away) while the dialog was open.
                        Logging.AppLogger.Warn("Sync aborted after confirmation: client no longer available.");
                        return SyncResult.Skipped("no-client");
                    }
                }

                // Snapshot the live notes once, after the confirm dialog returned, so each destructive action
                // can be re-validated against edits the user may have made while the dialog was open — without
                // re-reading the repository per key.
                ApplyContext context = new ApplyContext(
                    repository.LoadAll().GroupBy(n => n.Id, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal),
                    prefetched);

                int uploaded = 0, downloaded = 0, deleted = 0;
                bool changed = false;
                foreach (PlannedKey item in work)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    try
                    {
                        bool keyChanged = await ProcessKeyAsync(client, item, context, cancellationToken).ConfigureAwait(false);
                        if (keyChanged)
                        {
                            changed = true;
                            CountAction(item, context, ref uploaded, ref downloaded, ref deleted);
                            string noteName = item.Local?.Title ?? "(remote note)";
                            Logging.AppLogger.Info($"Sync {item.Action}: note '{noteName}' [{item.SyncKey}].");
                        }
                    }
                    catch (Exception ex) when (ex is HttpRequestException or IOException or InvalidOperationException)
                    {
                        Logging.AppLogger.Warn($"Sync key '{item.SyncKey}' failed: {ex.Message}");
                    }
                }

                Logging.AppLogger.Info($"Sync pass complete: {uploaded} uploaded, {downloaded} downloaded, {deleted} deleted.");

                // Mirror referenced note assets (pasted images, attachments) after notes converge, so
                // freshly downloaded notes contribute their asset references this same pass. Best-effort:
                // never fails the pass, and asset transfers count as a change so the UI refreshes.
                int assetsTransferred = await SyncAssetsAsync(client, cancellationToken).ConfigureAwait(false);
                if (assetsTransferred > 0)
                {
                    changed = true;
                }

                if (changed)
                {
                    SyncCompleted?.Invoke(this, EventArgs.Empty);
                }

                return SyncResult.Completed(changed, uploaded, downloaded, deleted);
            }
            finally
            {
                client?.Dispose();
            }
        }

        /// <summary>
        /// Whether <see cref="ConfirmDeletionsAsync"/> will actually surface a dialog for this plan (a
        /// confirmer is wired and the count reaches the threshold). Used to decide whether to release the
        /// client across the wait; mirrors the guard inside <see cref="ConfirmDeletionsAsync"/>.
        /// </summary>
        private bool WillPromptForDeletions(List<SyncChangeItem> deletions) =>
            ConfirmBulkChanges is not null && deletions.Count >= BulkDeleteThreshold;

        /// <summary>
        /// Coalesces the ETag of a file known to exist (a listing entry, or a file just uploaded or
        /// downloaded) to a stable non-null value. Servers that omit ETags would otherwise yield null,
        /// which never equals the value stored in the baseline, making every unchanged note look changed
        /// and loop forever (re-download or re-delete each pass). Existence is established by the caller
        /// (listing membership / transfer success), never by this value, so a missing ETag safely
        /// becomes the "present" sentinel.
        /// </summary>
        internal static string NormalizeETag(string? rawETag) =>
            string.IsNullOrEmpty(rawETag) ? "present" : rawETag;

        /// <summary>
        /// Produces the change-detection validator token for a file known to exist, from the source
        /// selected by <see cref="ChangeDetection"/>. In <see cref="ChangeDetectionMode.LastModified"/>
        /// mode the timestamp is rendered as a stable round-trip UTC string; in ETag mode the ETag is
        /// used. Either way a missing value collapses to the "present" sentinel (see
        /// <see cref="NormalizeETag"/>) so an unchanged note still settles instead of looping.
        /// </summary>
        internal string ValidatorFor(string? rawETag, DateTimeOffset? lastModified) =>
            ChangeDetection == ChangeDetectionMode.LastModified
                ? NormalizeETag(lastModified?.ToUniversalTime().ToString("O", System.Globalization.CultureInfo.InvariantCulture))
                : NormalizeETag(rawETag);

        private static void CountAction(PlannedKey item, ApplyContext context, ref int uploaded, ref int downloaded, ref int deleted)
        {
            switch (item.Action)
            {
                case SyncAction.Upload:
                    uploaded++;
                    break;
                case SyncAction.Download:
                    // A download that applied a tombstone removed the local note; count it as a deletion,
                    // otherwise as a content download.
                    if (context.PrefetchedByKey.TryGetValue(item.SyncKey, out RemoteFetch? fetch) && fetch.Wire is { Deleted: true } && item.Local is not null)
                    {
                        deleted++;
                    }
                    else
                    {
                        downloaded++;
                    }
                    break;
                case SyncAction.DeleteLocal:
                case SyncAction.DeleteRemote:
                    deleted++;
                    break;
                default:
                    break;
            }
        }

        /// <summary>
        /// Builds the set of data-losing deletions the gate should confirm, and prefetches the remote
        /// documents needed to classify them. Only <em>incoming</em> deletions are gated: an explicit
        /// <see cref="SyncAction.DeleteLocal"/>, or — the common cross-device case — a
        /// <see cref="SyncAction.Download"/> whose remote document is a tombstone, both of which remove a
        /// note <em>from this device</em> as a surprise. A <see cref="SyncAction.DeleteRemote"/> is the
        /// user's own local deletion propagating outward; the note is already gone here, so re-prompting
        /// for it is just alert fatigue and would desensitize the user to the prompts that matter. The
        /// tombstone-write still runs in the apply phase, guarded against a concurrent recreate.
        /// The tombstone-download case is invisible without reading the remote file, so we fetch candidate
        /// downloads here (bounded: only when enough deletions could exist to reach the confirmation
        /// threshold) and cache them for reuse during apply.
        /// </summary>
        private async Task<(List<SyncChangeItem> Deletions, Dictionary<string, RemoteFetch> Prefetched)> BuildDeletionPlanAsync(
            ISyncClient client, List<PlannedKey> work, CancellationToken cancellationToken)
        {
            List<SyncChangeItem> deletions = new List<SyncChangeItem>();
            Dictionary<string, RemoteFetch> prefetched = new Dictionary<string, RemoteFetch>(StringComparer.Ordinal);

            foreach (PlannedKey item in work)
            {
                if (item.Action == SyncAction.DeleteLocal)
                {
                    deletions.Add(new SyncChangeItem(item.SyncKey, item.Local?.Title ?? item.SyncKey, SyncDeleteSide.Local));
                }
            }

            // A tombstone-download only deletes when a local note exists to remove. If even treating
            // every such candidate as a deletion cannot reach the threshold, skip the prefetch entirely.
            List<PlannedKey> downloadCandidates = work
                .Where(w => w.Action == SyncAction.Download && w.Local is not null)
                .ToList();

            if (ConfirmBulkChanges is not null && deletions.Count + downloadCandidates.Count >= BulkDeleteThreshold)
            {
                foreach (PlannedKey candidate in downloadCandidates)
                {
                    RemoteFetch fetch = await FetchRemoteAsync(client, candidate.SyncKey, cancellationToken).ConfigureAwait(false);
                    prefetched[candidate.SyncKey] = fetch;
                    if (fetch.Wire is { Deleted: true })
                    {
                        deletions.Add(new SyncChangeItem(candidate.SyncKey, candidate.Local?.Title ?? candidate.SyncKey, SyncDeleteSide.Local));
                    }
                }
            }

            return (deletions, prefetched);
        }

        private async Task<bool> ConfirmDeletionsAsync(List<SyncChangeItem> deletions)
        {
            if (deletions.Count > 0)
            {
                Logging.AppLogger.Info($"Sync plans {deletions.Count} deletion(s): " +
                    string.Join(", ", deletions.Select(d => $"{d.Side} '{d.Title}'")));
            }

            Func<SyncChangePlan, Task<bool>>? confirm = ConfirmBulkChanges;
            if (confirm is null || deletions.Count < BulkDeleteThreshold)
            {
                return true;
            }

            return await confirm(new SyncChangePlan { Deletions = deletions }).ConfigureAwait(false);
        }

        private string RemotePath(string relative) =>
            remoteRoot.Length == 0 ? relative : $"{remoteRoot}/{relative}";

        private string RemoteNotePath(string syncKey) => RemotePath($"{RemoteNotesDir}/{syncKey}.json");

        private static string KeyFromRemotePath(string relativePath)
        {
            string name = Path.GetFileName(relativePath);
            return name.EndsWith(".json", StringComparison.OrdinalIgnoreCase)
                ? name[..^5]
                : string.Empty;
        }

        /// <summary>
        /// Sets the live client factory and scheduler period, (re)starting or stopping the periodic
        /// timer accordingly. Pass a null factory or non-positive interval to disable sync.
        /// </summary>
        /// <param name="clientFactory">Creates a fresh <see cref="ISyncClient"/> per pass, or null to disable.</param>
        /// <param name="remoteRoot">The remote root directory (relative path under the WebDAV base).</param>
        /// <param name="interval">The periodic sync interval, or <see cref="TimeSpan.Zero"/> to run only on demand.</param>
        public void Reconfigure(Func<ISyncClient?>? clientFactory, string remoteRoot, TimeSpan interval)
        {
            lock (timerGate)
            {
                this.clientFactory = clientFactory;
                this.remoteRoot = (remoteRoot ?? string.Empty).Trim().Trim('/');
                timer?.Dispose();
                timer = null;

                if (clientFactory is not null && interval > TimeSpan.Zero)
                {
                    timer = new System.Threading.Timer(_ => _ = SyncNowAsync(CancellationToken.None), null, interval, interval);
                }
            }
        }

        /// <summary>
        /// Stops the periodic timer.
        /// </summary>
        public void Stop()
        {
            lock (timerGate)
            {
                timer?.Dispose();
                timer = null;
            }
        }

        /// <summary>
        /// Marks a queued change as a forced upload: the next pass uploads the local note for this key
        /// unconditionally, overwriting whatever the remote holds regardless of its validator. Used by
        /// conflict resolution to make the chosen version the single source of truth.
        /// </summary>
        private const string ForceUploadOp = "force-upload";

        /// <summary>
        /// Resolves a conflict by declaring one surviving row the single source of truth: every other
        /// local row sharing the key is deleted, the conflict and baseline are cleared, and a forced
        /// upload is queued so the next pass overwrites the remote unconditionally (ignoring its edit
        /// time / validator) rather than re-detecting divergence.
        /// </summary>
        /// <param name="syncKey">The conflicted sync key.</param>
        /// <param name="winningNoteId">The id of the row that should win and become the truth.</param>
        /// <param name="error">A short reason when resolution is rejected.</param>
        /// <returns><see langword="true"/> when the conflict was cleared.</returns>
        public bool TryResolveConflict(string syncKey, string winningNoteId, out string? error)
        {
            error = null;
            List<AvaloniaNoteDocument> rows = repository.LoadAll().Where(n => n.SyncKey == syncKey).ToList();
            if (rows.Count == 0)
            {
                error = "Sync.Resolve.None";
                return false;
            }

            AvaloniaNoteDocument? winner = rows.FirstOrDefault(n => n.Id == winningNoteId);
            if (winner is null)
            {
                error = "Sync.Resolve.None";
                return false;
            }

            // Delete the losing rows. They share the sync key with the surviving winner, so the key still
            // has a live local note afterward and no remote tombstone is implied.
            foreach (AvaloniaNoteDocument loser in rows.Where(n => n.Id != winningNoteId))
            {
                repository.Delete(loser.Id);
            }

            // Clear the conflict and drop the baseline, then queue a forced upload so the next pass
            // overwrites the remote with the winner regardless of the remote's current validator.
            state.ClearConflict(syncKey);
            state.DeleteBaseline(syncKey);
            state.Enqueue(syncKey, ForceUploadOp);
            return true;
        }

        /// <inheritdoc />
        public void Dispose()
        {
            disposed = true;
            Stop();

            // Deliberately do NOT dispose passGate here. A pass running on the threadpool may have
            // passed the disposed check and still hold the gate; its finally would then Release() a
            // disposed semaphore and throw ObjectDisposedException on an unobserved fire-and-forget
            // task. We never use the semaphore's AvailableWaitHandle, so SemaphoreSlim needs no
            // explicit disposal — GC reclaims it once the in-flight pass completes.
        }
    }
}
