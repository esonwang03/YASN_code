using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using YASN.Infrastructure.Logging;
using YASN.App.Settings;

namespace YASN.App.Notes
{
    public class NoteManager
    {
        private static NoteManager _instance;
        private static readonly Lock _lock = new Lock();

        private const string LegacySaveFileName = "notes.json";
        private const int CurrentSchemaVersion = 2;
        public const double DefaultNoteWidth = 760;
        public const double DefaultNoteHeight = 460;

        private static string IndexFilePath => AppPaths.NotesIndexPath;

        public static NoteManager Instance
        {
            get
            {
                if (_instance != null) return _instance;
                lock (_lock)
                {
                    _instance ??= new NoteManager();
                }

                return _instance;
            }
        }

        public ObservableCollection<NoteData> Notes { get; }

        private int _nextId = 1;

        private NoteManager()
        {
            Notes = new ObservableCollection<NoteData>();
            Load();
        }

        public NoteData CreateNote(WindowLevel level = WindowLevel.Normal)
        {
            NoteData note = new NoteData
            {
                Id = _nextId++,
                Title = $"Note #{_nextId - 1}",
                Content = "Double Right Click to enter your markdown here...",
                Level = level,
                Left = 100,
                Top = 100,
                Width = DefaultNoteWidth,
                Height = DefaultNoteHeight,
                IsOpen = false
            };

            Notes.Add(note);
            Save();
            return note;
        }

        public void UpdateNote(NoteData note)
        {
            if (note != null)
            {
                Save();
            }
        }

        public void DeleteNote(NoteData note)
        {
            if (note == null)
            {
                return;
            }

            Notes.Remove(note);
            TryDeleteFile(AppPaths.GetNoteMarkdownPath(note.Id));
            TryDeleteFile(AppPaths.GetNoteHtmlCachePath(note.Id));
            TryDeleteDirectory(Path.Combine(AppPaths.NoteAssetsRoot, note.Id.ToString(CultureInfo.InvariantCulture)));
            TryDeleteDirectory(Path.Combine(AppPaths.NoteBackgroundsRoot, note.Id.ToString(CultureInfo.InvariantCulture)));
            Save();
        }

        private void Save()
        {
            try
            {
                WriteIndexFile();
                WriteMarkdownFiles();

                // AppLogger.Debug($"Saved {Notes.Count} notes to {IndexFilePath} (schema v{CurrentSchemaVersion})");
            }
            catch (IOException ex)
            {
                AppLogger.Warn($"Failed to save notes: {ex.Message}");
            }
            catch (UnauthorizedAccessException ex)
            {
                AppLogger.Warn($"Failed to save notes: {ex.Message}");
            }
            catch (JsonException ex)
            {
                AppLogger.Warn($"Failed to save notes: {ex.Message}");
            }
            catch (NotSupportedException ex)
            {
                AppLogger.Warn($"Failed to save notes: {ex.Message}");
            }
        }

        private void Load()
        {
            try
            {
                Notes.Clear();
                _nextId = 1;

                if (!File.Exists(IndexFilePath))
                {
                    TryMigrateLegacyStorage();
                }

                if (!File.Exists(IndexFilePath))
                {
                    AppLogger.Warn($"Notes index not found at {IndexFilePath}");
                    return;
                }

                string json = File.ReadAllText(IndexFilePath);
                var (items, schemaVersion) = ParseIndexItems(json);
                if (items.Length == 0 && schemaVersion == 0)
                {
                    return;
                }

                bool shouldRewrite = schemaVersion < CurrentSchemaVersion;
                foreach (NoteMetadataDto item in items)
                {
                    string content = ReadMarkdownContent(item.Id);
                    if (string.IsNullOrEmpty(content) && !string.IsNullOrEmpty(item.Content))
                    {
                        content = NormalizeLegacyContent(item.Content);
                        shouldRewrite = true;
                    }

                    NoteData note = new NoteData
                    {
                        Id = item.Id,
                        Title = item.Title ?? $"Note #{item.Id}",
                        Content = content,
                        Level = item.Level,
                        Left = item.Left,
                        Top = item.Top,
                        Width = item.Width,
                        Height = item.Height,
                        IsDarkMode = item.IsDarkMode,
                        LastEditorDisplayMode = EditorDisplayModeSettings.TryParseValue(item.LastEditorDisplayMode, out var loadedMode)
                            ? loadedMode
                            : null,
                        TitleBarColor = item.TitleBarColor,
                        BackgroundImagePath = item.BackgroundImagePath,
                        BackgroundImageOpacity = item.BackgroundImageOpacity,
                        IsOpen = item.IsOpen
                    };

                    Notes.Add(note);
                    if (item.Id >= _nextId)
                    {
                        _nextId = item.Id + 1;
                    }
                }

                if (shouldRewrite)
                {
                    Save();
                }

                AppLogger.Debug($"Loaded {Notes.Count} notes from {IndexFilePath}");
            }
            catch (IOException ex)
            {
                AppLogger.Debug($"Failed to load notes: {ex.Message}");
            }
            catch (UnauthorizedAccessException ex)
            {
                AppLogger.Debug($"Failed to load notes: {ex.Message}");
            }
            catch (JsonException ex)
            {
                AppLogger.Debug($"Failed to load notes: {ex.Message}");
            }
            catch (NotSupportedException ex)
            {
                AppLogger.Debug($"Failed to load notes: {ex.Message}");
            }
        }

