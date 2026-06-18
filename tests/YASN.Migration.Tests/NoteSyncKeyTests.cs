using YASN.AvaloniaNotes;

namespace YASN.Migration.Tests
{
    /// <summary>
    /// Verifies the stable sync-key: round-trip, backfill of legacy entries, and conflict copies that
    /// share a key while taking a fresh local id.
    /// </summary>
    public sealed class NoteSyncKeyTests : IDisposable
    {
        private readonly string root = Path.Combine(Path.GetTempPath(), "yasn-synckey-tests", Guid.NewGuid().ToString("N"));

        /// <summary>
        /// Removes the temporary data directory.
        /// </summary>
        public void Dispose()
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }

        /// <summary>
        /// A saved note's sync key round-trips through the index.
        /// </summary>
        [Fact]
        public void SyncKeyRoundTrips()
        {
            NoteRepository repository = new NoteRepository(root);
            AvaloniaNoteDocument note = new AvaloniaNoteDocument { Id = "1", SyncKey = "abc123", Content = "x" };
            repository.Save(note);

            AvaloniaNoteDocument loaded = repository.LoadAll().Single();

            Assert.Equal("abc123", loaded.SyncKey);
        }

        /// <summary>
        /// A legacy index entry without a sync key is backfilled and persisted on load.
        /// </summary>
        [Fact]
        public void BackfillAssignsAndPersistsSyncKey()
        {
            Directory.CreateDirectory(root);
            string legacy = "{\"schemaVersion\":4,\"notes\":[{\"id\":7,\"title\":\"Old\"}]}";
            File.WriteAllText(Path.Combine(root, "notes.index.json"), legacy);

            NoteRepository repository = new NoteRepository(root);
            AvaloniaNoteDocument first = repository.LoadAll().Single();
            string assigned = first.SyncKey;

            Assert.False(string.IsNullOrWhiteSpace(assigned));

            // Reload via a fresh repository: the backfilled key must be stable, not regenerated.
            AvaloniaNoteDocument reloaded = new NoteRepository(root).LoadAll().Single();
            Assert.Equal(assigned, reloaded.SyncKey);
        }

        /// <summary>
        /// A conflict copy keeps the source sync key but receives a new local id.
        /// </summary>
        [Fact]
        public void CreateConflictCopySharesKeyWithNewId()
        {
            NoteRepository repository = new NoteRepository(root);
            AvaloniaNoteDocument local = new AvaloniaNoteDocument { Id = "1", SyncKey = "shared", Content = "local" };
            repository.Save(local);

            AvaloniaNoteDocument remote = new AvaloniaNoteDocument { Id = "remote-id", SyncKey = "shared", Content = "remote" };
            AvaloniaNoteDocument copy = repository.CreateConflictCopy(remote);

            Assert.Equal("shared", copy.SyncKey);
            Assert.NotEqual("1", copy.Id);
            Assert.Equal(2, repository.LoadAll().Count(n => n.SyncKey == "shared"));
        }
    }
}
