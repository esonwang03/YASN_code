using System.Text.Json.Serialization;
using YASN.Core;

namespace YASN.AvaloniaNotes
{
    /// <summary>
    /// Serialized note index root stored in notes.index.json.
    /// </summary>
    internal sealed class NoteIndexData
    {
        /// <summary>
        /// Gets or sets the current index schema version.
        /// </summary>
        [JsonPropertyName("schemaVersion")]
        public int SchemaVersion { get; set; } = 5;

        /// <summary>
        /// Gets or sets serialized note metadata records.
        /// </summary>
        [JsonPropertyName("notes")]
        public List<NoteIndexEntry> Notes { get; set; } = new();
    }

    /// <summary>
    /// Serialized metadata for one note.
    /// </summary>
    internal sealed class NoteIndexEntry
    {
        /// <summary>
        /// Gets or sets the note identifier.
        /// </summary>
        [JsonPropertyName("id")]
        public int Id { get; set; }

        /// <summary>
        /// Gets or sets the cross-machine-stable sync key (GUID "N" form). Null on pre-schema-5
        /// entries; backfilled by the repository on load.
        /// </summary>
        [JsonPropertyName("syncKey")]
        public string? SyncKey { get; set; }

        /// <summary>
        /// Gets or sets the explicit user-assigned title, or null when derived from content.
        /// </summary>
        [JsonPropertyName("title")]
        public string? Title { get; set; }

        /// <summary>
        /// Gets or sets the window left position.
        /// </summary>
        [JsonPropertyName("left")]
        public double Left { get; set; } = 80;

        /// <summary>
        /// Gets or sets the window top position.
        /// </summary>
        [JsonPropertyName("top")]
        public double Top { get; set; } = 80;

        /// <summary>
        /// Gets or sets the window width.
        /// </summary>
        [JsonPropertyName("width")]
        public double Width { get; set; } = 900;

        /// <summary>
        /// Gets or sets the window height.
        /// </summary>
        [JsonPropertyName("height")]
        public double Height { get; set; } = 560;

        /// <summary>
        /// Gets or sets whether the note should be restored on startup.
        /// </summary>
        [JsonPropertyName("isOpen")]
        public bool IsOpen { get; set; } = true;

        /// <summary>
        /// Gets or sets the window stacking level.
        /// </summary>
        [JsonPropertyName("level")]
        public WindowLevel Level { get; set; } = WindowLevel.Normal;

        /// <summary>
        /// Gets or sets whether the note should appear in the taskbar where supported.
        /// </summary>
        [JsonPropertyName("showInTaskbar")]
        public bool ShowInTaskbar { get; set; } = true;

        /// <summary>
        /// Gets or sets the optional reminder timestamp.
        /// </summary>
        [JsonPropertyName("reminderAt")]
        public DateTimeOffset? ReminderAt { get; set; }

        /// <summary>
        /// Gets or sets the last editor display mode used for this note.
        /// </summary>
        [JsonPropertyName("displayMode")]
        public EditorDisplayMode DisplayMode { get; set; } = EditorDisplayMode.TextAndPreview;
    }
}
