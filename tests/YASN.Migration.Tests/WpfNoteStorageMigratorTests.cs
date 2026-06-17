using YASN.AvaloniaNotes;
using YASN.Core;

namespace YASN.Migration.Tests
{
    /// <summary>
    /// Verifies the legacy WPF storage migrator: it rewrites an old PascalCase index into the
    /// camelCase schema the Avalonia <see cref="NoteRepository"/> can actually read, backs up the
    /// original, backfills markdown, and is idempotent.
    /// </summary>
    public sealed class WpfNoteStorageMigratorTests : IDisposable
    {
        private readonly string root = Path.Combine(
            Path.GetTempPath(), "yasn-migrator-tests", Guid.NewGuid().ToString("N"));

        public void Dispose()
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }

        private string WriteIndex(string json)
        {
            Directory.CreateDirectory(root);
            string path = Path.Combine(root, "notes.index.json");
            File.WriteAllText(path, json);
            return path;
        }

        private const string V2PascalIndex = """
            {
              "SchemaVersion": 2,
              "Notes": [
                { "Id": 1, "Title": "Groceries", "Level": 1, "Left": 10, "Top": 20, "Width": 760, "Height": 460, "IsDarkMode": true, "LastEditorDisplayMode": "textOnly", "TitleBarColor": "#ABC", "IsOpen": true },
                { "Id": 2, "Title": "Plans", "Level": 0, "Left": 30, "Top": 40, "Width": 800, "Height": 500, "LastEditorDisplayMode": "previewOnly", "IsOpen": false }
              ]
            }
            """;

        /// <summary>The real bug: after migration the Avalonia repository loads the notes.</summary>
        [Fact]
        public void MigratedIndexIsReadableByRepository()
        {
            WriteIndex(V2PascalIndex);
            Directory.CreateDirectory(Path.Combine(root, "notes"));
            File.WriteAllText(Path.Combine(root, "notes", "1.md"), "# groceries");
            File.WriteAllText(Path.Combine(root, "notes", "2.md"), "# plans");

            MigrationReport report = WpfNoteStorageMigrator.Migrate(root);

            Assert.Equal(MigrationStatus.Migrated, report.Status);
            IReadOnlyList<AvaloniaNoteDocument> notes = new NoteRepository(root).LoadAll();
            Assert.Equal(2, notes.Count);
            Assert.Equal("Groceries", notes[0].Title);
            Assert.Equal(WindowLevel.TopMost, notes[0].Level);
            Assert.Equal("# groceries", notes[0].Content);
        }

        /// <summary>Titles are preserved as the explicit stored title.</summary>
        [Fact]
        public void PreservesTitleAsStoredTitle()
        {
            WriteIndex(V2PascalIndex);
            WpfNoteStorageMigrator.Migrate(root);

            AvaloniaNoteDocument note = new NoteRepository(root).LoadAll().Single(n => n.Id == 1);
            Assert.Equal("Groceries", note.StoredTitle);
        }

        /// <summary>Legacy display-mode strings map to the new integer enum.</summary>
        [Fact]
        public void MapsDisplayModeStrings()
        {
            WriteIndex(V2PascalIndex);
            WpfNoteStorageMigrator.Migrate(root);

            IReadOnlyList<AvaloniaNoteDocument> notes = new NoteRepository(root).LoadAll();
            Assert.Equal(EditorDisplayMode.TextOnly, notes.Single(n => n.Id == 1).DisplayMode);
            Assert.Equal(EditorDisplayMode.PreviewOnly, notes.Single(n => n.Id == 2).DisplayMode);
        }

        /// <summary>Every migrated note gets a non-empty sync key.</summary>
        [Fact]
        public void AssignsSyncKeys()
        {
            WriteIndex(V2PascalIndex);
            WpfNoteStorageMigrator.Migrate(root);

            IReadOnlyList<AvaloniaNoteDocument> notes = new NoteRepository(root).LoadAll();
            Assert.All(notes, n => Assert.False(string.IsNullOrWhiteSpace(n.SyncKey)));
            Assert.NotEqual(notes[0].SyncKey, notes[1].SyncKey);
        }

        /// <summary>The original index is backed up verbatim before conversion.</summary>
        [Fact]
        public void BacksUpOriginalIndex()
        {
            WriteIndex(V2PascalIndex);
            MigrationReport report = WpfNoteStorageMigrator.Migrate(root);

            string backup = Path.Combine(root, "notes.index.wpf-backup.json");
            Assert.True(File.Exists(backup));
            Assert.Equal(V2PascalIndex, File.ReadAllText(backup));
            Assert.Equal(backup, report.BackupPath);
        }

        /// <summary>A v1 bare array backfills its inline content into a markdown file.</summary>
        [Fact]
        public void BackfillsMarkdownFromV1InlineContent()
        {
            WriteIndex("""
                [ { "Id": 7, "Title": "Solo", "Content": "inline body text", "Level": 0, "IsOpen": true } ]
                """);

            MigrationReport report = WpfNoteStorageMigrator.Migrate(root);

            Assert.Equal(1, report.MarkdownFilesWritten);
            Assert.Equal("inline body text", File.ReadAllText(Path.Combine(root, "notes", "7.md")));
            Assert.Equal("inline body text", new NoteRepository(root).LoadAll().Single().Content);
        }

        /// <summary>Running twice converts once; the second run is a no-op and keeps the first backup.</summary>
        [Fact]
        public void IsIdempotent()
        {
            WriteIndex(V2PascalIndex);
            WpfNoteStorageMigrator.Migrate(root);
            string migrated = File.ReadAllText(Path.Combine(root, "notes.index.json"));
            string backup = File.ReadAllText(Path.Combine(root, "notes.index.wpf-backup.json"));

            MigrationReport second = WpfNoteStorageMigrator.Migrate(root);

            Assert.Equal(MigrationStatus.AlreadyCurrent, second.Status);
            Assert.Equal(migrated, File.ReadAllText(Path.Combine(root, "notes.index.json")));
            Assert.Equal(backup, File.ReadAllText(Path.Combine(root, "notes.index.wpf-backup.json")));
        }

        /// <summary>An empty or missing store reports nothing to do.</summary>
        [Fact]
        public void MissingStoreIsNoOp()
        {
            Directory.CreateDirectory(root);
            MigrationReport report = WpfNoteStorageMigrator.Migrate(root);
            Assert.Equal(MigrationStatus.NothingToMigrate, report.Status);
        }

        /// <summary>Dry-run detects the change but writes nothing.</summary>
        [Fact]
        public void DryRunWritesNothing()
        {
            string path = WriteIndex(V2PascalIndex);
            MigrationReport report = WpfNoteStorageMigrator.Migrate(root, dryRun: true);

            Assert.Equal(MigrationStatus.Migrated, report.Status);
            Assert.Equal(V2PascalIndex, File.ReadAllText(path));
            Assert.False(File.Exists(Path.Combine(root, "notes.index.wpf-backup.json")));
        }
    }
}
