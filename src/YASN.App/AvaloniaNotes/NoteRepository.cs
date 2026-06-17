using System.Text.Json;
using YASN.Infrastructure;

namespace YASN.AvaloniaNotes
{
    /// <summary>
    /// Persists Avalonia note metadata in notes.index.json and content in per-note Markdown files.
    /// </summary>
    public sealed class NoteRepository
    {
        private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
        private readonly string indexPath;
        private readonly string notesRoot;

        /// <summary>
        /// Raised after a note is saved, carrying the saved document. Used by the sync engine to
        /// enqueue the change.
        /// </summary>
        public event Action<AvaloniaNoteDocument>? NoteSaved;

        /// <summary>
        /// Raised after a note is deleted, carrying its id and sync key. Used by the sync engine to
        /// enqueue a tombstone.
        /// </summary>
        public event Action<int, string>? NoteDeleted;

        /// <summary>
        /// Initializes a repository rooted at the production app data directory.
        /// </summary>
        public NoteRepository()
            : this(AppPaths.DataDirectory)
        {
        }

        /// <summary>
        /// Initializes a repository rooted at a caller-provided data directory.
        /// </summary>
        /// <param name="dataRoot">The directory that contains notes.index.json and the notes folder.</param>
        public NoteRepository(string dataRoot)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(dataRoot);
            indexPath = Path.Combine(dataRoot, "notes.index.json");
            notesRoot = Path.Combine(dataRoot, "notes");
        }

        /// <summary>
        /// Loads all indexed notes with their Markdown content.
        /// </summary>
        /// <returns>The note list ordered by identifier.</returns>
        public IReadOnlyList<AvaloniaNoteDocument> LoadAll()
        {
            NoteIndexData index = LoadIndex();
            return index.Notes
                .OrderBy(note => note.Id)
                .Select(ToDocument)
                .ToList();
        }

        /// <summary>
        /// Loads notes marked open in the index.
        /// </summary>
        /// <returns>The open note list ordered by identifier.</returns>
        public IReadOnlyList<AvaloniaNoteDocument> LoadOpenNotes()
        {
            return LoadAll().Where(note => note.IsOpen).ToList();
        }

        /// <summary>
        /// Creates, saves, and returns a new note document.
        /// </summary>
        /// <returns>The newly created note.</returns>
        public AvaloniaNoteDocument CreateNote()
        {
            IReadOnlyList<AvaloniaNoteDocument> notes = LoadAll();
            int nextId = notes.Count == 0 ? 1 : notes.Max(note => note.Id) + 1;
            AvaloniaNoteDocument note = new AvaloniaNoteDocument { Id = nextId };
            Save(note);
            return note;
        }

        /// <summary>
        /// Materializes the remote side of a conflict as a second local note that shares the same
        /// <see cref="AvaloniaNoteDocument.SyncKey"/> as the local original but gets a fresh local id.
        /// The two rows together signal an unresolved conflict until the user deletes one.
        /// </summary>
        /// <param name="remote">The remote note to copy in (its <see cref="AvaloniaNoteDocument.SyncKey"/> is preserved).</param>
        /// <returns>The newly created conflict-copy note.</returns>
        public AvaloniaNoteDocument CreateConflictCopy(AvaloniaNoteDocument remote)
        {
            ArgumentNullException.ThrowIfNull(remote);

            IReadOnlyList<AvaloniaNoteDocument> notes = LoadAll();
            int nextId = notes.Count == 0 ? 1 : notes.Max(note => note.Id) + 1;
            remote.Id = nextId;
            Save(remote);
            return remote;
        }

