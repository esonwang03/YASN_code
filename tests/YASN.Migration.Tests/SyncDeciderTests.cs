using YASN.Infrastructure.Sync;

namespace YASN.Migration.Tests
{
    /// <summary>
    /// Exhaustively covers the pure three-way decision matrix in <see cref="SyncDecider"/>.
    /// </summary>
    public sealed class SyncDeciderTests
    {
        private static SyncBaseline Base(string? localHash, string? remoteETag, bool deleted = false) =>
            new SyncBaseline("k", localHash, remoteETag, "notes/k.json", DateTimeOffset.UnixEpoch, deleted);

        /// <summary>Nothing anywhere is a no-op.</summary>
        [Fact]
        public void NothingIsNone() => Assert.Equal(SyncAction.None, SyncDecider.Decide(null, null, null));

        /// <summary>A baseline with neither side present is dropped.</summary>
        [Fact]
        public void StaleBaselineDropped() => Assert.Equal(SyncAction.DropBaseline, SyncDecider.Decide(null, null, Base("h", "e")));

        /// <summary>Local-only with no baseline uploads.</summary>
        [Fact]
        public void NewLocalUploads() => Assert.Equal(SyncAction.Upload, SyncDecider.Decide("h", null, null));

        /// <summary>Remote-only with no baseline downloads.</summary>
        [Fact]
        public void NewRemoteDownloads() => Assert.Equal(SyncAction.Download, SyncDecider.Decide(null, "e", null));

        /// <summary>Remote vanished while local is unchanged deletes locally.</summary>
        [Fact]
        public void RemoteDeletedLocalUnchangedDeletesLocal() =>
            Assert.Equal(SyncAction.DeleteLocal, SyncDecider.Decide("h", null, Base("h", "e")));

        /// <summary>Remote vanished while local changed needs conflict comparison.</summary>
        [Fact]
        public void RemoteDeletedLocalChangedCompares() =>
            Assert.Equal(SyncAction.CompareForConflict, SyncDecider.Decide("h2", null, Base("h", "e")));

        /// <summary>Local vanished while remote is unchanged deletes remotely.</summary>
        [Fact]
        public void LocalDeletedRemoteUnchangedDeletesRemote() =>
            Assert.Equal(SyncAction.DeleteRemote, SyncDecider.Decide(null, "e", Base("h", "e")));

        /// <summary>Local vanished while remote changed needs conflict comparison.</summary>
        [Fact]
        public void LocalDeletedRemoteChangedCompares() =>
            Assert.Equal(SyncAction.CompareForConflict, SyncDecider.Decide(null, "e2", Base("h", "e")));

        /// <summary>
        /// A key already tombstoned (baseline.Deleted) whose remote tombstone is unchanged is settled:
        /// returning DeleteRemote here would re-PUT the tombstone forever (the re-tombstone loop).
        /// </summary>
        [Fact]
        public void AlreadyTombstonedUnchangedIsNone() =>
            Assert.Equal(SyncAction.None, SyncDecider.Decide(null, "e", Base(null, "e", deleted: true)));

        /// <summary>A not-yet-tombstoned local deletion still writes the tombstone once.</summary>
        [Fact]
        public void LocalDeletedNotYetTombstonedDeletesRemote() =>
            Assert.Equal(SyncAction.DeleteRemote, SyncDecider.Decide(null, "e", Base("h", "e", deleted: false)));

        /// <summary>Both unchanged since baseline is a no-op.</summary>
        [Fact]
        public void BothUnchangedIsNone() =>
            Assert.Equal(SyncAction.None, SyncDecider.Decide("h", "e", Base("h", "e")));

        /// <summary>Only local changed uploads.</summary>
        [Fact]
        public void OnlyLocalChangedUploads() =>
            Assert.Equal(SyncAction.Upload, SyncDecider.Decide("h2", "e", Base("h", "e")));

        /// <summary>Only remote changed downloads.</summary>
        [Fact]
        public void OnlyRemoteChangedDownloads() =>
            Assert.Equal(SyncAction.Download, SyncDecider.Decide("h", "e2", Base("h", "e")));

        /// <summary>Both changed needs conflict comparison.</summary>
        [Fact]
        public void BothChangedCompares() =>
            Assert.Equal(SyncAction.CompareForConflict, SyncDecider.Decide("h2", "e2", Base("h", "e")));

        /// <summary>Both present without a baseline (first contact) needs comparison.</summary>
        [Fact]
        public void BothPresentNoBaselineCompares() =>
            Assert.Equal(SyncAction.CompareForConflict, SyncDecider.Decide("h", "e", null));
    }
}
