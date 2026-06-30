using System.Text.Json;
using YASN.Infrastructure;

namespace YASN.AvaloniaNotes
{
    /// <summary>
    /// Persists Avalonia note metadata in notes.index.json and content in per-note Markdown files.
    /// </summary>
    public sealed class NoteRepository
    {
        private readonly string indexPath;
        private readonly string notesRoot;

        /// <summary>
        /// The current index schema version the repository writes. Version 8 introduced sync-key-based
        /// content file naming.
        /// </summary>
        private const int CurrentSchemaVersion = 8;

        /// <summary>
        /// The schema version at which content files began to be named by sync key. An index loaded
        /// below this version has its content files migrated on load.
        /// </summary>
        private const int ContentNamingSchemaVersion = 8;

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
            Dictionary<string, int> rowsByKey = CountRowsBySyncKey(index);
            return index.Notes
                .OrderBy(note => note.Id, StringComparer.Ordinal)
                .Select(entry => ToDocument(entry, rowsByKey))
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
        /// Saves one note and updates its metadata index entry. The content file is named by the
        /// note's <see cref="AvaloniaNoteDocument.SyncKey"/> when the key has a single local row, and
        /// by its <see cref="AvaloniaNoteDocument.Id"/> while the key has multiple rows (a conflict).
        /// </summary>
        /// <param name="note">The note to persist.</param>
        public void Save(AvaloniaNoteDocument note)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(indexPath) ?? ".");
            Directory.CreateDirectory(notesRoot);

            NoteIndexData index = LoadIndex();
            index.SchemaVersion = CurrentSchemaVersion;
            index.Notes.RemoveAll(entry => entry.Id == note.Id);
            index.Notes.Add(ToEntry(note));
            index.Notes = index.Notes.OrderBy(entry => entry.Id, StringComparer.Ordinal).ToList();

            string syncKey = string.IsNullOrWhiteSpace(note.SyncKey) ? note.Id : note.SyncKey;
            int rowsForKey = index.Notes.Count(entry => SyncKeyOf(entry) == syncKey);
            string fileKey = ResolveFileKey(note.Id, syncKey, rowsForKey);
            File.WriteAllText(GetMarkdownPath(fileKey), note.Content);

            string json = JsonSerializer.Serialize(index, NoteIndexJsonContext.Default.NoteIndexData);
            File.WriteAllText(indexPath, json);

            // Reconcile every row of this key so a save that split a sole row into a conflict pair moves
            // the pre-existing sibling from its sync-key name to its id name. Renames only; never deletes
            // another note's file (the prior bug). The common single-row, id==syncKey case is a no-op.
            ReconcileContentFileNamesForKey(syncKey, index);