        public void RestoreOpenNotes()
        {
            List<NoteData> openNotes = Notes.Where(n => n.IsOpen).ToList();
            foreach (NoteData note in openNotes)
            {
                try
                {
                    FloatingWindow window = new FloatingWindow(note);
                    window.Show();
                }
                catch (InvalidOperationException ex)
                {
                    AppLogger.Debug($"Failed to restore note window {note.Id}: {ex.Message}");
                }
                catch (Win32Exception ex)
                {
                    AppLogger.Debug($"Failed to restore note window {note.Id}: {ex.Message}");
                }
            }
        }

        public void ReloadNotes()
        {
            foreach (NoteData note in Notes.Where(n => n.IsOpen).ToList())
            {
                note.Window?.Close();
            }

            Notes.Clear();
            Load();
            RestoreOpenNotes();
        }

        /// <summary>
        /// Rebuilds the note index so every local markdown file has a corresponding index entry.
        /// </summary>
        /// <returns>The repair result describing whether the index changed.</returns>
        public NoteIndexRepairResult RepairIndexFromLocalMarkdownFiles()
        {
            Dictionary<int, NoteData> existingNotes = Notes.ToDictionary(note => note.Id);
            List<NoteData> recoveredNotes = new List<NoteData>();

            foreach (int noteId in EnumerateLocalMarkdownNoteIds())
            {
                if (existingNotes.ContainsKey(noteId))
                {
                    continue;
                }

                recoveredNotes.Add(CreateRecoveredNote(noteId));
            }

            if (recoveredNotes.Count == 0)
            {
                return new NoteIndexRepairResult
                {
                    WasChanged = false,
                    AddedNoteCount = 0,
                    Message = "note.index.json is already aligned with local markdown files."
                };
            }

            foreach (NoteData note in recoveredNotes.OrderBy(note => note.Id))
            {
                Notes.Add(note);
                if (note.Id >= _nextId)
                {
                    _nextId = note.Id + 1;
                }
            }

            WriteIndexFile();

            return new NoteIndexRepairResult
            {
                WasChanged = true,
                AddedNoteCount = recoveredNotes.Count,
                Message = $"Rebuilt note index by restoring {recoveredNotes.Count} local markdown file(s)."
            };
        }

        private void TryMigrateLegacyStorage()
        {
            string[] legacyCandidates = new[]
            {
                AppPaths.LegacyNotesFilePath,
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, LegacySaveFileName)
            };

            string? legacyPath = legacyCandidates.FirstOrDefault(File.Exists);
            if (string.IsNullOrEmpty(legacyPath))
            {
                return;
            }

