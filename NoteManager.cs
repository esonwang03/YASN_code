using System.Collections.ObjectModel;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Windows.Documents;
using DataFormats = System.Windows.DataFormats;

namespace YASN
{
    public class NoteManager
    {
        private static NoteManager? _instance;
        private static readonly object Lock = new();
        private const string LegacySaveFileName = "notes.json";

        private static string IndexFilePath => AppPaths.NotesIndexPath;

        public static NoteManager Instance
        {
            get
            {
                if (_instance == null)
                {
                    lock (Lock)
                    {
                        if (_instance == null)
                        {
                            _instance = new NoteManager();
                        }
                    }
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
                Content = "Enter your markdown here...",
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

                var metadata = Notes.Select(n => new NoteMetadataDto
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
                }).ToArray();

                WriteTextFile(IndexFilePath, JsonSerializer.Serialize(metadata, options));

                foreach (var note in Notes)
                {
                    var markdownPath = AppPaths.GetNoteMarkdownPath(note.Id);
                    WriteTextFile(markdownPath, note.Content ?? string.Empty);
                }

                System.Diagnostics.Debug.WriteLine($"Saved {Notes.Count} notes to {IndexFilePath}");
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
                var items = JsonSerializer.Deserialize<NoteMetadataDto[]>(json);
                if (items == null)
                {
                    return;
                }

                foreach (var item in items)
                {
                    var content = ReadMarkdownContent(item.Id);
                    if (string.IsNullOrEmpty(content) && !string.IsNullOrEmpty(item.Content))
                    {
                        content = NormalizeLegacyContent(item.Content);
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

                System.Diagnostics.Debug.WriteLine($"Loaded {Notes.Count} notes from {IndexFilePath}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to load notes: {ex.Message}\nStack trace: {ex.StackTrace}");
            }
        }

        public void RestoreOpenNotes()
        {
            System.Diagnostics.Debug.WriteLine($"RestoreOpenNotes called. Total notes: {Notes.Count}");

            var openNotes = Notes.Where(n => n.IsOpen).ToList();
            System.Diagnostics.Debug.WriteLine($"Notes marked as open: {openNotes.Count}");

            foreach (var note in openNotes)
            {
                try
                {
                    var window = new FloatingWindow(note);
                    window.Show();
                    System.Diagnostics.Debug.WriteLine($"Note {note.Id} window created and shown successfully");
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

                var migratedNotes = new List<NoteData>();
                foreach (var item in legacyItems)
                {
                    migratedNotes.Add(new NoteData
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

                Notes.Clear();
                foreach (var note in migratedNotes)
                {
                    Notes.Add(note);
                }

                if (migratedNotes.Count > 0)
                {
                    _nextId = migratedNotes.Max(n => n.Id) + 1;
                }

                Save();
                System.Diagnostics.Debug.WriteLine($"Migrated {migratedNotes.Count} legacy notes from {legacyPath}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to migrate legacy notes: {ex.Message}");
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

            if (!content.TrimStart().StartsWith(@"{\rtf", StringComparison.OrdinalIgnoreCase))
            {
                return content;
            }

            try
            {
                var document = new FlowDocument();
                var textRange = new TextRange(document.ContentStart, document.ContentEnd);
                using var stream = new MemoryStream(Encoding.UTF8.GetBytes(content));
                textRange.Load(stream, DataFormats.Rtf);
                return (textRange.Text ?? string.Empty).Replace("\r\n", "\n").TrimEnd();
            }
            catch
            {
                return content;
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
