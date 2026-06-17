using YASN.AvaloniaNotes;

namespace YASN.Infrastructure.Sync
{
    /// <summary>
    /// Maps between the local <see cref="AvaloniaNoteDocument"/> and the wire <see cref="SyncNoteDocument"/>.
    /// Window-open state is intentionally excluded: it is per-machine and must not propagate.
    /// </summary>
    public static class NoteSyncMapper
    {
        /// <summary>
        /// Projects a local note onto its wire representation.
        /// </summary>
        /// <param name="note">The local note.</param>
        /// <param name="modifiedAtUtc">The local modification time to stamp.</param>
        /// <returns>The wire document.</returns>
        public static SyncNoteDocument ToWire(AvaloniaNoteDocument note, DateTimeOffset modifiedAtUtc)
        {
            ArgumentNullException.ThrowIfNull(note);
            return new SyncNoteDocument
            {
                SyncKey = note.SyncKey,
                Title = string.IsNullOrWhiteSpace(note.StoredTitle) ? null : note.StoredTitle,
                Content = note.Content,
                Left = note.Left,
                Top = note.Top,
                Width = note.Width,
                Height = note.Height,
                Level = note.Level,
                ShowInTaskbar = note.ShowInTaskbar,
                ReminderAt = note.ReminderAt,
                DisplayMode = note.DisplayMode,
                ModifiedAtUtc = modifiedAtUtc,
                Deleted = false
            };
        }

        /// <summary>
        /// Builds a local note from a wire document. The id is left at zero for the caller to assign;
        /// the note is created closed so it does not auto-open on another machine.
        /// </summary>
        /// <param name="document">The wire document.</param>
        /// <returns>The local note.</returns>
        public static AvaloniaNoteDocument ToDocument(SyncNoteDocument document)
        {
            ArgumentNullException.ThrowIfNull(document);
            return new AvaloniaNoteDocument
            {
                SyncKey = document.SyncKey,
                StoredTitle = document.Title,
                Content = document.Content,
                Left = document.Left,
                Top = document.Top,
                Width = document.Width,
                Height = document.Height,
                IsOpen = false,
                Level = document.Level,
                ShowInTaskbar = document.ShowInTaskbar,
                ReminderAt = document.ReminderAt,
                DisplayMode = document.DisplayMode
            };
        }

        /// <summary>
        /// Builds a tombstone wire document for a deleted note.
        /// </summary>
        /// <param name="syncKey">The deleted note's sync key.</param>
        /// <param name="modifiedAtUtc">The deletion time to stamp.</param>
        /// <returns>A tombstone document.</returns>
        public static SyncNoteDocument Tombstone(string syncKey, DateTimeOffset modifiedAtUtc)
        {
            return new SyncNoteDocument { SyncKey = syncKey, Deleted = true, ModifiedAtUtc = modifiedAtUtc };
        }
    }
}