            try
            {
                string legacyJson = File.ReadAllText(legacyPath);
                LegacyNoteDataDto[]? legacyItems = JsonSerializer.Deserialize<LegacyNoteDataDto[]>(legacyJson);
                if (legacyItems == null || legacyItems.Length == 0)
                {
                    return;
                }

                string backupPath = Path.Combine(AppPaths.DataDirectory, "notes.v1.backup.json");
                if (!File.Exists(backupPath))
                {
                    WriteTextFile(backupPath, legacyJson);
                }

                Notes.Clear();
                foreach (LegacyNoteDataDto item in legacyItems)
                {
                    Notes.Add(new NoteData
                    {
                        Id = item.Id,
                        Title = item.Title ?? $"Note #{item.Id}",
                        Content = NormalizeLegacyContent(item.Content),
                        Level = item.Level,
                        Left = item.Left,
                        Top = item.Top,
                        Width = item.Width,
                        Height = item.Height,
                        IsDarkMode = item.IsDarkMode,
                        LastEditorDisplayMode = null,
                        TitleBarColor = item.TitleBarColor,
                        BackgroundImagePath = item.BackgroundImagePath,
                        BackgroundImageOpacity = item.BackgroundImageOpacity,
                        IsOpen = item.IsOpen
                    });
                }

                _nextId = Notes.Count > 0 ? Notes.Max(n => n.Id) + 1 : 1;
                Save();
                AppLogger.Debug($"Migrated {Notes.Count} legacy notes from {legacyPath}");
            }
            catch (IOException ex)
            {
                AppLogger.Debug($"Failed to migrate legacy notes: {ex.Message}");
            }
            catch (UnauthorizedAccessException ex)
            {
                AppLogger.Debug($"Failed to migrate legacy notes: {ex.Message}");
            }
            catch (JsonException ex)
            {
                AppLogger.Debug($"Failed to migrate legacy notes: {ex.Message}");
            }
            catch (NotSupportedException ex)
            {
                AppLogger.Debug($"Failed to migrate legacy notes: {ex.Message}");
            }
        }

        private static (NoteMetadataDto[] items, int schemaVersion) ParseIndexItems(string json)
        {
            try
            {
                using JsonDocument doc = JsonDocument.Parse(json);
                if (doc.RootElement.ValueKind == JsonValueKind.Array)
                {
                    var notes = JsonSerializer.Deserialize<NoteMetadataDto[]>(json);
                    return (notes ?? Array.Empty<NoteMetadataDto>(), 1);
                }

                if (doc.RootElement.ValueKind != JsonValueKind.Object)
                {
                    return (Array.Empty<NoteMetadataDto>(), 0);
                }

                NoteIndexDto? index = JsonSerializer.Deserialize<NoteIndexDto>(json);
                return (index?.Notes ?? Array.Empty<NoteMetadataDto>(), index?.SchemaVersion ?? 1);
            }
            catch (JsonException ex)
            {
                AppLogger.Debug($"Failed to parse notes index: {ex.Message}");
                return (Array.Empty<NoteMetadataDto>(), 0);
            }
            catch (NotSupportedException ex)
            {
                AppLogger.Debug($"Failed to parse notes index: {ex.Message}");
                return (Array.Empty<NoteMetadataDto>(), 0);
            }
            catch (InvalidOperationException ex)
            {
                AppLogger.Debug($"Failed to parse notes index: {ex.Message}");
                return (Array.Empty<NoteMetadataDto>(), 0);
            }
        }

        private static string ReadMarkdownContent(int noteId)
        {
            try
            {
                string path = AppPaths.GetNoteMarkdownPath(noteId);
                return File.Exists(path) ? File.ReadAllText(path) : string.Empty;
            }
            catch (IOException ex)
            {
                AppLogger.Debug($"Failed to read note content for note {noteId}: {ex.Message}");
                return string.Empty;
            }
            catch (UnauthorizedAccessException ex)
            {
                AppLogger.Debug($"Failed to read note content for note {noteId}: {ex.Message}");
                return string.Empty;
            }
        }

        private static string NormalizeLegacyContent(string content)
        {
            if (string.IsNullOrEmpty(content))
            {
                return string.Empty;
            }

            return content.TrimStart().StartsWith(@"{\rtf", StringComparison.OrdinalIgnoreCase)
                ? ConvertRtfToPlainText(content)
                : content;
        }

        /// <summary>
        /// Creates JSON serializer settings used by the note index file.
        /// </summary>
        /// <returns>The serializer options for writing the note index.</returns>
        private static JsonSerializerOptions CreateIndexJsonOptions()
        {
            return new JsonSerializerOptions
            {
                WriteIndented = true
            };
        }

