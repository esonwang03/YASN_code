using System.Text.Json.Serialization;

namespace YASN.Migration
{
    /// <summary>
    /// Read-side index wrapper. Property names are matched case-insensitively, so this binds both the
    /// old WPF PascalCase index and the new Avalonia camelCase index.
    /// </summary>
    internal sealed class LegacyIndex
    {
        public int SchemaVersion { get; set; } = 1;

        public List<LegacyNote> Notes { get; set; } = new();
    }

    /// <summary>
    /// Read-side note metadata covering the union of legacy (v1/v2) and current fields. Fields that
    /// the new schema dropped (dark mode, title-bar color, background image) are intentionally absent.
    /// </summary>
    internal sealed class LegacyNote
    {
        public int Id { get; set; }

        public string? SyncKey { get; set; }

        public string? Title { get; set; }

        /// <summary>Inline content present only in v1 stores; used to backfill a missing markdown file.</summary>
        public string? Content { get; set; }

        public int Level { get; set; }

        public double Left { get; set; }

        public double Top { get; set; }

        public double Width { get; set; }

        public double Height { get; set; }

        public bool IsOpen { get; set; }

        /// <summary>Legacy string editor mode (e.g. "textOnly"); mapped to the new integer enum.</summary>
        public string? LastEditorDisplayMode { get; set; }
    }

    /// <summary>
    /// Write-side index root. Mirrors the Avalonia build's <c>notes.index.json</c> exactly: camelCase
    /// keys and numeric enums, indented.
    /// </summary>
    internal sealed class NewIndex
    {
        [JsonPropertyName("schemaVersion")]
        public int SchemaVersion { get; set; } = WpfNoteStorageMigrator.CurrentSchemaVersion;

        [JsonPropertyName("notes")]
        public List<NewNote> Notes { get; set; } = new();
    }

    /// <summary>
    /// Write-side note metadata. Property order and names match the Avalonia <c>NoteIndexEntry</c> so
    /// the produced file is byte-compatible with what the app writes itself.
    /// </summary>
    internal sealed class NewNote
    {
        [JsonPropertyName("id")]
        public int Id { get; set; }

        [JsonPropertyName("syncKey")]
        public string SyncKey { get; set; } = string.Empty;

        [JsonPropertyName("title")]
        public string? Title { get; set; }

        [JsonPropertyName("left")]
        public double Left { get; set; }

        [JsonPropertyName("top")]
        public double Top { get; set; }

        [JsonPropertyName("width")]
        public double Width { get; set; }

        [JsonPropertyName("height")]
        public double Height { get; set; }

        [JsonPropertyName("isOpen")]
        public bool IsOpen { get; set; }

        [JsonPropertyName("level")]
        public int Level { get; set; }

        [JsonPropertyName("showInTaskbar")]
        public bool ShowInTaskbar { get; set; } = true;

        [JsonPropertyName("reminderAt")]
        public DateTimeOffset? ReminderAt { get; set; }

        [JsonPropertyName("displayMode")]
        public int DisplayMode { get; set; } = 2;
    }
}
