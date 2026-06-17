using System.Text.Json;

namespace YASN.Migration
{
    /// <summary>
    /// Converts the legacy WPF note store (PascalCase index, schema v1/v2) into the schema the
    /// Avalonia build reads (camelCase, schema 5, with per-note sync keys).
    /// </summary>
    /// <remarks>
    /// Both builds share the same files — <c>notes.index.json</c> and <c>notes/{id}.md</c> under the
    /// data directory — but System.Text.Json is case-sensitive, so the new build silently loads zero
    /// notes from an old PascalCase index. This migrator is dependency-free (no Avalonia/Core types)
    /// so it can be linked into both the app and the standalone CLI, and is idempotent: a current
    /// index is left untouched.
    /// </remarks>
    public static partial class WpfNoteStorageMigrator
    {
        /// <summary>The schema version written by the current Avalonia build.</summary>
        public const int CurrentSchemaVersion = 5;

        private const string IndexFileName = "notes.index.json";
        private const string LegacyFileName = "notes.json";
        private const string BackupFileName = "notes.index.wpf-backup.json";
        private const string NotesFolderName = "notes";

        private static readonly JsonSerializerOptions ReadOptions = new()
        {
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true
        };

        private static readonly JsonSerializerOptions WriteOptions = new() { WriteIndented = true };

        /// <summary>
        /// Migrates the note store under <paramref name="dataDirectory"/> in place, backing up the
        /// original index first.
        /// </summary>
        /// <param name="dataDirectory">The directory that holds the index and the notes folder.</param>
        /// <param name="dryRun">When true, detects and reports but writes nothing.</param>
        /// <param name="log">An optional sink that receives each progress line as it is produced.</param>
        /// <returns>A report describing the outcome.</returns>
        public static MigrationReport Migrate(string dataDirectory, bool dryRun = false, TextWriter? log = null)
        {
            MigrationReport report = new MigrationReport();

            void Note(string message)
            {
                report.Messages.Add(message);
                log?.WriteLine(message);
            }

            try
            {
                if (string.IsNullOrWhiteSpace(dataDirectory) || !Directory.Exists(dataDirectory))
                {
                    Note($"Data directory not found: {dataDirectory}");
                    report.Status = MigrationStatus.NothingToMigrate;
                    return report;
                }

                string indexPath = Path.Combine(dataDirectory, IndexFileName);
                string legacyPath = Path.Combine(dataDirectory, LegacyFileName);
                string sourcePath = File.Exists(indexPath) ? indexPath
                    : File.Exists(legacyPath) ? legacyPath
                    : string.Empty;

                if (sourcePath.Length == 0)
                {
                    Note("No notes.index.json or notes.json found; nothing to migrate.");
                    report.Status = MigrationStatus.NothingToMigrate;
                    return report;
                }

                string json = File.ReadAllText(sourcePath);
                LegacyIndex parsed = ParseIndex(json);
                if (parsed.Notes.Count == 0)
                {
                    Note($"Index at {sourcePath} has no notes; nothing to migrate.");
                    report.Status = MigrationStatus.NothingToMigrate;
                    return report;
                }

                if (sourcePath == indexPath && IsAlreadyCurrent(json, parsed))
                {
                    Note("Index is already in the current schema; no migration needed.");
                    report.Status = MigrationStatus.AlreadyCurrent;
                    return report;
                }

                ConvertInPlace(dataDirectory, indexPath, sourcePath, json, parsed, dryRun, report, Note);
                return report;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or NotSupportedException or ArgumentException)
            {
                Note($"Migration failed: {ex.Message}");
                report.Status = MigrationStatus.Failed;
                return report;
            }
        }
    }
}
