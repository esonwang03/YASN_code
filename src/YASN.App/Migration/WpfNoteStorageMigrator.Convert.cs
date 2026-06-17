using System.Text.Json;
using System.Text.Json.Nodes;

namespace YASN.Migration
{
    /// <summary>
    /// Parsing, detection, and conversion helpers for <see cref="WpfNoteStorageMigrator"/>.
    /// </summary>
    public static partial class WpfNoteStorageMigrator
    {
        /// <summary>
        /// Parses either an index wrapper (<c>{schemaVersion, notes:[...]}</c>) or a bare v1 array
        /// of notes. Case-insensitive, so it reads both old PascalCase and new camelCase files.
        /// </summary>
        private static LegacyIndex ParseIndex(string json)
        {
            string trimmed = json.TrimStart();
            if (trimmed.StartsWith('['))
            {
                List<LegacyNote>? array = JsonSerializer.Deserialize<List<LegacyNote>>(json, ReadOptions);
                return new LegacyIndex { SchemaVersion = 1, Notes = array ?? new List<LegacyNote>() };
            }

            LegacyIndex? wrapper = JsonSerializer.Deserialize<LegacyIndex>(json, ReadOptions);
            return wrapper ?? new LegacyIndex();
        }

        /// <summary>
        /// Decides whether the index is already in the current shape: schema is current, the keys are
        /// camelCase (so the running app can read it), and every note already carries a sync key.
        /// </summary>
        private static bool IsAlreadyCurrent(string json, LegacyIndex parsed)
        {
            if (parsed.SchemaVersion < CurrentSchemaVersion)
            {
                return false;
            }

            if (parsed.Notes.Any(note => string.IsNullOrWhiteSpace(note.SyncKey)))
            {
                return false;
            }

            // The case-insensitive parse succeeds even on PascalCase; require true camelCase keys so a
            // v5 file still written in the old casing is re-emitted in a form the app can actually read.
            try
            {
                JsonNode? root = JsonNode.Parse(json);
                JsonArray? notes = root?["notes"]?.AsArray();
                if (notes is null || notes.Count == 0)
                {
                    return false;
                }

                JsonObject? first = notes[0]?.AsObject();
                return first is not null && first.ContainsKey("id") && first.ContainsKey("syncKey");
            }
            catch (JsonException)
            {
                return false;
            }
        }

        private static void ConvertInPlace(
            string dataDirectory,
            string indexPath,
            string sourcePath,
            string originalJson,
            LegacyIndex parsed,
            bool dryRun,
            MigrationReport report,
            Action<string> note)
        {
            string notesRoot = Path.Combine(dataDirectory, NotesFolderName);
            NewIndex output = new NewIndex { SchemaVersion = CurrentSchemaVersion };

            foreach (LegacyNote legacy in parsed.Notes)
            {
                NewNote converted = ConvertNote(legacy);
                output.Notes.Add(converted);

                string markdownPath = Path.Combine(notesRoot, $"{converted.Id}.md");
                if (!File.Exists(markdownPath) && !string.IsNullOrEmpty(legacy.Content))
                {
                    if (!dryRun)
                    {
                        Directory.CreateDirectory(notesRoot);
                        File.WriteAllText(markdownPath, legacy.Content);
                    }

                    report.MarkdownFilesWritten++;
                    note($"Restored markdown for note {converted.Id} from inline content.");
                }
            }

            output.Notes = output.Notes.OrderBy(n => n.Id).ToList();
            report.NotesMigrated = output.Notes.Count;

            string backupPath = Path.Combine(dataDirectory, BackupFileName);
            if (dryRun)
            {
                note($"[dry-run] Would back up {sourcePath} to {backupPath} and write {output.Notes.Count} notes in schema v{CurrentSchemaVersion}.");
                report.Status = MigrationStatus.Migrated;
                return;
            }

            if (!File.Exists(backupPath))
            {
                File.Copy(sourcePath, backupPath);
                report.BackupPath = backupPath;
                note($"Backed up original index to {backupPath}.");
            }
            else
            {
                report.BackupPath = backupPath;
                note($"Backup already exists at {backupPath}; left as-is.");
            }

            File.WriteAllText(indexPath, JsonSerializer.Serialize(output, WriteOptions));
            note($"Wrote {output.Notes.Count} notes to {indexPath} in schema v{CurrentSchemaVersion}.");
            report.Status = MigrationStatus.Migrated;
        }

        private static NewNote ConvertNote(LegacyNote legacy)
        {
            return new NewNote
            {
                Id = legacy.Id,
                SyncKey = string.IsNullOrWhiteSpace(legacy.SyncKey) ? Guid.NewGuid().ToString("N") : legacy.SyncKey,
                Title = string.IsNullOrWhiteSpace(legacy.Title) ? null : legacy.Title,
                Left = legacy.Left,
                Top = legacy.Top,
                Width = legacy.Width <= 0 ? 900 : legacy.Width,
                Height = legacy.Height <= 0 ? 560 : legacy.Height,
                IsOpen = legacy.IsOpen,
                Level = legacy.Level,
                ShowInTaskbar = true,
                ReminderAt = null,
                DisplayMode = MapDisplayMode(legacy.LastEditorDisplayMode)
            };
        }

        /// <summary>
        /// Maps the legacy editor-display-mode string to the new integer enum
        /// (previewOnly=0, textOnly=1, textAndPreview=2). Defaults to text-and-preview.
        /// </summary>
        private static int MapDisplayMode(string? legacyValue) => legacyValue switch
        {
            "previewOnly" => 0,
            "textOnly" => 1,
            "textAndPreview" => 2,
            _ => 2
        };
    }
}
