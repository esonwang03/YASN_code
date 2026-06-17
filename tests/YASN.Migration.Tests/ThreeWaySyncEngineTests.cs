using YASN.AvaloniaNotes;
using YASN.Infrastructure.Sync;

namespace YASN.Migration.Tests
{
    /// <summary>
    /// Exercises the three-way engine end to end against an in-memory client and a temp repository:
    /// initial up/down, one-sided edits, convergence, conflict creation, and the resolve lifecycle.
    /// </summary>
    public sealed class ThreeWaySyncEngineTests : IDisposable
    {
        private readonly string root = Path.Combine(Path.GetTempPath(), "yasn-engine-tests", Guid.NewGuid().ToString("N"));
        private readonly NoteRepository repository;
        private readonly SyncStateStore state;
        private readonly FakeSyncClient client = new();
        private readonly ThreeWaySyncEngine engine;

        public ThreeWaySyncEngineTests()
        {
            repository = new NoteRepository(root);
            state = new SyncStateStore(Path.Combine(root, "sync.db"));
            engine = new ThreeWaySyncEngine(repository, state);
            engine.Reconfigure(() => client, string.Empty, TimeSpan.Zero);
        }

        public void Dispose()
        {
            engine.Dispose();
            state.Dispose();
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }

        private AvaloniaNoteDocument Save(string key, string content, int id)
        {
            AvaloniaNoteDocument note = new AvaloniaNoteDocument { Id = id, SyncKey = key, Content = content };
            repository.Save(note);
            return note;
        }

        private void SeedRemote(string key, string content)
        {
            SyncNoteDocument wire = new SyncNoteDocument { SyncKey = key, Content = content };
            client.SeedRemote($"notes/{key}.json", NoteWireSerializer.Serialize(wire));
        }

        /// <summary>A purely local note uploads on first pass.</summary>
        [Fact]
        public async Task LocalOnlyUploads()
        {
            Save("k1", "local body", 1);
            await engine.SyncNowAsync();

            Assert.Equal("local body", await ReadRemote("k1"));
            Assert.NotNull(state.GetBaseline("k1"));
        }

        /// <summary>A purely remote note downloads on first pass.</summary>
        [Fact]
        public async Task RemoteOnlyDownloads()
        {
            SeedRemote("k2", "remote body");
            await engine.SyncNowAsync();

            AvaloniaNoteDocument? local = repository.LoadAll().FirstOrDefault(n => n.SyncKey == "k2");
            Assert.NotNull(local);
            Assert.Equal("remote body", local!.Content);
        }

        private async Task<string?> ReadRemote(string key)
        {
            string temp = Path.Combine(root, $"read-{key}.json");
            if (!await client.DownloadFileAsync($"notes/{key}.json", temp))
            {
                return null;
            }

            return NoteWireSerializer.Deserialize(File.ReadAllBytes(temp))?.Content;
        }

        /// <summary>After an initial sync, an unchanged second pass is a no-op.</summary>
        [Fact]
        public async Task UnchangedSecondPassIsNoOp()
        {
            Save("k1", "body", 1);
            SyncResult first = await engine.SyncNowAsync();
            SyncResult second = await engine.SyncNowAsync();

            Assert.True(first.Changed);
            Assert.False(second.Changed);
        }

        /// <summary>A local edit after baseline uploads the new content.</summary>
        [Fact]
        public async Task LocalEditUploads()
        {
            AvaloniaNoteDocument note = Save("k1", "v1", 1);
            await engine.SyncNowAsync();

            note.Content = "v2";
            repository.Save(note);
            await engine.SyncNowAsync();

            Assert.Equal("v2", await ReadRemote("k1"));
        }

        /// <summary>A remote edit after baseline downloads into the existing local note.</summary>
        [Fact]
        public async Task RemoteEditDownloadsIntoSameNote()
        {
            Save("k1", "v1", 1);
            await engine.SyncNowAsync();

            SyncNoteDocument edited = new SyncNoteDocument { SyncKey = "k1", Content = "remote v2" };
            client.SeedRemote("notes/k1.json", NoteWireSerializer.Serialize(edited));
            await engine.SyncNowAsync();

            Assert.Single(repository.LoadAll(), n => n.SyncKey == "k1");
            Assert.Equal("remote v2", repository.LoadAll().Single(n => n.SyncKey == "k1").Content);
        }

        /// <summary>Both sides editing to identical content converges with no conflict.</summary>
        [Fact]
        public async Task ConvergentEditsDoNotConflict()
        {
            AvaloniaNoteDocument note = Save("k1", "v1", 1);
            await engine.SyncNowAsync();

            note.Content = "same";
            repository.Save(note);
            SyncNoteDocument remote = NoteSyncMapper.ToWire(note, DateTimeOffset.UnixEpoch);
            client.SeedRemote("notes/k1.json", NoteWireSerializer.Serialize(remote));

            await engine.SyncNowAsync();

            Assert.Empty(engine.ConflictedSyncKeys);
            Assert.Single(repository.LoadAll(), n => n.SyncKey == "k1");
        }

        /// <summary>Divergent edits create a conflict copy and exclude the key from sync.</summary>
        [Fact]
        public async Task DivergentEditsCreateConflict()
        {
            AvaloniaNoteDocument note = Save("k1", "v1", 1);
            await engine.SyncNowAsync();

            note.Content = "local v2";
            repository.Save(note);
            SyncNoteDocument remote = new SyncNoteDocument { SyncKey = "k1", Content = "remote v2" };
            client.SeedRemote("notes/k1.json", NoteWireSerializer.Serialize(remote));

            await engine.SyncNowAsync();

            Assert.Contains("k1", engine.ConflictedSyncKeys);
            Assert.Equal(2, repository.LoadAll().Count(n => n.SyncKey == "k1"));
        }

        /// <summary>Resolution is rejected while two copies share the key, and succeeds after deleting one.</summary>
        [Fact]
        public async Task ConflictResolvesAfterDeletingDuplicate()
        {
            AvaloniaNoteDocument note = Save("k1", "v1", 1);
            await engine.SyncNowAsync();
            note.Content = "local v2";
            repository.Save(note);
            client.SeedRemote("notes/k1.json", NoteWireSerializer.Serialize(new SyncNoteDocument { SyncKey = "k1", Content = "remote v2" }));
            await engine.SyncNowAsync();

            Assert.False(engine.TryResolveConflict("k1", out string? error));
            Assert.Equal("Sync.Resolve.Duplicates", error);

            // Delete the conflict copy (the higher id), leaving one note for the key.
            int copyId = repository.LoadAll().Where(n => n.SyncKey == "k1").Max(n => n.Id);
            repository.Delete(copyId);

            Assert.True(engine.TryResolveConflict("k1", out _));
            Assert.DoesNotContain("k1", engine.ConflictedSyncKeys);
        }

        /// <summary>Overlapping passes serialize: the second returns busy rather than running concurrently.</summary>
        [Fact]
        public async Task OverlappingPassesSerialize()
        {
            Save("k1", "body", 1);
            Task<SyncResult> a = engine.SyncNowAsync();
            Task<SyncResult> b = engine.SyncNowAsync();
            SyncResult[] results = await Task.WhenAll(a, b);

            Assert.Contains(results, r => r.Message == "busy" || !r.Changed);
        }
    }
}
