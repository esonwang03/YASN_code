using System;
using System.IO;

namespace YASN
{
    /// <summary>
    /// Centralized paths for app data and configuration storage.
    /// </summary>
    public static class AppPaths
    {
        public static string BaseDirectory { get; } = AppDomain.CurrentDomain.BaseDirectory;
        public static string DataDirectory { get; } = Path.Combine(BaseDirectory, "data");
        public static string NotesFilePath { get; } = Path.Combine(DataDirectory, "notes.json");
        public static string NoteImagesRoot { get; } = Path.Combine(DataDirectory, "NoteImages");
        public static string NoteBackgroundsRoot { get; } = Path.Combine(DataDirectory, "NoteBackgrounds");

        public static string SyncSettingsPath { get; } = Path.Combine(DataDirectory, "settings.sync.json");
        public static string LocalSettingsPath { get; } = Path.Combine(BaseDirectory, "settings.local.json");

        static AppPaths()
        {
            Directory.CreateDirectory(DataDirectory);
            Directory.CreateDirectory(NoteImagesRoot);
            Directory.CreateDirectory(NoteBackgroundsRoot);
        }

        public static string GetNoteImagesDirectory(int noteId)
        {
            var path = Path.Combine(NoteImagesRoot, noteId.ToString());
            Directory.CreateDirectory(path);
            return path;
        }

        public static string GetNoteBackgroundDirectory(int noteId)
        {
            var path = Path.Combine(NoteBackgroundsRoot, noteId.ToString());
            Directory.CreateDirectory(path);
            return path;
        }
    }
}
