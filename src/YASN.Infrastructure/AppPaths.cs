using System.Text.Json;

namespace YASN.Infrastructure
{
    /// <summary>
    /// Centralized paths for app data and configuration storage.
    /// </summary>
    public static class AppPaths
    {
        public const string DataDirectorySettingKey = "app.dataDirectory";

        /// <summary>Per-user application folder name used on Windows and Linux.</summary>
        private const string AppFolderName = "yasn";

        /// <summary>macOS bundle identifier, used for the per-user data and cache folders.</summary>
        private const string MacBundleId = "io.github.esonwang03.yasn";

        public static string BaseDirectory { get; } = AppDomain.CurrentDomain.BaseDirectory;

        /// <summary>
        /// Root for the read-only content shipped beside the app (preview styles, bundled tutorial).
        /// On macOS this content lives in <c>Contents/Resources</c>, not <c>Contents/MacOS</c>, because
        /// codesign treats every file under <c>Contents/MacOS</c> except the apphost as nested Mach-O
        /// code and rejects plain text files (e.g. <c>style/*.md</c>) as unsigned subcomponents. On
        /// other platforms the content sits next to the executable, so this resolves to
        /// <see cref="BaseDirectory"/>.
        /// </summary>
        public static string BundledContentRoot { get; } = OperatingSystem.IsMacOS()
            ? Path.GetFullPath(Path.Combine(BaseDirectory, "..", "Resources"))
            : BaseDirectory;

        /// <summary>
        /// Fixed per-user root for persistent, machine-local files (local settings, sync database,
        /// reminder state, log). Platform-standard and writable, unlike the executable directory.
        /// Windows: <c>%AppData%/yasn</c>. macOS: <c>~/Library/Application Support/&lt;bundle-id&gt;</c>.
        /// Machine-local files anchor here even when <see cref="DataDirectory"/> is moved, so relocating
        /// the data directory onto a synced folder never replicates them.
        /// </summary>
        public static string PersistentRoot { get; } = ResolvePersistentRoot();

        /// <summary>
        /// Per-user root for regenerable files (rendered HTML cache, log) that can be cleared without
        /// data loss. Separate from persistent data. Windows: <c>%LocalAppData%/yasn/cache</c>.
        /// macOS: <c>~/Library/Caches/&lt;bundle-id&gt;</c>.
        /// </summary>
        public static string CacheRoot { get; } = ResolveCacheRoot();

        public static string DataDirectory { get; }
        public static string LegacyNotesFilePath => Path.Combine(DataDirectory, "notes.json");
        public static string NotesIndexPath => Path.Combine(DataDirectory, "notes.index.json");
        public static string NotesMarkdownRoot => Path.Combine(DataDirectory, "notes");
        public static string NoteAssetsRoot => Path.Combine(DataDirectory, "note-assets");
        public static string NoteAttachmentsRoot => Path.Combine(NoteAssetsRoot, "attachments");
        public static string NoteBackgroundsRoot => Path.Combine(NoteAssetsRoot, "backgrounds");
        public static string StyleRoot => Path.Combine(DataDirectory, "style");
        public static string HtmlCacheRoot => Path.Combine(CacheRoot, "html-cache");

        /// <summary>
        /// Path to the bundled tutorial note Markdown, copied next to the executable at build time.
        /// Read-only source for the first-run welcome note and the "show tutorial" settings action.
        /// </summary>
        public static string BundledTutorialPath => Path.Combine(BundledContentRoot, "Resources", "tutorial.md");

        public static string SyncSettingsPath => Path.Combine(DataDirectory, "settings.sync.json");
        public static string LocalSettingsPath { get; } = Path.Combine(PersistentRoot, "settings.local.json");
        public static string LogFilePath { get; } = Path.Combine(CacheRoot, "yasn_log.log");
        public static string SignatureFilePath => Path.Combine(DataDirectory, "sync.manifest.json");

        /// <summary>
        /// Machine-local SQLite database holding the sync baseline, queue, and conflict state. Lives in
        /// the fixed <see cref="PersistentRoot"/> (not the replicated data directory) so it is never synced.
        /// </summary>
        public static string SyncDatabasePath { get; } = Path.Combine(PersistentRoot, "sync.db");

        /// <summary>
        /// Path to the machine-local reminder fire-state file. Kept in the fixed <see cref="PersistentRoot"/>
        /// (not the replicated data directory) so reminder delivery history is never synced between devices.
        /// </summary>
        public static string ReminderStatePath { get; } = Path.Combine(PersistentRoot, "reminder-state.json");

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
            Directory.CreateDirectory(PersistentRoot);
            Directory.CreateDirectory(CacheRoot);
            DataDirectory = ResolveDataDirectory();
            Directory.CreateDirectory(DataDirectory);
            Directory.CreateDirectory(NotesMarkdownRoot);
            Directory.CreateDirectory(NoteAssetsRoot);
            Directory.CreateDirectory(NoteAttachmentsRoot);
            Directory.CreateDirectory(NoteBackgroundsRoot);
            Directory.CreateDirectory(StyleRoot);
            Directory.CreateDirectory(HtmlCacheRoot);
        }

        /// <summary>
        /// Resolves the fixed per-user persistent root for the current platform.
        /// </summary>
        private static string ResolvePersistentRoot()
        {
            if (OperatingSystem.IsMacOS())
            {
                return Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                    "Library", "Application Support", MacBundleId);
            }

            // Windows: %AppData%/yasn. Linux/other: ~/.config/yasn (ApplicationData maps to $XDG_CONFIG_HOME).
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), AppFolderName);
        }

        /// <summary>
        /// Resolves the per-user cache root for the current platform.
        /// </summary>
        private static string ResolveCacheRoot()
        {
            if (OperatingSystem.IsMacOS())
            {
                return Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                    "Library", "Caches", MacBundleId);
            }

            // Windows: %LocalAppData%/yasn/cache. Linux/other: ~/.local/share/yasn/cache.
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), AppFolderName, "cache");
        }

        /// <summary>
        /// Permanently deletes all of the app's on-disk state: the configured data directory, the fixed
        /// persistent root (local settings, sync database, reminder state, log), and the cache root.
        /// Best-effort: per-path failures (a held handle, a permission error) are logged via Debug and
        /// skipped so the wipe removes as much as it can. Callers must release any open handles (the
        /// sync SQLite connection) first, and should exit afterward since in-memory state is now orphaned.
        /// </summary>
        public static void DeleteAllData()
        {
            HashSet<string> roots = new(StringComparer.OrdinalIgnoreCase)
            {
                DataDirectory,
                PersistentRoot,
                CacheRoot
            };

            foreach (string root in roots)
            {
                try
                {
                    if (Directory.Exists(root))
                    {
                        Directory.Delete(root, recursive: true);
                    }
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    System.Diagnostics.Debug.WriteLine($"Failed to delete '{root}': {ex.Message}");
                }
            }
        }

        public static bool TryNormalizeDataDirectory(string? value, out string normalizedPath, out string errorMessage)
        {
            errorMessage = string.Empty;
            try
            {
                string raw = string.IsNullOrWhiteSpace(value)
                    ? PersistentRoot
                    : value.Trim();

                normalizedPath = Path.GetFullPath(Path.IsPathRooted(raw)
                    ? raw
                    : Path.Combine(PersistentRoot, raw));

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
