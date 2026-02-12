using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace YASN
{
    public class NoteManager
    {
        private static NoteManager _instance;
        private static readonly object _lock = new object();

        private const string LegacySaveFileName = "notes.json";
        private const int CurrentSchemaVersion = 2;

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
            var note = new NoteData
            {
                Id = _nextId++,
                Title = $"Note #{_nextId - 1}",
                Content = "Double Right Click to enter your markdown here...",
                Level = level,
                Left = 100,
                Top = 100,
                Width = 760,
                Height = 460,
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
            TryDeleteDirectory(Path.Combine(AppPaths.NoteAssetsRoot, note.Id.ToString()));
            TryDeleteDirectory(Path.Combine(AppPaths.NoteBackgroundsRoot, note.Id.ToString()));
            Save();
        }

        public void Save()
        {
            try
            {
                var options = new JsonSerializerOptions
                {
                    WriteIndented = true
                };

                var index = new NoteIndexDto
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
                        TitleBarColor = n.TitleBarColor,
                        BackgroundImagePath = n.BackgroundImagePath,
                        BackgroundImageOpacity = n.BackgroundImageOpacity,
                        IsOpen = n.IsOpen
                    }).ToArray()
                };

                WriteTextFile(IndexFilePath, JsonSerializer.Serialize(index, options));

                foreach (var note in Notes)
                {
                    WriteTextFile(AppPaths.GetNoteMarkdownPath(note.Id), note.Content ?? string.Empty);
                }

                System.Diagnostics.Debug.WriteLine($"Saved {Notes.Count} notes to {IndexFilePath} (schema v{CurrentSchemaVersion})");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to save notes: {ex.Message}");
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
                    System.Diagnostics.Debug.WriteLine($"Notes index not found at {IndexFilePath}");
                    return;
                }

                var json = File.ReadAllText(IndexFilePath);
                var (items, schemaVersion) = ParseIndexItems(json);
                if (items == null)
                {
                    return;
                }

                var shouldRewrite = schemaVersion < CurrentSchemaVersion;
                foreach (var item in items)
                {
                    var content = ReadMarkdownContent(item.Id);
                    if (string.IsNullOrEmpty(content) && !string.IsNullOrEmpty(item.Content))
                    {
                        content = NormalizeLegacyContent(item.Content);
                        shouldRewrite = true;
                    }

                    var note = new NoteData
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

                System.Diagnostics.Debug.WriteLine($"Loaded {Notes.Count} notes from {IndexFilePath}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to load notes: {ex.Message}\nStack trace: {ex.StackTrace}");
            }
        }

        public void RestoreOpenNotes()
        {
            var openNotes = Notes.Where(n => n.IsOpen).ToList();
            foreach (var note in openNotes)
            {
                try
                {
                    var window = new FloatingWindow(note);
                    window.Show();
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Failed to restore note window {note.Id}: {ex.Message}");
                }
            }
        }

        public void ReloadNotes()
        {
            foreach (var note in Notes.Where(n => n.IsOpen).ToList())
            {
                note.Window?.Close();
            }

            Notes.Clear();
            Load();
            RestoreOpenNotes();
        }

        private void TryMigrateLegacyStorage()
        {
            var legacyCandidates = new[]
            {
                AppPaths.LegacyNotesFilePath,
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, LegacySaveFileName)
            };

            var legacyPath = legacyCandidates.FirstOrDefault(File.Exists);
            if (string.IsNullOrEmpty(legacyPath))
            {
                return;
            }

            try
            {
                var legacyJson = File.ReadAllText(legacyPath);
                var legacyItems = JsonSerializer.Deserialize<LegacyNoteDataDto[]>(legacyJson);
                if (legacyItems == null || legacyItems.Length == 0)
                {
                    return;
                }

                var backupPath = Path.Combine(AppPaths.DataDirectory, "notes.v1.backup.json");
                if (!File.Exists(backupPath))
                {
                    WriteTextFile(backupPath, legacyJson);
                }

                Notes.Clear();
                foreach (var item in legacyItems)
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
                        TitleBarColor = item.TitleBarColor,
                        BackgroundImagePath = item.BackgroundImagePath,
                        BackgroundImageOpacity = item.BackgroundImageOpacity,
                        IsOpen = item.IsOpen
                    });
                }

                _nextId = Notes.Count > 0 ? Notes.Max(n => n.Id) + 1 : 1;
                Save();
                System.Diagnostics.Debug.WriteLine($"Migrated {Notes.Count} legacy notes from {legacyPath}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to migrate legacy notes: {ex.Message}");
            }
        }

        private static (NoteMetadataDto[] items, int schemaVersion) ParseIndexItems(string json)
        {
            try
            {
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.ValueKind == JsonValueKind.Array)
                {
                    var notes = JsonSerializer.Deserialize<NoteMetadataDto[]>(json);
                    return (notes, 1);
                }

                if (doc.RootElement.ValueKind != JsonValueKind.Object)
                {
                    return (null, 0);
                }

                var index = JsonSerializer.Deserialize<NoteIndexDto>(json);
                return (index?.Notes ?? Array.Empty<NoteMetadataDto>(), index?.SchemaVersion ?? 1);
            }
            catch
            {
                return (null, 0);
            }
        }

        private static string ReadMarkdownContent(int noteId)
        {
            try
            {
                var path = AppPaths.GetNoteMarkdownPath(noteId);
                return File.Exists(path) ? File.ReadAllText(path) : string.Empty;
            }
            catch
            {
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

        private static string ConvertRtfToPlainText(string rtf)
        {
            try
            {
                var sb = new StringBuilder();
                var skipStack = new Stack<bool>();
                var skipDestination = false;
                var ucSkipCount = 1;
                var pendingSkip = 0;

                for (var i = 0; i < rtf.Length; i++)
                {
                    if (pendingSkip > 0)
                    {
                        pendingSkip -= 1;
                        continue;
                    }

                    var c = rtf[i];
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

                        var next = rtf[++i];
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
                                var hex = rtf.Substring(i + 1, 2);
                                if (byte.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var b) && !skipDestination)
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

                        var wordStart = i;
                        while (i < rtf.Length && char.IsLetter(rtf[i]))
                        {
                            i++;
                        }

                        var word = rtf.Substring(wordStart, i - wordStart);
                        var sign = 1;
                        if (i < rtf.Length && (rtf[i] == '-' || rtf[i] == '+'))
                        {
                            sign = rtf[i] == '-' ? -1 : 1;
                            i++;
                        }

                        var numStart = i;
                        while (i < rtf.Length && char.IsDigit(rtf[i]))
                        {
                            i++;
                        }

                        var hasParam = i > numStart;
                        var param = 0;
                        if (hasParam)
                        {
                            int.TryParse(rtf.Substring(numStart, i - numStart), out param);
                            param *= sign;
                        }

                        var hasDelimiterSpace = i < rtf.Length && rtf[i] == ' ';
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
                                    var codePoint = param;
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

                return sb.ToString().Replace("\r\n", "\n").TrimEnd();
            }
            catch
            {
                return rtf;
            }
        }

        private static void WriteTextFile(string path, string content)
        {
            var directory = Path.GetDirectoryName(path);
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
            catch
            {
                // ignored
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
            catch
            {
                // ignored
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
