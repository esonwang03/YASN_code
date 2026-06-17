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
        /// Creates notes with stable incrementing identifiers.
        /// </summary>
        [Fact]
        public void CreateNoteAssignsNextIdentifier()
        {
            NoteRepository repository = new NoteRepository(root);

            AvaloniaNoteDocument first = repository.CreateNote();
            AvaloniaNoteDocument second = repository.CreateNote();

            Assert.Equal(1, first.Id);
            Assert.Equal(2, second.Id);
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
                Id = 7,
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
                Id = 3,
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
                Id = 4,
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
                Id = 5,
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

            repository.Delete(999);

            Assert.Single(repository.LoadAll());
        }
    }
}