        /// <summary>
        /// Writes the current in-memory note metadata to the index file only.
        /// </summary>
        private void WriteIndexFile()
        {
            NoteIndexDto index = new NoteIndexDto
            {
                SchemaVersion = CurrentSchemaVersion,
                UpdatedAtUtc = DateTime.UtcNow,
                Notes = Notes.Select(n => new NoteMetadataDto
                {
                    Id = n.Id,
                    Title = n.Title,
                    Level = n.Level,
                    Left = n.Left,
                    Top = n.Top,
                    Width = n.Width,
                    Height = n.Height,
                    IsDarkMode = n.IsDarkMode,
                    LastEditorDisplayMode = n.LastEditorDisplayMode.HasValue
                        ? EditorDisplayModeSettings.ToValue(n.LastEditorDisplayMode.Value)
                        : null,
                    TitleBarColor = n.TitleBarColor,
                    BackgroundImagePath = n.BackgroundImagePath,
                    BackgroundImageOpacity = n.BackgroundImageOpacity,
                    IsOpen = n.IsOpen
                }).ToArray()
            };

            WriteTextFile(IndexFilePath, JsonSerializer.Serialize(index, CreateIndexJsonOptions()));
        }

        /// <summary>
        /// Writes every note's markdown content to local storage.
        /// </summary>
        private void WriteMarkdownFiles()
        {
            foreach (NoteData note in Notes)
            {
                WriteTextFile(AppPaths.GetNoteMarkdownPath(note.Id), note.Content ?? string.Empty);
            }
        }

        /// <summary>
        /// Enumerates valid note ids derived from local markdown file names.
        /// </summary>
        /// <returns>The local note ids discovered from disk.</returns>
        private static IEnumerable<int> EnumerateLocalMarkdownNoteIds()
        {
            if (!Directory.Exists(AppPaths.NotesMarkdownRoot))
            {
                yield break;
            }

            foreach (string path in Directory.GetFiles(AppPaths.NotesMarkdownRoot, "*.md", SearchOption.TopDirectoryOnly))
            {
                string fileName = Path.GetFileNameWithoutExtension(path);
                if (int.TryParse(fileName, NumberStyles.Integer, CultureInfo.InvariantCulture, out int noteId) && noteId > 0)
                {
                    yield return noteId;
                }
            }
        }

        /// <summary>
        /// Creates a default note entry for a markdown file that exists locally but is missing from the index.
        /// </summary>
        /// <param name="noteId">The note id recovered from the markdown file name.</param>
        /// <returns>A recovered note entry.</returns>
        private static NoteData CreateRecoveredNote(int noteId)
        {
            return new NoteData
            {
                Id = noteId,
                Title = $"Recovered Note #{noteId}",
                Content = ReadMarkdownContent(noteId),
                Level = WindowLevel.Normal,
                Left = 100,
                Top = 100,
                Width = DefaultNoteWidth,
                Height = DefaultNoteHeight,
                IsOpen = false
            };
        }

