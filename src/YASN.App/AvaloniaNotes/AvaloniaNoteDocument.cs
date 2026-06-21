using YASN.Core;

namespace YASN.AvaloniaNotes
{
    /// <summary>
    /// Represents note content and window metadata used by the Avalonia shell.
    /// </summary>
    public sealed class AvaloniaNoteDocument
    {
        /// <summary>
        /// Gets or sets the stable note identifier (GUID "N" form). Normally equal to
        /// <see cref="SyncKey"/>; the two differ only for a conflict copy, which keeps the remote
        /// note's <see cref="SyncKey"/> but gets a fresh local id.
        /// </summary>
        public string Id { get; set; } = Guid.NewGuid().ToString("N");

        /// <summary>
        /// Gets or sets the cross-machine-stable sync key (GUID "N" form). Identifies the same logical
        /// note across devices and survives sync. Two rows sharing one key represent an unresolved
        /// conflict.
        /// </summary>
        public string SyncKey { get; set; } = Guid.NewGuid().ToString("N");

        /// <summary>
        /// Gets or sets the Markdown note content.
        /// </summary>
        public string Content { get; set; } = "# Untitled note";

        /// <summary>
        /// Gets or sets the explicit user-assigned title. When null or blank the display
        /// <see cref="Title"/> falls back to the content-derived title.
        /// </summary>
        public string? StoredTitle { get; set; }

        /// <summary>
        /// Gets the display title: the stored title when set, otherwise derived from the content.
        /// </summary>
        public string Title => string.IsNullOrWhiteSpace(StoredTitle)
            ? NoteTitleFormatter.GetTitle(Content)
            : StoredTitle!;

        /// <summary>
        /// Gets or sets the window left position.
        /// </summary>
        public double Left { get; set; } = 80;

        /// <summary>
        /// Gets or sets the window top position.
        /// </summary>
        public double Top { get; set; } = 80;

        /// <summary>
        /// Gets or sets the window width.
        /// </summary>
        public double Width { get; set; } = 900;

        /// <summary>
        /// Gets or sets the window height.
        /// </summary>
        public double Height { get; set; } = 560;

        /// <summary>
        /// Gets or sets whether the note should be restored on startup.
        /// </summary>
        public bool IsOpen { get; set; } = true;

        /// <summary>
        /// Gets or sets the requested window stacking level.
        /// </summary>
        public WindowLevel Level { get; set; } = WindowLevel.Normal;

        /// <summary>
        /// Gets or sets whether the note should appear in the taskbar where supported.
        /// </summary>
        public bool ShowInTaskbar { get; set; } = true;

        /// <summary>
        /// Gets or sets the optional reminder timestamp.
        /// </summary>
        public DateTimeOffset? ReminderAt { get; set; }

        /// <summary>
        /// Gets or sets the UTC time the note content was last edited. Drives the "open most recently
        /// edited note" command. Window moves, resizes, level changes, and title-only renames do not
        /// update it; only content changes do. Null on notes that predate this field; the repository
        /// backfills it from the content file's last-write time on load.
        /// </summary>
        public DateTimeOffset? ContentModifiedAt { get; set; }

        /// <summary>
        /// Gets or sets the last editor display mode used for this note.
        /// </summary>
        public EditorDisplayMode DisplayMode { get; set; } = EditorDisplayMode.TextAndPreview;
    }
}
