using System.Text.Json.Serialization;
using YASN.Core;

namespace YASN.Infrastructure.Sync
{
    /// <summary>
    /// Self-contained on-the-wire representation of one note (metadata + content together), stored
    /// remotely as <c>notes/{syncKey}.json</c>. The local notes.index.json + per-note .md files remain
    /// the local store; the engine projects between them and this document.
    /// </summary>
    public sealed class SyncNoteDocument
    {
        /// <summary>Gets or sets the cross-machine-stable sync key (GUID "N" form).</summary>
        [JsonPropertyName("syncKey")]
        public string SyncKey { get; set; } = string.Empty;

        /// <summary>Gets or sets the explicit title, or null when derived from content.</summary>
        [JsonPropertyName("title")]
        public string? Title { get; set; }

        /// <summary>Gets or sets the Markdown content.</summary>
        [JsonPropertyName("content")]
        public string Content { get; set; } = string.Empty;

        /// <summary>Gets or sets the window left position.</summary>
        [JsonPropertyName("left")]
        public double Left { get; set; }

        /// <summary>Gets or sets the window top position.</summary>
        [JsonPropertyName("top")]
        public double Top { get; set; }

        /// <summary>Gets or sets the window width.</summary>
        [JsonPropertyName("width")]
        public double Width { get; set; }

        /// <summary>Gets or sets the window height.</summary>
        [JsonPropertyName("height")]
        public double Height { get; set; }

        /// <summary>Gets or sets the window stacking level.</summary>
        [JsonPropertyName("level")]
        public WindowLevel Level { get; set; }

        /// <summary>Gets or sets whether the note appears in the taskbar where supported.</summary>
        [JsonPropertyName("showInTaskbar")]
        public bool ShowInTaskbar { get; set; }

        /// <summary>Gets or sets the optional reminder timestamp.</summary>
        [JsonPropertyName("reminderAt")]
        public DateTimeOffset? ReminderAt { get; set; }

        /// <summary>Gets or sets the last editor display mode used.</summary>
        [JsonPropertyName("displayMode")]
        public EditorDisplayMode DisplayMode { get; set; }

        /// <summary>Gets or sets the UTC time the note was last modified locally.</summary>
        [JsonPropertyName("modifiedAtUtc")]
        public DateTimeOffset ModifiedAtUtc { get; set; }

        /// <summary>Gets or sets whether this document is a tombstone for a deleted note.</summary>
        [JsonPropertyName("deleted")]
        public bool Deleted { get; set; }
    }
}
