using YASN.Infrastructure.Sync;

namespace YASN.Migration.Tests
{
    /// <summary>
    /// Verifies the SQLite-backed sync state store: baseline upsert/read, queue coalescing, conflict
    /// tracking, and persistence across reopen.
    /// </summary>
    public sealed class SyncStateStoreTests : IDisposable
    {
        private readonly string dbPath = Path.Combine(Path.GetTempPath(), "yasn-syncdb-tests", Guid.NewGuid().ToString("N"), "sync.db");

        /// <summary>
        /// Removes the temporary database directory.
        /// </summary>
        public void Dispose()
        {
            string? dir = Path.GetDirectoryName(dbPath);
            if (dir is not null && Directory.Exists(dir))
            {
                Directory.Delete(dir, recursive: true);
            }
        }

        /// <summary>
        /// A baseline upserts, reads back, and survives reopening the database.
        /// </summary>
        [Fact]
        public void BaselineUpsertsReadsAndPersists()
        {
            SyncBaseline baseline = new SyncBaseline("k1", "hash1", "etag1", "notes/k1.json", DateTimeOffset.UnixEpoch, false);
            using (SyncStateStore store = new SyncStateStore(dbPath))
            {
                store.UpsertBaseline(baseline);
                store.UpsertBaseline(baseline with { LocalHash = "hash2" });
            }

            using SyncStateStore reopened = new SyncStateStore(dbPath);
            SyncBaseline? read = reopened.GetBaseline("k1");

            Assert.NotNull(read);
            Assert.Equal("hash2", read!.LocalHash);
            Assert.Equal("etag1", read.RemoteETag);
            Assert.Single(reopened.GetAllBaselines());
        }

        /// <summary>
        /// Enqueuing the same key twice coalesces to a single row with the latest op.
        /// </summary>
        [Fact]
        public void QueueCoalescesByKey()
        {
            using SyncStateStore store = new SyncStateStore(dbPath);
            store.Enqueue("k1", "upsert");
            store.Enqueue("k1", "delete");
            store.Enqueue("k2", "upsert");

            IReadOnlyList<SyncQueueItem> queue = store.GetQueue();

            Assert.Equal(2, queue.Count);
            Assert.Equal("delete", queue.Single(i => i.SyncKey == "k1").Operation);

            store.Dequeue("k1");
            Assert.Single(store.GetQueue());
        }

        /// <summary>
        /// Conflict keys are tracked, deduplicated, and clearable.
        /// </summary>
        [Fact]
        public void ConflictTrackingInsertsAndClears()
        {
            using SyncStateStore store = new SyncStateStore(dbPath);
            store.MarkConflict("k1", "both changed");
            store.MarkConflict("k1", "ignored duplicate");
            store.MarkConflict("k2", null);

            Assert.Equal(2, store.GetConflictedKeys().Count);
            Assert.Contains("k1", store.GetConflictedKeys());

            store.ClearConflict("k1");
            Assert.DoesNotContain("k1", store.GetConflictedKeys());
        }

        /// <summary>
        /// Deleting a baseline removes it.
        /// </summary>
        [Fact]
        public void DeleteBaselineRemovesRow()
        {
            using SyncStateStore store = new SyncStateStore(dbPath);
            store.UpsertBaseline(new SyncBaseline("k1", null, null, "notes/k1.json", DateTimeOffset.UnixEpoch, false));
            store.DeleteBaseline("k1");

            Assert.Null(store.GetBaseline("k1"));
        }
    }
}
