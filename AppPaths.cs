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
        public static string LegacyNotesFilePath { get; } = Path.Combine(DataDirectory, "notes.json");
        public static string NotesIndexPath { get; } = Path.Combine(DataDirectory, "notes.index.json");
        public static string NotesMarkdownRoot { get; } = Path.Combine(DataDirectory, "notes");
        public static string NoteAssetsRoot { get; } = Path.Combine(DataDirectory, "note-assets");
        public static string NoteBackgroundsRoot { get; } = Path.Combine(NoteAssetsRoot, "backgrounds");
        public static string HtmlCacheRoot { get; } = Path.Combine(DataDirectory, "html-cache");

        public static string SyncSettingsPath { get; } = Path.Combine(DataDirectory, "settings.sync.json");
        public static string LocalSettingsPath { get; } = Path.Combine(BaseDirectory, "settings.local.json");
        public static string LogFilePath { get; } = Path.Combine(BaseDirectory, "yasn_log.log");
        public static string SignatureFilePath { get; } = Path.Combine(DataDirectory, "sync.manifest.json");

        static AppPaths()
        {
            Directory.CreateDirectory(DataDirectory);
            Directory.CreateDirectory(NotesMarkdownRoot);
            Directory.CreateDirectory(NoteAssetsRoot);
            Directory.CreateDirectory(NoteBackgroundsRoot);
            Directory.CreateDirectory(HtmlCacheRoot);
        }

        public static string GetNoteMarkdownPath(int noteId)
        {
            return Path.Combine(NotesMarkdownRoot, $"{noteId}.md");
        }

        public static string GetNoteAssetsDirectory(int noteId)
        {
            var path = Path.Combine(NoteAssetsRoot, noteId.ToString());
            Directory.CreateDirectory(path);
            return path;
        }

        public static string GetNoteBackgroundDirectory(int noteId)
        {
            var path = Path.Combine(NoteBackgroundsRoot, noteId.ToString());
            Directory.CreateDirectory(path);
            return path;
        }

        public static string GetNoteHtmlCachePath(int noteId)
        {
            return Path.Combine(HtmlCacheRoot, $"{noteId}.html");
        }
    }
}
