using YASN.AvaloniaNotes;
using YASN.Core;

namespace YASN.Migration.Tests
{
    /// <summary>
    /// Verifies multi-note metadata and content persistence for the Avalonia shell.
    /// </summary>
    public sealed class NoteRepositoryTests : IDisposable
    {
        private readonly string root = Path.Combine(Path.GetTempPath(), "yasn-note-repository-tests", Guid.NewGuid().ToString("N"));

        /// <summary>
        /// Deletes the temporary note root created for each test.
        /// </summary>
        public void Dispose()
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }

        /// <summary>
        /// New notes get distinct GUID identifiers, and the id doubles as the cross-device sync key
        /// (the two collapsed into one identifier).
        /// </summary>
        [Fact]
        public void CreateNoteAssignsDistinctGuidIdentifier()
        {
            NoteRepository repository = new NoteRepository(root);

            AvaloniaNoteDocument first = repository.CreateNote();
            AvaloniaNoteDocument second = repository.CreateNote();

            Assert.NotEqual(first.Id, second.Id);
            Assert.False(string.IsNullOrWhiteSpace(first.Id));
            Assert.Equal(first.Id, first.SyncKey);
            Assert.Equal(second.Id, second.SyncKey);
        }

        /// <summary>
        /// Saves note window metadata, content, and reminder timestamp.
        /// </summary>
        [Fact]
        public void SaveAndLoadRoundTripsMetadataContentAndReminder()
        {
            NoteRepository repository = new NoteRepository(root);
            DateTimeOffset reminder = DateTimeOffset.Parse("2026-06-16T10:30:00+00:00", System.Globalization.CultureInfo.InvariantCulture);
            AvaloniaNoteDocument note = new AvaloniaNoteDocument
            {
                Id = "7",
                Content = "# Title",
                Left = 10,
                Top = 20,
                Width = 300,
                Height = 240,
                IsOpen = true,
                Level = WindowLevel.TopMost,
                ShowInTaskbar = false,
                ReminderAt = reminder
            };

            repository.Save(note);
            AvaloniaNoteDocument loaded = repository.LoadAll().Single();

            Assert.Equal(note.Id, loaded.Id);
            Assert.Equal(note.Content, loaded.Content);
            Assert.Equal(note.Left, loaded.Left);
            Assert.Equal(note.Top, loaded.Top);
            Assert.Equal(note.Width, loaded.Width);
            Assert.Equal(note.Height, loaded.Height);
            Assert.True(loaded.IsOpen);
            Assert.Equal(WindowLevel.TopMost, loaded.Level);
            Assert.False(loaded.ShowInTaskbar);
            Assert.Equal(reminder, loaded.ReminderAt);
        }

        /// <summary>
        /// Round-trips the editor display mode through the index.
        /// </summary>
        [Fact]
        public void SaveAndLoadRoundTripsDisplayMode()
        {
            NoteRepository repository = new NoteRepository(root);
            AvaloniaNoteDocument note = new AvaloniaNoteDocument
            {
                Id = "3",
                Content = "# Mode",
                DisplayMode = EditorDisplayMode.TextOnly
            };

            repository.Save(note);
            AvaloniaNoteDocument loaded = repository.LoadAll().Single();

            Assert.Equal(EditorDisplayMode.TextOnly, loaded.DisplayMode);
        }

        /// <summary>
        /// A stored title round-trips through the index.
        /// </summary>
        [Fact]
        public void SaveAndLoadRoundTripsStoredTitle()
        {
            NoteRepository repository = new NoteRepository(root);
            AvaloniaNoteDocument note = new AvaloniaNoteDocument
            {
                Id = "4",
                Content = "# Heading from content",
                StoredTitle = "My custom title"
            };

            repository.Save(note);
            AvaloniaNoteDocument loaded = repository.LoadAll().Single();

            Assert.Equal("My custom title", loaded.StoredTitle);
            Assert.Equal("My custom title", loaded.Title);
        }

