using System.Globalization;
using YASN.Infrastructure;

namespace YASN.SingleNote
{
    /// <summary>
    /// Loads and saves the single note document to the YASN data directory.
    /// </summary>
    public sealed class SingleNoteStore
    {
        private const int SingleNoteId = 1;
        private readonly string notesRoot;

        /// <summary>
        /// Initializes a store rooted at the production app data directory.
        /// </summary>
        public SingleNoteStore()
            : this(AppPaths.DataDirectory)
        {
        }

        /// <summary>
        /// Initializes a store rooted at a caller-provided data directory.
        /// </summary>
        /// <param name="dataRoot">The directory that contains the notes folder.</param>
        public SingleNoteStore(string dataRoot)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(dataRoot);
            notesRoot = Path.Combine(dataRoot, "notes");
        }

        /// <summary>
        /// Loads the single note, returning a default document when it has not been saved.
        /// </summary>
        /// <returns>The persisted or default single note.</returns>
        public SingleNoteDocument Load()
        {
            string path = GetPath(SingleNoteId);
            if (!File.Exists(path))
            {
                return new SingleNoteDocument(SingleNoteId, "# Untitled note");
            }

            string content = File.ReadAllText(path);
            return new SingleNoteDocument(SingleNoteId, content);
        }

        /// <summary>
        /// Saves the single note content to disk.
        /// </summary>
        /// <param name="note">The note document to save.</param>
        public void Save(SingleNoteDocument note)
        {
            Directory.CreateDirectory(notesRoot);
            File.WriteAllText(GetPath(note.Id), note.Content);
        }

        private string GetPath(int noteId)
        {
            return Path.Combine(notesRoot, string.Create(CultureInfo.InvariantCulture, $"{noteId}.md"));
        }
    }
}