        /// <summary>
        /// Saves one note and updates its metadata index entry.
        /// </summary>
        /// <param name="note">The note to persist.</param>
        public void Save(AvaloniaNoteDocument note)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(indexPath) ?? ".");
            Directory.CreateDirectory(notesRoot);

            File.WriteAllText(GetMarkdownPath(note.Id), note.Content);

            NoteIndexData index = LoadIndex();
            index.SchemaVersion = 5;
            index.Notes.RemoveAll(entry => entry.Id == note.Id);
            index.Notes.Add(ToEntry(note));
            index.Notes = index.Notes.OrderBy(entry => entry.Id).ToList();

            string json = JsonSerializer.Serialize(index, JsonOptions);
            File.WriteAllText(indexPath, json);

            NoteSaved?.Invoke(note);
        }

        /// <summary>
        /// Deletes one note's content file and removes its metadata index entry.
        /// </summary>
        /// <param name="noteId">The identifier of the note to delete.</param>
        public void Delete(int noteId)
        {
            string markdownPath = GetMarkdownPath(noteId);
            if (File.Exists(markdownPath))
            {
                File.Delete(markdownPath);
            }

            NoteIndexData index = LoadIndex();
            NoteIndexEntry? entry = index.Notes.FirstOrDefault(e => e.Id == noteId);
            if (entry is null)
            {
                return;
            }

            string syncKey = entry.SyncKey ?? string.Empty;
            index.Notes.RemoveAll(e => e.Id == noteId);
            index.SchemaVersion = 5;
            string json = JsonSerializer.Serialize(index, JsonOptions);
            File.WriteAllText(indexPath, json);

            NoteDeleted?.Invoke(noteId, syncKey);
        }

        private NoteIndexData LoadIndex()
        {
            if (!File.Exists(indexPath))
            {
                return new NoteIndexData();
            }

            string json = File.ReadAllText(indexPath);
            NoteIndexData? index = JsonSerializer.Deserialize<NoteIndexData>(json);
            index ??= new NoteIndexData();
            BackfillSyncKeys(index);
            return index;
        }

        /// <summary>
        /// Assigns sync keys to any pre-schema-5 entries that lack one and rewrites the index once.
        /// Idempotent: a fully-keyed index is left untouched.
        /// </summary>
        private void BackfillSyncKeys(NoteIndexData index)
        {
            bool changed = false;
            foreach (NoteIndexEntry entry in index.Notes)
            {
                if (string.IsNullOrWhiteSpace(entry.SyncKey))
                {
                    entry.SyncKey = Guid.NewGuid().ToString("N");
                    changed = true;
                }
            }

            if (!changed)
            {
                return;
            }

            index.SchemaVersion = 5;
            File.WriteAllText(indexPath, JsonSerializer.Serialize(index, JsonOptions));
        }

        private AvaloniaNoteDocument ToDocument(NoteIndexEntry entry)
        {
            return new AvaloniaNoteDocument
            {
                Id = entry.Id,
                SyncKey = string.IsNullOrWhiteSpace(entry.SyncKey) ? Guid.NewGuid().ToString("N") : entry.SyncKey,
                Content = LoadContent(entry.Id),
                StoredTitle = entry.Title,
                Left = entry.Left,
                Top = entry.Top,
                Width = entry.Width,
                Height = entry.Height,
                IsOpen = entry.IsOpen,
                Level = entry.Level,
                ShowInTaskbar = entry.ShowInTaskbar,
                ReminderAt = entry.ReminderAt,
                DisplayMode = entry.DisplayMode
            };
        }

        private string LoadContent(int noteId)
        {
            string path = GetMarkdownPath(noteId);
            return File.Exists(path) ? File.ReadAllText(path) : "# Untitled note";
        }

        private static NoteIndexEntry ToEntry(AvaloniaNoteDocument note)
        {
            return new NoteIndexEntry
            {
                Id = note.Id,
                SyncKey = string.IsNullOrWhiteSpace(note.SyncKey) ? Guid.NewGuid().ToString("N") : note.SyncKey,
                Title = string.IsNullOrWhiteSpace(note.StoredTitle) ? null : note.StoredTitle,
                Left = note.Left,
                Top = note.Top,
                Width = note.Width,
                Height = note.Height,
                IsOpen = note.IsOpen,
                Level = note.Level,
                ShowInTaskbar = note.ShowInTaskbar,
                ReminderAt = note.ReminderAt,
                DisplayMode = note.DisplayMode
            };
        }

        private string GetMarkdownPath(int noteId)
        {
            return Path.Combine(notesRoot, $"{noteId}.md");
        }
    }
}
