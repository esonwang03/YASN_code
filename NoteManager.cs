using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace YASN
{
    public class NoteManager
    {
        private static NoteManager _instance;
        private static readonly object _lock = new object();
        private const string SaveFileName = "notes.json";
        
        // ��ȡ�����ļ�������·��
        private static string SaveFilePath => AppPaths.NotesFilePath;

        public static NoteManager Instance
        {
            get
            {
                if (_instance == null)
                {
                    lock (_lock)
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
                Content = "Enter your text here...",
                Level = level,
                Left = 100,
                Top = 100,
                Width = 400,
                Height = 300,
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
            if (note != null)
            {
                Notes.Remove(note);
                Save();
            }
        }

        public void Save()
        {
            try
            {
                var options = new JsonSerializerOptions
                {
                    WriteIndented = true
                };

                var json = JsonSerializer.Serialize(Notes.Select(n => new
                {
                    n.Id,
                    n.Title,
                    n.Content,
                    n.Level,
                    n.Left,
                    n.Top,
                    n.Width,
                    n.Height,
                    n.IsDarkMode,
                    n.TitleBarColor,
                    n.BackgroundImagePath,
                    n.BackgroundImageOpacity,
                    n.IsOpen
                }), options);

                var directory = Path.GetDirectoryName(SaveFilePath);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                File.WriteAllText(SaveFilePath, json);
                System.Diagnostics.Debug.WriteLine($"Saved {Notes.Count} notes to {SaveFilePath}");
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
                var legacyPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, SaveFileName);
                if (!File.Exists(SaveFilePath) && File.Exists(legacyPath))
                {
                    var directory = Path.GetDirectoryName(SaveFilePath);
                    if (!string.IsNullOrEmpty(directory))
                    {
                        Directory.CreateDirectory(directory);
                    }
                    File.Copy(legacyPath, SaveFilePath, true);
                }

                if (File.Exists(SaveFilePath))
                {
                    System.Diagnostics.Debug.WriteLine($"Loading notes from {SaveFilePath}");
                    var json = File.ReadAllText(SaveFilePath);
                    System.Diagnostics.Debug.WriteLine($"JSON content length: {json.Length}");
                    
                    var items = JsonSerializer.Deserialize<NoteDataDto[]>(json);

                    if (items != null)
                    {
                        System.Diagnostics.Debug.WriteLine($"Deserialized {items.Length} notes");
                        foreach (var item in items)
                        {
                            var note = new NoteData
                            {
                                Id = item.Id,
                                Title = item.Title,
                                Content = item.Content,
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
                            System.Diagnostics.Debug.WriteLine($"Loaded note: Id={note.Id}, Title={note.Title}, IsOpen={note.IsOpen}");

                            if (item.Id >= _nextId)
                            {
                                _nextId = item.Id + 1;
                            }
                        }
                    }
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"Notes file not found at {SaveFilePath}");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to load notes: {ex.Message}\nStack trace: {ex.StackTrace}");
            }
        }
        
        /// <summary>
        /// �Զ���֮ǰ�򿪵ı�ǩ����
        /// </summary>
        public void RestoreOpenNotes()
        {
            System.Diagnostics.Debug.WriteLine($"RestoreOpenNotes called. Total notes: {Notes.Count}");
            
            var openNotes = Notes.Where(n => n.IsOpen).ToList();
            System.Diagnostics.Debug.WriteLine($"Notes marked as open: {openNotes.Count}");
            
            foreach (var note in openNotes)
            {
                try
                {
                    System.Diagnostics.Debug.WriteLine($"Restoring note: Id={note.Id}, Title={note.Title}, IsOpen={note.IsOpen}");
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

        /// <summary>
        /// Reload notes from file (for sync purposes)
        /// </summary>
        public void ReloadNotes()
        {
            // Close all open windows
            foreach (var note in Notes.Where(n => n.IsOpen).ToList())
            {
                note.Window?.Close();
            }

            // Clear current notes
            Notes.Clear();

            // Reload from file
            Load();

            // Restore open notes
            RestoreOpenNotes();
        }

        private class NoteDataDto
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



