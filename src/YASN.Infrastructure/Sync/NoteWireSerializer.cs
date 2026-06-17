using System.Security.Cryptography;
using System.Text.Json;

namespace YASN.Infrastructure.Sync
{
    /// <summary>
    /// Serializes <see cref="SyncNoteDocument"/> to and from the on-the-wire JSON and computes a
    /// stable content hash used by the three-way engine to detect changes.
    /// </summary>
    public static class NoteWireSerializer
    {
        private static readonly JsonSerializerOptions WriteOptions = new() { WriteIndented = true };
        private static readonly JsonSerializerOptions HashOptions = new() { WriteIndented = false };

        /// <summary>
        /// Serializes a document to indented JSON bytes for upload.
        /// </summary>
        /// <param name="document">The document to serialize.</param>
        /// <returns>UTF-8 JSON bytes.</returns>
        public static byte[] Serialize(SyncNoteDocument document)
        {
            ArgumentNullException.ThrowIfNull(document);
            return JsonSerializer.SerializeToUtf8Bytes(document, WriteOptions);
        }

        /// <summary>
        /// Deserializes a document from JSON bytes, or null when the payload is empty or invalid.
        /// </summary>
        /// <param name="bytes">The UTF-8 JSON bytes.</param>
        /// <returns>The parsed document, or null.</returns>
        public static SyncNoteDocument? Deserialize(byte[] bytes)
        {
            if (bytes is null || bytes.Length == 0)
            {
                return null;
            }

            try
            {
                return JsonSerializer.Deserialize<SyncNoteDocument>(bytes);
            }
            catch (JsonException)
            {
                return null;
            }
        }

        /// <summary>
        /// Computes a stable SHA-256 hash of a document's meaningful fields. The hash excludes the
        /// volatile <see cref="SyncNoteDocument.ModifiedAtUtc"/> so two replicas that converge on the
        /// same content compare equal regardless of edit timing.
        /// </summary>
        /// <param name="document">The document to hash.</param>
        /// <returns>Lowercase hex SHA-256.</returns>
        public static string ComputeContentHash(SyncNoteDocument document)
        {
            ArgumentNullException.ThrowIfNull(document);

            SyncNoteDocument canonical = new SyncNoteDocument
            {
                SyncKey = document.SyncKey,
                Title = document.Title,
                Content = document.Content,
                Left = document.Left,
                Top = document.Top,
                Width = document.Width,
                Height = document.Height,
                Level = document.Level,
                ShowInTaskbar = document.ShowInTaskbar,
                ReminderAt = document.ReminderAt,
                DisplayMode = document.DisplayMode,
                Deleted = document.Deleted,
                ModifiedAtUtc = default
            };

            byte[] json = JsonSerializer.SerializeToUtf8Bytes(canonical, HashOptions);
            return Convert.ToHexStringLower(SHA256.HashData(json));
        }
    }
}