            NoteSaved?.Invoke(note);
        }

        /// <summary>
        /// Deletes one note's content file and removes its metadata index entry.
        /// </summary>
        /// <param name="noteId">The identifier of the note to delete.</param>
        public void Delete(string noteId)
        {
            NoteIndexData index = LoadIndex();
            NoteIndexEntry? entry = index.Notes.FirstOrDefault(e => e.Id == noteId);
            if (entry is null)
            {
                return;
            }

            string syncKey = SyncKeyOf(entry);
            int rowsBefore = index.Notes.Count(e => SyncKeyOf(e) == syncKey);
            string fileKey = ResolveFileKey(entry.Id, syncKey, rowsBefore);
            string markdownPath = GetMarkdownPath(fileKey);
            if (File.Exists(markdownPath))
            {
                File.Delete(markdownPath);
            }

            index.Notes.RemoveAll(e => e.Id == noteId);
            index.SchemaVersion = CurrentSchemaVersion;
            string json = JsonSerializer.Serialize(index, NoteIndexJsonContext.Default.NoteIndexData);
            File.WriteAllText(indexPath, json);

            // Deleting one row of a conflict pair leaves a sole survivor whose file must collapse from
            // its id name to its sync-key name (the collapse case).
            ReconcileContentFileNamesForKey(syncKey, index);

            NoteDeleted?.Invoke(noteId, syncKey);
        }

        /// <summary>
        /// Returns the absolute path of the note's Markdown content file, resolving the sync-key vs.
        /// id naming rule against the current index. Used by callers that open the raw file (e.g. the
        /// external-editor action) so they do not duplicate the naming logic.
        /// </summary>
        /// <param name="note">The note whose content path is wanted.</param>
        /// <returns>The absolute content file path.</returns>
        public string GetContentFilePath(AvaloniaNoteDocument note)
        {
            ArgumentNullException.ThrowIfNull(note);

            string syncKey = string.IsNullOrWhiteSpace(note.SyncKey) ? note.Id : note.SyncKey;
            NoteIndexData index = LoadIndex();
            int rowsForKey = index.Notes.Count(entry => SyncKeyOf(entry) == syncKey);
            // A not-yet-indexed note counts as its own sole row.
            string fileKey = ResolveFileKey(note.Id, syncKey, Math.Max(1, rowsForKey));
            return GetMarkdownPath(fileKey);
        }

        private NoteIndexData LoadIndex()
        {
            if (!File.Exists(indexPath))
            {
                return new NoteIndexData();
            }

            string json = File.ReadAllText(indexPath);
            NoteIndexData? index = JsonSerializer.Deserialize(json, NoteIndexJsonContext.Default.NoteIndexData);
            index ??= new NoteIndexData();
            BackfillSyncKeys(index);
            MigrateContentFileNames(index);
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

            index.SchemaVersion = CurrentSchemaVersion;
            File.WriteAllText(indexPath, JsonSerializer.Serialize(index, NoteIndexJsonContext.Default.NoteIndexData));
        }

        /// <summary>
        /// One-time migration that renames content files from the legacy id-based scheme to the
        /// sync-key-based scheme. Runs on load when the stored schema predates version 8. Applies the
        /// same naming invariant the repository now maintains: a key with a single row is stored under
        /// its sync key, a conflict pair under each row's id. Idempotent and non-destructive — it only
        /// moves a file when the source exists and the destination is absent.
        /// </summary>
        private void MigrateContentFileNames(NoteIndexData index)
        {
            if (index.SchemaVersion >= ContentNamingSchemaVersion)
            {
                return;
            }

            foreach (string syncKey in index.Notes.Select(SyncKeyOf).Distinct(StringComparer.Ordinal))
            {
                ReconcileContentFileNamesForKey(syncKey, index);
            }

            index.SchemaVersion = CurrentSchemaVersion;
            File.WriteAllText(indexPath, JsonSerializer.Serialize(index, NoteIndexJsonContext.Default.NoteIndexData));
        }

        /// <summary>
        /// Enforces the content-file naming invariant for every row sharing one sync key: a sole row
        /// is stored as <c>&lt;syncKey&gt;.md</c>, each row of a conflict pair as <c>&lt;id&gt;.md</c>.
        /// Moves a file only when its desired name is free and its current (alternate) name exists, so
        /// the operation is idempotent and never overwrites content. The common case where a note's id
        /// equals its sync key is a no-op.
        /// </summary>
        /// <param name="syncKey">The sync key whose rows to reconcile.</param>
        /// <param name="index">The loaded index supplying the row-set for the key.</param>
        private void ReconcileContentFileNamesForKey(string syncKey, NoteIndexData index)
        {
            List<NoteIndexEntry> rows = index.Notes.Where(entry => SyncKeyOf(entry) == syncKey).ToList();
            foreach (NoteIndexEntry entry in rows)
            {
                string desired = ResolveFileKey(entry.Id, syncKey, rows.Count);
                string alternate = desired == entry.Id ? syncKey : entry.Id;
                if (desired == alternate)
                {
                    continue;
                }

                string desiredPath = GetMarkdownPath(desired);
                string alternatePath = GetMarkdownPath(alternate);
                if (File.Exists(alternatePath) && !File.Exists(desiredPath))
                {
                    File.Move(alternatePath, desiredPath);
                }
                else if (File.Exists(alternatePath) && File.Exists(desiredPath))
                {
                    AppLogger.Warn($"Note '{entry.Id}' has both '{alternate}.md' and '{desired}.md'; leaving both and reading '{desired}.md'.");
                }
            }
        }

        private AvaloniaNoteDocument ToDocument(NoteIndexEntry entry, IReadOnlyDictionary<string, int> rowsByKey)
        {
            string id = string.IsNullOrWhiteSpace(entry.Id) ? entry.SyncKey ?? Guid.NewGuid().ToString("N") : entry.Id;
            string syncKey = string.IsNullOrWhiteSpace(entry.SyncKey) ? id : entry.SyncKey;
            int rowsForKey = rowsByKey.TryGetValue(syncKey, out int count) ? count : 1;
            string fileKey = ResolveFileKey(id, syncKey, rowsForKey);
            return new AvaloniaNoteDocument
            {
                Id = id,
                SyncKey = syncKey,
                Content = LoadContent(fileKey),
                StoredTitle = entry.Title,
                Left = entry.Left,
                Top = entry.Top,
                Width = entry.Width,
                Height = entry.Height,
                IsOpen = entry.IsOpen,
                Level = entry.Level,
                ShowInTaskbar = entry.ShowInTaskbar,
                ReminderAt = entry.ReminderAt,
                ContentModifiedAt = entry.ContentModifiedAt ?? ContentFileWriteTime(fileKey),
                DisplayMode = entry.DisplayMode
            };
        }

        /// <summary>
        /// Counts how many index rows share each sync key, so the naming rule can distinguish a sole
        /// row (named by sync key) from a conflict pair (named by id).
        /// </summary>
        private static Dictionary<string, int> CountRowsBySyncKey(NoteIndexData index)
        {
            Dictionary<string, int> counts = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (NoteIndexEntry entry in index.Notes)
            {
                string key = SyncKeyOf(entry);
                counts[key] = counts.TryGetValue(key, out int existing) ? existing + 1 : 1;
            }

            return counts;
        }

        /// <summary>
        /// Returns the sync key for an index entry, falling back to its id when the key is unset.
        /// </summary>
        private static string SyncKeyOf(NoteIndexEntry entry)
        {
            return string.IsNullOrWhiteSpace(entry.SyncKey) ? entry.Id : entry.SyncKey;
        }

        /// <summary>
        /// Resolves the content file key for one note: its sync key when it is the only row for that
        /// key, otherwise its id (a conflict pair, where the shared sync key cannot name both files).
        /// A total function of the row-set — never a heuristic.
        /// </summary>
        private static string ResolveFileKey(string id, string syncKey, int rowsForKey)
        {
            return rowsForKey <= 1 ? syncKey : id;
        }

        /// <summary>
        /// Returns the content file's last-write time in UTC, used to backfill
        /// <see cref="AvaloniaNoteDocument.ContentModifiedAt"/> for notes written before the field
        /// existed. Returns <see langword="null"/> when the file is missing so a freshly created,
        /// not-yet-saved note sorts last rather than to the epoch's opposite.
        /// </summary>
        private DateTimeOffset? ContentFileWriteTime(string fileKey)
        {
            string path = GetMarkdownPath(fileKey);
            return File.Exists(path) ? File.GetLastWriteTimeUtc(path) : null;
        }

        private string LoadContent(string fileKey)
        {
            string path = GetMarkdownPath(fileKey);
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

        private string GetMarkdownPath(string fileKey)
        {
            return Path.Combine(notesRoot, $"{fileKey}.md");
        }
    }
}
