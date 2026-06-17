using YASN.SingleNote;

namespace YASN.Migration.Tests
{
    /// <summary>
    /// Verifies the phase-two single-note persistence behavior.
    /// </summary>
    public sealed class SingleNoteStoreTests : IDisposable
    {
        private readonly string root = Path.Combine(Path.GetTempPath(), "yasn-single-note-tests", Guid.NewGuid().ToString("N"));

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
        /// Loads a default note when no persisted note exists yet.
        /// </summary>
        [Fact]
        public void LoadReturnsDefaultNoteWhenFileDoesNotExist()
        {
            SingleNoteStore store = new SingleNoteStore(root);

            SingleNoteDocument note = store.Load();

            Assert.Equal(1, note.Id);
            Assert.Equal("# Untitled note", note.Content);
        }

        /// <summary>
        /// Persists edited note content and reloads it from disk.
        /// </summary>
        [Fact]
        public void SavePersistsContentForReload()
        {
            SingleNoteStore store = new SingleNoteStore(root);
            SingleNoteDocument note = new SingleNoteDocument(1, "# Saved\n\nbody");

            store.Save(note);
            SingleNoteDocument loaded = store.Load();

            Assert.Equal(note.Id, loaded.Id);
            Assert.Equal(note.Content, loaded.Content);
            Assert.True(File.Exists(Path.Combine(root, "notes", "1.md")));
        }
    }
}
