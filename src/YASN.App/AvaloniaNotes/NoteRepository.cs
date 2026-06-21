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
        public event Action<string, string>? NoteDeleted;

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
                .OrderBy(note => note.Id, StringComparer.Ordinal)
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
            string id = Guid.NewGuid().ToString("N");
            AvaloniaNoteDocument note = new AvaloniaNoteDocument { Id = id, SyncKey = id };
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

            remote.Id = Guid.NewGuid().ToString("N");
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
            index.SchemaVersion = 7;
            index.Notes.RemoveAll(entry => entry.Id == note.Id);
            index.Notes.Add(ToEntry(note));
            index.Notes = index.Notes.OrderBy(entry => entry.Id, StringComparer.Ordinal).ToList();

            string json = JsonSerializer.Serialize(index, JsonOptions);
            File.WriteAllText(indexPath, json);

            NoteSaved?.Invoke(note);
        }

        /// <summary>
        /// Deletes one note's content file and removes its metadata index entry.
        /// </summary>
        /// <param name="noteId">The identifier of the note to delete.</param>
        public void Delete(string noteId)
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
            index.SchemaVersion = 7;
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
        /// Assigns identifiers to any entries that lack one and rewrites the index once. Each note's
        /// id and sync key are both GUIDs and normally equal; a missing one is backfilled from the
        /// other (or a fresh GUID when both are absent). Idempotent: a fully-identified index is left
        /// untouched. The schema migrator normally does this first; this is the load-time safety net.
        /// </summary>
        private void BackfillSyncKeys(NoteIndexData index)
        {
            bool changed = false;
            foreach (NoteIndexEntry entry in index.Notes)
            {
                if (string.IsNullOrWhiteSpace(entry.SyncKey))
                {
                    entry.SyncKey = string.IsNullOrWhiteSpace(entry.Id) ? Guid.NewGuid().ToString("N") : entry.Id;
                    changed = true;
                }

                if (string.IsNullOrWhiteSpace(entry.Id))
                {
                    entry.Id = entry.SyncKey;
                    changed = true;
                }
            }

            if (!changed)
            {
                return;
            }

            index.SchemaVersion = 7;
            File.WriteAllText(indexPath, JsonSerializer.Serialize(index, JsonOptions));
        }

        private AvaloniaNoteDocument ToDocument(NoteIndexEntry entry)
        {
            string id = string.IsNullOrWhiteSpace(entry.Id) ? entry.SyncKey ?? Guid.NewGuid().ToString("N") : entry.Id;
            return new AvaloniaNoteDocument
            {
                Id = id,
                SyncKey = string.IsNullOrWhiteSpace(entry.SyncKey) ? id : entry.SyncKey,
                Content = LoadContent(id),
                StoredTitle = entry.Title,
                Left = entry.Left,
                Top = entry.Top,
                Width = entry.Width,
                Height = entry.Height,
                IsOpen = entry.IsOpen,
                Level = entry.Level,
                ShowInTaskbar = entry.ShowInTaskbar,
                ReminderAt = entry.ReminderAt,
                ContentModifiedAt = entry.ContentModifiedAt ?? ContentFileWriteTime(id),
                DisplayMode = entry.DisplayMode
            };
        }

        /// <summary>
        /// Returns the content file's last-write time in UTC, used to backfill
        /// <see cref="AvaloniaNoteDocument.ContentModifiedAt"/> for notes written before the field
        /// existed. Returns <see langword="null"/> when the file is missing so a freshly created,
        /// not-yet-saved note sorts last rather than to the epoch's opposite.
        /// </summary>
        private DateTimeOffset? ContentFileWriteTime(string noteId)
        {
            string path = GetMarkdownPath(noteId);
            return File.Exists(path) ? File.GetLastWriteTimeUtc(path) : null;
        }

        private string LoadContent(string noteId)
        {
            string path = GetMarkdownPath(noteId);
            return File.Exists(path) ? File.ReadAllText(path) : "# Untitled note";
        }

        private static NoteIndexEntry ToEntry(AvaloniaNoteDocument note)
        {
            return new NoteIndexEntry
            {
                Id = note.Id,
                SyncKey = string.IsNullOrWhiteSpace(note.SyncKey) ? note.Id : note.SyncKey,
                Title = string.IsNullOrWhiteSpace(note.StoredTitle) ? null : note.StoredTitle,
                Left = note.Left,
                Top = note.Top,
                Width = note.Width,
                Height = note.Height,
                IsOpen = note.IsOpen,
                Level = note.Level,
                ShowInTaskbar = note.ShowInTaskbar,
                ReminderAt = note.ReminderAt,
                ContentModifiedAt = note.ContentModifiedAt,
                DisplayMode = note.DisplayMode
            };
        }

        private string GetMarkdownPath(string noteId)
        {
            return Path.Combine(notesRoot, $"{noteId}.md");
        }
    }
}
