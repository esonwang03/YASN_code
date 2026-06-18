using System.Text.Json;

namespace YASN.Infrastructure
{
    /// <summary>
    /// Centralized paths for app data and configuration storage.
    /// </summary>
    public static class AppPaths
    {
        public const string DataDirectorySettingKey = "app.dataDirectory";

        public static string BaseDirectory { get; } = AppDomain.CurrentDomain.BaseDirectory;
        public static string DataDirectory { get; }
        public static string LegacyNotesFilePath => Path.Combine(DataDirectory, "notes.json");
        public static string NotesIndexPath => Path.Combine(DataDirectory, "notes.index.json");
        public static string NotesMarkdownRoot => Path.Combine(DataDirectory, "notes");
        public static string NoteAssetsRoot => Path.Combine(DataDirectory, "note-assets");
        public static string NoteAttachmentsRoot => Path.Combine(NoteAssetsRoot, "attachments");
        public static string NoteBackgroundsRoot => Path.Combine(NoteAssetsRoot, "backgrounds");
        public static string StyleRoot => Path.Combine(DataDirectory, "style");
        public static string HtmlCacheRoot => Path.Combine(DataDirectory, "html-cache");

        /// <summary>
        /// Path to the bundled tutorial note Markdown, copied next to the executable at build time.
        /// Read-only source for the first-run welcome note and the "show tutorial" settings action.
        /// </summary>
        public static string BundledTutorialPath => Path.Combine(BaseDirectory, "Resources", "tutorial.md");

        public static string SyncSettingsPath => Path.Combine(DataDirectory, "settings.sync.json");
        public static string LocalSettingsPath { get; } = Path.Combine(BaseDirectory, "settings.local.json");
        public static string LogFilePath { get; } = Path.Combine(BaseDirectory, "yasn_log.log");
        public static string SignatureFilePath => Path.Combine(DataDirectory, "sync.manifest.json");

        /// <summary>
        /// Machine-local SQLite database holding the sync baseline, queue, and conflict state. Lives
        /// beside the local settings (not in the replicated data directory) so it is never synced.
        /// </summary>
        public static string SyncDatabasePath { get; } = Path.Combine(BaseDirectory, "sync.db");

        /// <summary>
        /// Path to the machine-local reminder fire-state file. Kept beside the local settings (not in
        /// the replicated data directory) so reminder delivery history is never synced between devices.
        /// </summary>
        public static string ReminderStatePath { get; } = Path.Combine(BaseDirectory, "reminder-state.json");

        /// <summary>
        /// Per-user LaunchAgents directory used for macOS auto-start.
        /// </summary>
        public static string MacLaunchAgentsDirectory => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "Library",
            "LaunchAgents");

        /// <summary>
        /// Lock file backing the single-instance guard.
        /// </summary>
        public static string SingleInstanceLockPath => Path.Combine(DataDirectory, "yasn.instance.lock");

        static AppPaths()
        {
            DataDirectory = ResolveDataDirectory();
            Directory.CreateDirectory(DataDirectory);
            Directory.CreateDirectory(NotesMarkdownRoot);
            Directory.CreateDirectory(NoteAssetsRoot);
            Directory.CreateDirectory(NoteAttachmentsRoot);
            Directory.CreateDirectory(NoteBackgroundsRoot);
            Directory.CreateDirectory(StyleRoot);
            Directory.CreateDirectory(HtmlCacheRoot);
        }

        public static bool TryNormalizeDataDirectory(string? value, out string normalizedPath, out string errorMessage)
        {
            errorMessage = string.Empty;
            try
            {
                string raw = string.IsNullOrWhiteSpace(value)
                    ? Path.Combine(BaseDirectory, "data")
                    : value.Trim();

                normalizedPath = Path.GetFullPath(Path.IsPathRooted(raw)
                    ? raw
                    : Path.Combine(BaseDirectory, raw));

                Directory.CreateDirectory(normalizedPath);
                return true;
            }
            catch (ArgumentException ex)
            {
                normalizedPath = string.Empty;
                errorMessage = ex.Message;
                System.Diagnostics.Debug.WriteLine($"Failed to normalize data directory: {ex.Message}");
                return false;
            }
            catch (IOException ex)
            {
                normalizedPath = string.Empty;
                errorMessage = ex.Message;
                System.Diagnostics.Debug.WriteLine($"Failed to normalize data directory: {ex.Message}");
                return false;
            }
            catch (NotSupportedException ex)
            {
                normalizedPath = string.Empty;
                errorMessage = ex.Message;
                System.Diagnostics.Debug.WriteLine($"Failed to normalize data directory: {ex.Message}");
                return false;
            }
            catch (UnauthorizedAccessException ex)
            {
                normalizedPath = string.Empty;
                errorMessage = ex.Message;
                System.Diagnostics.Debug.WriteLine($"Failed to normalize data directory: {ex.Message}");
                return false;
            }
        }

        private static string ResolveDataDirectory()
        {
            if (File.Exists(LocalSettingsPath))
            {
                try
                {
                    string json = File.ReadAllText(LocalSettingsPath);
                    Dictionary<string, string>? dict = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
                    if (dict != null && dict.TryGetValue(DataDirectorySettingKey, out string? value) &&
                        TryNormalizeDataDirectory(value, out string? configuredPath, out _))
                    {
                        return configuredPath;
                    }
                }
                catch (IOException ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Failed to read local settings for data directory: {ex.Message}");
                }
                catch (JsonException ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Failed to read local settings for data directory: {ex.Message}");
                }
                catch (UnauthorizedAccessException ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Failed to read local settings for data directory: {ex.Message}");
                }
            }

            TryNormalizeDataDirectory(null, out string? defaultPath, out _);
            return defaultPath;
        }

        public static string GetNoteMarkdownPath(int noteId)
        {
            return Path.Combine(NotesMarkdownRoot, $"{noteId}.md");
        }

        public static string GetNoteAssetsDirectory(string noteId)
        {
            string path = Path.Combine(NoteAssetsRoot, noteId);
            Directory.CreateDirectory(path);
            return path;
        }

        public static string GetNoteBackgroundDirectory(int noteId)
        {
            string path = Path.Combine(NoteBackgroundsRoot, noteId.ToString(System.Globalization.CultureInfo.InvariantCulture));
            Directory.CreateDirectory(path);
            return path;
        }

        public static string GetNoteAttachmentsDirectory(string noteId)
        {
            string path = Path.Combine(NoteAttachmentsRoot, noteId);
            Directory.CreateDirectory(path);
            return path;
        }

        public static string GetNoteHtmlCachePath(string noteId)
        {
            return Path.Combine(HtmlCacheRoot, $"{noteId}.html");
        }
    }
}
