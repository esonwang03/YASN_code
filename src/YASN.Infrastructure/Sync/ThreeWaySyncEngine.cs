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

            ISyncClient? client = null;
            try
            {
                client = factory();
                if (client is null)
                {
                    return SyncResult.Skipped("no-client");
                }

                return await RunPassAsync(client, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is HttpRequestException or IOException or InvalidOperationException)
            {
                Logging.AppLogger.Debug($"Sync pass failed: {ex.Message}");
                return SyncResult.Failed(ex.Message);
            }
            finally
            {
                client?.Dispose();
                passGate.Release();
            }
        }

        private async Task<SyncResult> RunPassAsync(ISyncClient client, CancellationToken cancellationToken)
        {
            await client.EnsureDirectoryAsync(remoteRoot).ConfigureAwait(false);
            await client.EnsureDirectoryAsync(RemotePath(RemoteNotesDir)).ConfigureAwait(false);

            HashSet<string> conflicted = new HashSet<string>(state.GetConflictedKeys(), StringComparer.Ordinal);

            Dictionary<string, List<AvaloniaNoteDocument>> localByKey = repository.LoadAll()
                .GroupBy(n => n.SyncKey, StringComparer.Ordinal)
                .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);

            IReadOnlyList<RemoteEntry> remoteEntries = await client.ListDirectoryAsync(RemotePath(RemoteNotesDir)).ConfigureAwait(false);
            Dictionary<string, RemoteEntry> remoteByKey = new Dictionary<string, RemoteEntry>(StringComparer.Ordinal);
            foreach (RemoteEntry entry in remoteEntries)
            {
                string key = KeyFromRemotePath(entry.RelativePath);
                if (key.Length > 0)
                {
                    remoteByKey[key] = entry;
                }
            }

            HashSet<string> keys = new HashSet<string>(StringComparer.Ordinal);
            keys.UnionWith(localByKey.Keys);
            keys.UnionWith(remoteByKey.Keys);
            keys.UnionWith(state.GetAllBaselines().Select(b => b.SyncKey));

            bool changed = false;
            foreach (string key in keys)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (conflicted.Contains(key))
                {
                    continue;
                }

                // A key with two local rows but no recorded conflict is a freshly duplicated pair; skip.
                if (localByKey.TryGetValue(key, out List<AvaloniaNoteDocument>? rows) && rows.Count > 1)
                {
                    continue;
                }

                try
                {
                    changed |= await ProcessKeyAsync(client, key, rows?.FirstOrDefault(),
                        remoteByKey.GetValueOrDefault(key), cancellationToken).ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is HttpRequestException or IOException or InvalidOperationException)
                {
                    Logging.AppLogger.Debug($"Sync key '{key}' failed: {ex.Message}");
                }
            }

            if (changed)
            {
                SyncCompleted?.Invoke(this, EventArgs.Empty);
            }

            return SyncResult.Completed(changed);
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
        /// Attempts to resolve a conflict by re-baselining from the single surviving note for the key.
        /// Fails when zero or more than one note still shares the key (the user must delete duplicates
        /// down to exactly one first).
        /// </summary>
        /// <param name="syncKey">The conflicted sync key.</param>
        /// <param name="error">A short reason when resolution is rejected.</param>
        /// <returns><see langword="true"/> when the conflict was cleared.</returns>
        public bool TryResolveConflict(string syncKey, out string? error)
        {
            error = null;
            int count = repository.LoadAll().Count(n => n.SyncKey == syncKey);
            if (count == 0)
            {
                error = "Sync.Resolve.None";
                return false;
            }

            if (count > 1)
            {
                error = "Sync.Resolve.Duplicates";
                return false;
            }

            // Clear conflict and drop the baseline so the survivor re-uploads as the new truth.
            state.ClearConflict(syncKey);
            state.DeleteBaseline(syncKey);
            state.Enqueue(syncKey, "upsert");
            return true;
        }

        /// <inheritdoc />
        public void Dispose()
        {
            disposed = true;
            Stop();
            passGate.Dispose();
        }
    }
}
