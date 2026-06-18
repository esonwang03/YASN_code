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
            AvaloniaNoteDocument groceries = notes.Single(n => n.Title == "Groceries");
            Assert.Equal(WindowLevel.TopMost, groceries.Level);
            Assert.Equal("# groceries", groceries.Content);
        }

        /// <summary>Titles are preserved as the explicit stored title.</summary>
        [Fact]
        public void PreservesTitleAsStoredTitle()
        {
            WriteIndex(V2PascalIndex);
            WpfNoteStorageMigrator.Migrate(root);

            AvaloniaNoteDocument note = new NoteRepository(root).LoadAll().Single(n => n.Title == "Groceries");
            Assert.Equal("Groceries", note.StoredTitle);
        }

        /// <summary>Legacy display-mode strings map to the new integer enum.</summary>
        [Fact]
        public void MapsDisplayModeStrings()
        {
            WriteIndex(V2PascalIndex);
            WpfNoteStorageMigrator.Migrate(root);

            IReadOnlyList<AvaloniaNoteDocument> notes = new NoteRepository(root).LoadAll();
            Assert.Equal(EditorDisplayMode.TextOnly, notes.Single(n => n.Title == "Groceries").DisplayMode);
            Assert.Equal(EditorDisplayMode.PreviewOnly, notes.Single(n => n.Title == "Plans").DisplayMode);
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

        /// <summary>
        /// A v5 index with integer ids is upgraded to GUID ids and its markdown files are renamed to
        /// match, so the note content still loads under the new id. This is the int→GUID collapse: the
        /// per-note sync key becomes the canonical id.
        /// </summary>
        [Fact]
        public void MigratesIntegerIdsToGuidAndRenamesMarkdown()
        {
            WriteIndex("""
                {
                  "schemaVersion": 5,
                  "notes": [
                    { "id": 1, "syncKey": "key-one", "title": "First", "isOpen": true },
                    { "id": 2, "syncKey": "key-two", "title": "Second", "isOpen": true }
                  ]
                }
                """);
            Directory.CreateDirectory(Path.Combine(root, "notes"));
            File.WriteAllText(Path.Combine(root, "notes", "1.md"), "# first body");
            File.WriteAllText(Path.Combine(root, "notes", "2.md"), "# second body");

            MigrationReport report = WpfNoteStorageMigrator.Migrate(root);

            Assert.Equal(MigrationStatus.Migrated, report.Status);
            // The numeric markdown files are renamed to the GUID (here, the sync key) ids.
            Assert.False(File.Exists(Path.Combine(root, "notes", "1.md")));
            Assert.True(File.Exists(Path.Combine(root, "notes", "key-one.md")));

            IReadOnlyList<AvaloniaNoteDocument> notes = new NoteRepository(root).LoadAll();
            AvaloniaNoteDocument first = notes.Single(n => n.Title == "First");
            Assert.Equal("key-one", first.Id);
            Assert.Equal("key-one", first.SyncKey);
            Assert.Equal("# first body", first.Content);
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
            AvaloniaNoteDocument note = new NoteRepository(root).LoadAll().Single();
            Assert.Equal("inline body text", note.Content);
            Assert.Equal("inline body text", File.ReadAllText(Path.Combine(root, "notes", $"{note.Id}.md")));
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