        private static string ConvertRtfToPlainText(string rtf)
        {
            try
            {
                StringBuilder sb = new StringBuilder();
                Stack<bool> skipStack = new Stack<bool>();
                bool skipDestination = false;
                int ucSkipCount = 1;
                int pendingSkip = 0;

                for (int i = 0; i < rtf.Length; i++)
                {
                    if (pendingSkip > 0)
                    {
                        pendingSkip -= 1;
                        continue;
                    }

                    char c = rtf[i];
                    if (c == '{')
                    {
                        skipStack.Push(skipDestination);
                        continue;
                    }

                    if (c == '}')
                    {
                        skipDestination = skipStack.Count > 0 && skipStack.Pop();
                        continue;
                    }

                    if (c == '\\')
                    {
                        if (i + 1 >= rtf.Length)
                        {
                            break;
                        }

                        char next = rtf[++i];
                        if (next == '\\' || next == '{' || next == '}')
                        {
                            if (!skipDestination)
                            {
                                sb.Append(next);
                            }

                            continue;
                        }

                        if (next == '*')
                        {
                            skipDestination = true;
                            continue;
                        }

                        if (next == '\'')
                        {
                            if (i + 2 < rtf.Length)
                            {
                                string hex = rtf.Substring(i + 1, 2);
                                if (byte.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out byte b) && !skipDestination)
                                {
                                    sb.Append((char)b);
                                }

                                i += 2;
                            }

                            continue;
                        }

                        if (!char.IsLetter(next))
                        {
                            if (!skipDestination)
                            {
                                if (next == '~')
                                {
                                    sb.Append(' ');
                                }
                                else if (next == '|')
                                {
                                    sb.Append('\n');
                                }
                            }

                            continue;
                        }

                        int wordStart = i;
                        while (i < rtf.Length && char.IsLetter(rtf[i]))
                        {
                            i++;
                        }

                        string word = rtf.Substring(wordStart, i - wordStart);
                        int sign = 1;
                        if (i < rtf.Length && (rtf[i] == '-' || rtf[i] == '+'))
                        {
                            sign = rtf[i] == '-' ? -1 : 1;
                            i++;
                        }

                        int numStart = i;
                        while (i < rtf.Length && char.IsDigit(rtf[i]))
                        {
                            i++;
                        }

                        bool hasParam = i > numStart;
                        int param = 0;
                        if (hasParam)
                        {
                            int.TryParse(rtf.Substring(numStart, i - numStart), out param);
                            param *= sign;
                        }

                        bool hasDelimiterSpace = i < rtf.Length && rtf[i] == ' ';
                        if (!hasDelimiterSpace)
                        {
                            i--;
                        }

                        if (word.Equals("uc", StringComparison.OrdinalIgnoreCase) && hasParam)
                        {
                            ucSkipCount = Math.Max(0, param);
                        }

                        if (skipDestination)
                        {
                            continue;
                        }

                        switch (word)
                        {
                            case "par":
                            case "line":
                                sb.Append('\n');
                                break;
                            case "tab":
                                sb.Append('\t');
                                break;
                            case "u":
                                if (hasParam)
                                {
                                    int codePoint = param;
                                    if (codePoint < 0)
                                    {
                                        codePoint += 65536;
                                    }

                                    if (codePoint >= 0 && codePoint <= 0x10FFFF)
                                    {
                                        sb.Append(char.ConvertFromUtf32(codePoint));
                                    }

                                    pendingSkip = ucSkipCount;
                                }

                                break;
                        }

                        continue;
                    }

                    if (!skipDestination)
                    {
                        sb.Append(c);
                    }
                }

                return sb.ToString().Replace("\r\n", "\n", StringComparison.Ordinal).TrimEnd();
            }
            catch (ArgumentOutOfRangeException ex)
            {
                AppLogger.Debug($"Failed to convert RTF note content: {ex.Message}");
                return rtf;
            }
            catch (InvalidOperationException ex)
            {
                AppLogger.Debug($"Failed to convert RTF note content: {ex.Message}");
                return rtf;
            }
        }

        private static void WriteTextFile(string path, string content)
        {
            string? directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(path, content ?? string.Empty);
        }

        private static void TryDeleteFile(string path)
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch (IOException ex)
            {
                AppLogger.Debug($"Failed to delete file '{path}': {ex.Message}");
            }
            catch (UnauthorizedAccessException ex)
            {
                AppLogger.Debug($"Failed to delete file '{path}': {ex.Message}");
            }
        }

        private static void TryDeleteDirectory(string path)
        {
            try
            {
                if (Directory.Exists(path))
                {
                    Directory.Delete(path, true);
                }
            }
            catch (IOException ex)
            {
                AppLogger.Debug($"Failed to delete directory '{path}': {ex.Message}");
            }
            catch (UnauthorizedAccessException ex)
            {
                AppLogger.Debug($"Failed to delete directory '{path}': {ex.Message}");
            }
        }

        private class NoteIndexDto
        {
            public int SchemaVersion { get; set; } = 1;
            public DateTime UpdatedAtUtc { get; set; }
            public NoteMetadataDto[] Notes { get; set; } = Array.Empty<NoteMetadataDto>();
        }

        private class NoteMetadataDto
        {
            public int Id { get; set; }
            public string Title { get; set; }
            public string Content { get; set; }
            public WindowLevel Level { get; set; }
            public double Left { get; set; }
            public double Top { get; set; }
            public double Width { get; set; }
            public double Height { get; set; }
            public bool IsDarkMode { get; set; }
            public string? LastEditorDisplayMode { get; set; }
            public string TitleBarColor { get; set; }
            public string BackgroundImagePath { get; set; }
            public double BackgroundImageOpacity { get; set; }
            public bool IsOpen { get; set; }
        }

        private class LegacyNoteDataDto
        {
            public int Id { get; set; }
            public string Title { get; set; }
            public string Content { get; set; }
            public WindowLevel Level { get; set; }
            public double Left { get; set; }
            public double Top { get; set; }
            public double Width { get; set; }
            public double Height { get; set; }
            public bool IsDarkMode { get; set; }
            public string TitleBarColor { get; set; }
            public string BackgroundImagePath { get; set; }
            public double BackgroundImageOpacity { get; set; }
            public bool IsOpen { get; set; }
        }
    }
}