        /// <summary>
        /// With no stored title the display title falls back to the content heading.
        /// </summary>
        [Fact]
        public void TitleFallsBackToContentWhenNotStored()
        {
            NoteRepository repository = new NoteRepository(root);
            AvaloniaNoteDocument note = new AvaloniaNoteDocument
            {
                Id = "5",
                Content = "# Derived heading"
            };

            repository.Save(note);
            AvaloniaNoteDocument loaded = repository.LoadAll().Single();

            Assert.Null(loaded.StoredTitle);
            Assert.Equal("Derived heading", loaded.Title);
        }

        /// <summary>
        /// Deletes one note's content and index entry while leaving others intact.
        /// </summary>
        [Fact]
        public void DeleteRemovesContentAndIndexEntry()
        {
            NoteRepository repository = new NoteRepository(root);
            AvaloniaNoteDocument first = repository.CreateNote();
            AvaloniaNoteDocument second = repository.CreateNote();
            string firstPath = Path.Combine(root, "notes", $"{first.Id}.md");

            repository.Delete(first.Id);

            Assert.False(File.Exists(firstPath));
            IReadOnlyList<AvaloniaNoteDocument> remaining = repository.LoadAll();
            Assert.Single(remaining);
            Assert.Equal(second.Id, remaining[0].Id);
        }

        /// <summary>
        /// Deleting an unknown note identifier is a no-op.
        /// </summary>
        [Fact]
        public void DeleteUnknownIdentifierLeavesNotesUntouched()
        {
            NoteRepository repository = new NoteRepository(root);
            repository.CreateNote();

            repository.Delete("999");

            Assert.Single(repository.LoadAll());
        }

        /// <summary>
        /// A sole note is stored under its sync key, not its id, so local and remote naming unify even
        /// when the two identifiers differ (a kept conflict copy).
        /// </summary>
        [Fact]
        public void SoleNoteIsStoredUnderSyncKey()
        {
            NoteRepository repository = new NoteRepository(root);
            AvaloniaNoteDocument note = new AvaloniaNoteDocument { Id = "local-id", SyncKey = "shared-key", Content = "# Body" };

            repository.Save(note);

            Assert.True(File.Exists(Path.Combine(root, "notes", "shared-key.md")));
            Assert.False(File.Exists(Path.Combine(root, "notes", "local-id.md")));
            Assert.Equal("# Body", repository.LoadAll().Single().Content);
            Assert.Equal(Path.Combine(root, "notes", "shared-key.md"), repository.GetContentFilePath(note));
        }

        /// <summary>
        /// A conflict pair (two rows sharing one sync key) stores each row under its own id, since the
        /// shared sync key cannot name both files; deleting one collapses the survivor back to the
        /// sync-key name.
        /// </summary>
        [Fact]
        public void ConflictPairUsesIdNamesThenCollapsesOnDelete()
        {
            NoteRepository repository = new NoteRepository(root);
            AvaloniaNoteDocument original = new AvaloniaNoteDocument { Id = "k", SyncKey = "k", Content = "original" };
            repository.Save(original);

            // Materialize the remote side of a conflict: same sync key, fresh id.
            AvaloniaNoteDocument copy = new AvaloniaNoteDocument { Id = "k", SyncKey = "k", Content = "remote copy" };
            repository.CreateConflictCopy(copy);
            string copyId = copy.Id;

            Assert.NotEqual("k", copyId);
            Assert.True(File.Exists(Path.Combine(root, "notes", "k.md")));        // original at <SyncKey>.md
            Assert.True(File.Exists(Path.Combine(root, "notes", $"{copyId}.md"))); // copy at <Id>.md
            Assert.Equal(2, repository.LoadAll().Count(n => n.SyncKey == "k"));

            // Delete the copy: the survivor stays/returns to the sync-key name.
            repository.Delete(copyId);

            Assert.False(File.Exists(Path.Combine(root, "notes", $"{copyId}.md")));
            Assert.True(File.Exists(Path.Combine(root, "notes", "k.md")));
            Assert.Equal("original", repository.LoadAll().Single(n => n.SyncKey == "k").Content);
        }
    }
}
