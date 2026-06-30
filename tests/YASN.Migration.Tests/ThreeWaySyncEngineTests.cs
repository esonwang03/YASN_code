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

        private AvaloniaNoteDocument Save(string key, string content, string id)
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

        /// <summary>Replaces a remote note with a tombstone, as another device's delete would.</summary>
        private void SeedTombstone(string key)
        {
            SyncNoteDocument wire = new SyncNoteDocument { SyncKey = key, Deleted = true };
            client.SeedRemote($"notes/{key}.json", NoteWireSerializer.Serialize(wire));
        }

        /// <summary>A purely local note uploads on first pass.</summary>
        [Fact]
        public async Task LocalOnlyUploads()
        {
            Save("k1", "local body", "1");
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

        /// <summary>True when the remote note exists and is a tombstone (a propagated deletion).</summary>
        private async Task<bool> IsRemoteTombstone(string key)
        {
            string temp = Path.Combine(root, $"tomb-{key}.json");
            if (!await client.DownloadFileAsync($"notes/{key}.json", temp))
            {
                return false;
            }

            return NoteWireSerializer.Deserialize(File.ReadAllBytes(temp))?.Deleted == true;
        }

        /// <summary>After an initial sync, an unchanged second pass is a no-op.</summary>
        [Fact]
        public async Task UnchangedSecondPassIsNoOp()
        {
            Save("k1", "body", "1");
            SyncResult first = await engine.SyncNowAsync();
            SyncResult second = await engine.SyncNowAsync();

            Assert.True(first.Changed);
            Assert.False(second.Changed);
        }

        /// <summary>A local edit after baseline uploads the new content.</summary>
        [Fact]
        public async Task LocalEditUploads()
        {
            AvaloniaNoteDocument note = Save("k1", "v1", "1");
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
            Save("k1", "v1", "1");
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
            AvaloniaNoteDocument note = Save("k1", "v1", "1");
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
            AvaloniaNoteDocument note = Save("k1", "v1", "1");
            await engine.SyncNowAsync();

            note.Content = "local v2";
            repository.Save(note);
            SyncNoteDocument remote = new SyncNoteDocument { SyncKey = "k1", Content = "remote v2" };
            client.SeedRemote("notes/k1.json", NoteWireSerializer.Serialize(remote));

            await engine.SyncNowAsync();

            Assert.Contains("k1", engine.ConflictedSyncKeys);
            Assert.Equal(2, repository.LoadAll().Count(n => n.SyncKey == "k1"));
        }

        /// <summary>
        /// Resolving a conflict picks one row as the winner, deletes the other automatically, and
        /// clears the conflict — the user no longer has to delete duplicates by hand first.
        /// </summary>
        [Fact]
        public async Task ConflictResolvesByPickingWinner()
        {
            AvaloniaNoteDocument note = Save("k1", "v1", "1");
            await engine.SyncNowAsync();
            note.Content = "local v2";
            repository.Save(note);
            client.SeedRemote("notes/k1.json", NoteWireSerializer.Serialize(new SyncNoteDocument { SyncKey = "k1", Content = "remote v2" }));
            await engine.SyncNowAsync();

            Assert.Equal(2, repository.LoadAll().Count(n => n.SyncKey == "k1"));

            // Pick the original local row (id "1") as the winner; the conflict copy is deleted.
            Assert.True(engine.TryResolveConflict("k1", "1", out _));
            Assert.DoesNotContain("k1", engine.ConflictedSyncKeys);
            AvaloniaNoteDocument survivor = repository.LoadAll().Single(n => n.SyncKey == "k1");
            Assert.Equal("1", survivor.Id);
        }

        /// <summary>
        /// Resolving with an unknown winner id (e.g. the row vanished) is rejected rather than
        /// silently deleting everything.
        /// </summary>
        [Fact]
        public async Task ResolveRejectsUnknownWinner()
        {
            AvaloniaNoteDocument note = Save("k1", "v1", "1");
            await engine.SyncNowAsync();
            note.Content = "local v2";
            repository.Save(note);
            client.SeedRemote("notes/k1.json", NoteWireSerializer.Serialize(new SyncNoteDocument { SyncKey = "k1", Content = "remote v2" }));
            await engine.SyncNowAsync();

            Assert.False(engine.TryResolveConflict("k1", "does-not-exist", out string? error));
            Assert.Equal("Sync.Resolve.None", error);
            Assert.Contains("k1", engine.ConflictedSyncKeys);
        }

        /// <summary>
        /// The resolved winner becomes the only truth: after resolve + sync the remote holds the
        /// winner's content (overwriting the diverged remote version, ignoring edit time), the conflict
        /// is cleared, exactly one local row remains, and a further pass is a no-op (no re-conflict).
        /// </summary>
        [Fact]
        public async Task ForceResolveOverwritesRemoteAndDoesNotReconflict()
        {
            AvaloniaNoteDocument note = Save("k1", "v1", "1");
            await engine.SyncNowAsync();
            note.Content = "local wins";
            repository.Save(note);
            client.SeedRemote("notes/k1.json", NoteWireSerializer.Serialize(new SyncNoteDocument { SyncKey = "k1", Content = "remote loses" }));
            await engine.SyncNowAsync();

            Assert.Contains("k1", engine.ConflictedSyncKeys);

            Assert.True(engine.TryResolveConflict("k1", "1", out _));
            SyncResult resolvePass = await engine.SyncNowAsync();

            Assert.True(resolvePass.Success);
            Assert.Equal("local wins", await ReadRemote("k1"));
            Assert.DoesNotContain("k1", engine.ConflictedSyncKeys);
            Assert.Single(repository.LoadAll(), n => n.SyncKey == "k1");

            SyncResult settled = await engine.SyncNowAsync();
            Assert.False(settled.Changed);
            Assert.DoesNotContain("k1", engine.ConflictedSyncKeys);
        }

        /// <summary>
        /// Regression for the data-wipe bug: a failed remote listing (transient error / unreadable
        /// directory) must never be read as "remote is empty", which would delete every synced note.
        /// </summary>
        [Fact]
        public async Task FailedListingNeverDeletesLocalNotes()
        {
            Save("k1", "body one", "1");
            Save("k2", "body two", "2");
            await engine.SyncNowAsync();
            Assert.Equal(2, repository.LoadAll().Count);

            // Next pass: the remote listing fails. The engine must abort, not delete.
            client.FailListing = true;
            SyncResult result = await engine.SyncNowAsync();

            Assert.False(result.Success);
            Assert.Equal("list-failed", result.Message);
            Assert.Equal(2, repository.LoadAll().Count);
        }

        /// <summary>A failure to ensure the remote directory aborts the pass without touching notes.</summary>
        [Fact]
        public async Task FailedEnsureDirectoryAbortsPass()
        {
            Save("k1", "body", "1");
            await engine.SyncNowAsync();

            client.FailEnsureDirectory = true;
            SyncResult result = await engine.SyncNowAsync();

            Assert.False(result.Success);
            Assert.Equal("ensure-directory-failed", result.Message);
            Assert.Single(repository.LoadAll());
        }

        /// <summary>On a fresh (genuinely empty) remote, local notes upload rather than being deleted.</summary>
        [Fact]
        public async Task EmptyRemoteUploadsLocalNotes()
        {
            Save("k1", "body", "1");
            SyncResult result = await engine.SyncNowAsync();

            Assert.True(result.Success);
            Assert.Equal("body", await ReadRemote("k1"));
            Assert.Equal(1, result.FilesUploaded);
        }

        /// <summary>When the user declines a bulk delete, no notes are removed and the pass is skipped.</summary>
        [Fact]
        public async Task DeclinedBulkDeleteKeepsNotes()
        {
            Save("k1", "one", "1");
            Save("k2", "two", "2");
            await engine.SyncNowAsync();

            // Both notes removed remotely while unchanged locally → two DeleteLocal actions.
            client.RemoveRemote("notes/k1.json");
            client.RemoveRemote("notes/k2.json");
            engine.ConfirmBulkChanges = _ => Task.FromResult(false);

            SyncResult result = await engine.SyncNowAsync();

            Assert.Equal("delete-declined", result.Message);
            Assert.Equal(2, repository.LoadAll().Count);
        }

        /// <summary>When the user approves a bulk delete, the notes are removed as planned.</summary>
        [Fact]
        public async Task ApprovedBulkDeleteRemovesNotes()
        {
            Save("k1", "one", "1");
            Save("k2", "two", "2");
            await engine.SyncNowAsync();

            client.RemoveRemote("notes/k1.json");
            client.RemoveRemote("notes/k2.json");
            SyncChangePlan? seen = null;
            engine.ConfirmBulkChanges = plan =>
            {
                seen = plan;
                return Task.FromResult(true);
            };

            SyncResult result = await engine.SyncNowAsync();

            Assert.True(result.Success);
            Assert.Equal(2, result.FilesDeleted);
            Assert.Empty(repository.LoadAll());
            Assert.NotNull(seen);
            Assert.Equal(2, seen!.LocalDeleteCount);
        }

        /// <summary>A single deletion stays below the threshold and never prompts the confirmer.</summary>
        [Fact]
        public async Task SingleDeleteDoesNotPromptConfirmer()
        {
            Save("k1", "one", "1");
            await engine.SyncNowAsync();

            client.RemoveRemote("notes/k1.json");
            bool prompted = false;
            engine.ConfirmBulkChanges = _ =>
            {
                prompted = true;
                return Task.FromResult(false);
            };

            SyncResult result = await engine.SyncNowAsync();

            Assert.False(prompted);
            Assert.True(result.Success);
            Assert.Empty(repository.LoadAll());
        }

        /// <summary>
        /// TOCTOU guard: a note edited while the confirm dialog is open (modeled by mutating the note
        /// inside the ConfirmBulkChanges callback, which runs at the exact plan/apply boundary) must not
        /// be deleted — its edit survives and uploads — while an untouched note in the same plan deletes.
        /// </summary>
        [Fact]
        public async Task EditDuringConfirmIsNotDeleted()
        {
            Save("k1", "one", "1");
            Save("k2", "two", "2");
            await engine.SyncNowAsync();

            client.RemoveRemote("notes/k1.json");
            client.RemoveRemote("notes/k2.json");
            engine.ConfirmBulkChanges = _ =>
            {
                // Simulate the user editing k1 on the UI thread while the dialog is open.
                Save("k1", "edited while dialog open", "1");
                return Task.FromResult(true);
            };

            SyncResult result = await engine.SyncNowAsync();

            Assert.True(result.Success);
            AvaloniaNoteDocument? survivor = repository.LoadAll().FirstOrDefault(n => n.Id == "1");
            Assert.NotNull(survivor);
            Assert.Equal("edited while dialog open", survivor!.Content);
            Assert.DoesNotContain(repository.LoadAll(), n => n.Id == "2");

            // The preserved edit uploads on the next pass rather than being lost.
            await engine.SyncNowAsync();
            Assert.Equal("edited while dialog open", await ReadRemote("k1"));
        }

        /// <summary>
        /// On a server that omits ETags, an unchanged note must settle: the second pass is a no-op, not
        /// an endless re-download. Without ETag normalization the null listing ETag never equals the
        /// stored baseline marker, so every pass looked "remote changed".
        /// </summary>
        [Fact]
        public async Task EtagLessServerSettlesAfterFirstSync()
        {
            client.SuppressETags = true;
            SeedRemote("k1", "remote body");

            SyncResult first = await engine.SyncNowAsync();
            Assert.True(first.Success);
            Assert.Equal(1, first.FilesDownloaded);

            SyncResult second = await engine.SyncNowAsync();
            Assert.True(second.Success);
            Assert.False(second.Changed);
            Assert.Equal(0, second.FilesDownloaded);
        }

        /// <summary>
        /// On an ETag-less server, Last-Modified change detection must still catch a remote edit. In ETag
        /// mode every entry collapses to the "present" sentinel, so an edited-but-still-present note looks
        /// unchanged; selecting Last-Modified makes the validator track the timestamp instead.
        /// </summary>
        [Fact]
        public async Task LastModifiedModeDetectsRemoteEditOnEtagLessServer()
        {
            client.SuppressETags = true;
            engine.ChangeDetection = ChangeDetectionMode.LastModified;

            SeedRemote("k1", "remote body");
            SyncResult first = await engine.SyncNowAsync();
            Assert.Equal(1, first.FilesDownloaded);
            Assert.Equal("remote body", repository.LoadAll().Single(n => n.SyncKey == "k1").Content);

            // Another device edits the note: same path, no ETag, but a newer Last-Modified stamp.
            SeedRemote("k1", "edited remotely");
            SyncResult second = await engine.SyncNowAsync();

            Assert.True(second.Changed);
            Assert.Equal(1, second.FilesDownloaded);
            Assert.Equal("edited remotely", repository.LoadAll().Single(n => n.SyncKey == "k1").Content);

            // And it still settles: a third pass with no further change is a no-op.
            SyncResult third = await engine.SyncNowAsync();
            Assert.False(third.Changed);
        }

        /// <summary>
        /// Sanity check on the gap this fixes: in ETag mode against an ETag-less server, a remote edit to
        /// an already-present note is NOT detected (both passes see the "present" sentinel). This is the
        /// motivation for offering Last-Modified detection.
        /// </summary>
        [Fact]
        public async Task EtagModeMissesRemoteEditOnEtagLessServer()
        {
            client.SuppressETags = true;
            engine.ChangeDetection = ChangeDetectionMode.ETag;

            SeedRemote("k1", "remote body");
            await engine.SyncNowAsync();

            SeedRemote("k1", "edited remotely");
            SyncResult second = await engine.SyncNowAsync();

            // The edit is missed — documents the limitation Last-Modified mode exists to address.
            Assert.False(second.Changed);
            Assert.Equal("remote body", repository.LoadAll().Single(n => n.SyncKey == "k1").Content);
        }

        /// <summary>
        /// The real cross-device delete path: notes are tombstoned remotely (not physically removed), so
        /// the receiving device sees them as Download-of-tombstone, not DeleteLocal. The bulk gate must
        /// still count these and prompt — otherwise a mass remote delete wipes local notes unconfirmed.
        /// </summary>
        [Fact]
        public async Task TombstoneDownloadsAreGatedAndCounted()
        {
            Save("k1", "one", "1");
            Save("k2", "two", "2");
            await engine.SyncNowAsync();

            // Another device deleted both notes → tombstones land on the remote with fresh ETags.
            SeedTombstone("k1");
            SeedTombstone("k2");
            SyncChangePlan? seen = null;
            engine.ConfirmBulkChanges = plan =>
            {
                seen = plan;
                return Task.FromResult(true);
            };

            SyncResult result = await engine.SyncNowAsync();

            Assert.True(result.Success);
            Assert.NotNull(seen);
            Assert.Equal(2, seen!.LocalDeleteCount);
            Assert.Equal(2, result.FilesDeleted);
            Assert.Empty(repository.LoadAll());
        }

        /// <summary>A declined tombstone-download bulk delete keeps the local notes intact.</summary>
        [Fact]
        public async Task DeclinedTombstoneDownloadKeepsNotes()
        {
            Save("k1", "one", "1");
            Save("k2", "two", "2");
            await engine.SyncNowAsync();

            SeedTombstone("k1");
            SeedTombstone("k2");
            engine.ConfirmBulkChanges = _ => Task.FromResult(false);

            SyncResult result = await engine.SyncNowAsync();

            Assert.Equal("delete-declined", result.Message);
            Assert.Equal(2, repository.LoadAll().Count);
        }

        /// <summary>
        /// Post-confirm guard on the Download path: a note edited locally while the confirm dialog is
        /// open must not be overwritten by the remote copy the pass planned to download.
        /// </summary>
        [Fact]
        public async Task EditDuringConfirmIsNotOverwrittenByDownload()
        {
            Save("k1", "one", "1");
            Save("k2", "two", "2");
            Save("k3", "local k3", "3");
            await engine.SyncNowAsync();          // upload all three so baselines exist

            // Another device deletes k1/k2 (tombstones, crossing the threshold) and edits k3 — all in
            // the same incoming batch the next pass will process.
            SeedTombstone("k1");
            SeedTombstone("k2");
            SeedRemote("k3", "remote edit of k3");

            engine.ConfirmBulkChanges = _ =>
            {
                // User edits k3 on the UI thread while the dialog is open.
                Save("k3", "edited while dialog open", "3");
                return Task.FromResult(true);
            };

            await engine.SyncNowAsync();

            AvaloniaNoteDocument? k3 = repository.LoadAll().FirstOrDefault(n => n.Id == "3");
            Assert.NotNull(k3);
            Assert.Equal("edited while dialog open", k3!.Content);
        }

        /// <summary>
        /// Post-confirm guard on the DeleteRemote path: if the user recreates a locally-deleted note
        /// while the confirm dialog is open, the pass must not tombstone it remotely (which would drop
        /// the resurrected note on the next convergence). The dialog here is tripped by two incoming
        /// tombstones (the gated, surprising kind), while k1's own outgoing DeleteRemote rides along —
        /// the user's recreation of k1 mid-dialog must still be honored at apply.
        /// </summary>
        [Fact]
        public async Task RecreateDuringConfirmIsNotTombstoned()
        {
            Save("k1", "one", "1");
            Save("k2", "two", "2");
            Save("k3", "three", "3");
            await engine.SyncNowAsync();          // upload all, establish baselines

            // User deletes k1 locally → next pass plans an outgoing DeleteRemote (no longer gated alone).
            repository.Delete("1");
            // Another device deletes k2 and k3 → two incoming tombstones, which DO trip the gate.
            SeedTombstone("k2");
            SeedTombstone("k3");

            engine.BulkDeleteThreshold = 2;
            engine.ConfirmBulkChanges = _ =>
            {
                // While the dialog is open, the user recreates k1.
                Save("k1", "recreated", "1");
                return Task.FromResult(true);
            };

            await engine.SyncNowAsync();

            // k1 was resurrected, so it must survive and re-upload rather than being tombstoned.
            AvaloniaNoteDocument? k1 = repository.LoadAll().FirstOrDefault(n => n.SyncKey == "k1");
            Assert.NotNull(k1);
            Assert.Equal("recreated", k1!.Content);

            await engine.SyncNowAsync();
            Assert.Equal("recreated", await ReadRemote("k1"));
        }

        /// <summary>
        /// Finding 7: the gate confirms only surprising incoming deletions, never the user's own outgoing
        /// ones. Deleting many notes locally must propagate as tombstones without a prompt — the user just
        /// made those deletions, so re-confirming them is pure alert fatigue.
        /// </summary>
        [Fact]
        public async Task OwnLocalDeletesDoNotTripTheGate()
        {
            Save("k1", "one", "1");
            Save("k2", "two", "2");
            Save("k3", "three", "3");
            await engine.SyncNowAsync();

            repository.Delete("1");
            repository.Delete("2");
            repository.Delete("3");

            engine.BulkDeleteThreshold = 2;
            bool prompted = false;
            engine.ConfirmBulkChanges = _ =>
            {
                prompted = true;
                return Task.FromResult(true);
            };

            SyncResult result = await engine.SyncNowAsync();

            Assert.False(prompted);                 // no confirmation for the user's own deletions
            Assert.True(result.Success);
            Assert.True(await IsRemoteTombstone("k1"));
            Assert.True(await IsRemoteTombstone("k2"));
            Assert.True(await IsRemoteTombstone("k3"));
        }

        /// <summary>
        /// Finding 6: while the confirm dialog is open, the WebDAV client is released and a fresh one is
        /// opened for the apply phase. With a counting factory, a gated pass creates two clients (initial
        /// + reopen); the apply still completes against the new client.
        /// </summary>
        [Fact]
        public async Task ClientIsReopenedAcrossConfirmDialog()
        {
            int created = 0;
            engine.Reconfigure(() => { created++; return client; }, string.Empty, TimeSpan.Zero);

            Save("k1", "one", "1");
            Save("k2", "two", "2");
            await engine.SyncNowAsync();            // one client; no deletions, no prompt
            Assert.Equal(1, created);

            SeedTombstone("k1");
            SeedTombstone("k2");
            engine.BulkDeleteThreshold = 2;
            engine.ConfirmBulkChanges = _ => Task.FromResult(true);

            created = 0;
            SyncResult result = await engine.SyncNowAsync();

            Assert.Equal(2, created);               // initial client + reopen after the dialog
            Assert.True(result.Success);
            Assert.Equal(2, result.FilesDeleted);   // both incoming tombstones applied locally
        }

        /// <summary>Overlapping passes serialize: the second returns busy rather than running concurrently.</summary>
        [Fact]
        public async Task OverlappingPassesSerialize()
        {
            Save("k1", "body", "1");
            Task<SyncResult> a = engine.SyncNowAsync();
            Task<SyncResult> b = engine.SyncNowAsync();
            SyncResult[] results = await Task.WhenAll(a, b);

            Assert.Contains(results, r => r.Message == "busy" || !r.Changed);
        }
    }
}
