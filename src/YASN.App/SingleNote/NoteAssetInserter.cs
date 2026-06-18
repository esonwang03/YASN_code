using YASN.Infrastructure;

namespace YASN.SingleNote
{
    /// <summary>
    /// Copies dropped, pasted, or picked files into a note's asset directories and builds the
    /// relative Markdown snippet that references them. Pure file-system logic with no UI dependency
    /// so it can be unit tested and reused across input paths (toolbar, drag-drop, clipboard).
    /// </summary>
    public static class NoteAssetInserter
    {
        private static readonly string[] ImageExtensions =
        {
            ".png", ".jpg", ".jpeg", ".gif", ".bmp", ".webp"
        };

        /// <summary>
        /// Determines whether a file path has a recognized image extension.
        /// </summary>
        /// <param name="filePath">The candidate file path.</param>
        /// <returns><c>true</c> when the extension is a supported image type.</returns>
        public static bool IsImageFile(string filePath)
        {
            string extension = Path.GetExtension(filePath);
            return ImageExtensions.Any(candidate => string.Equals(candidate, extension, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// Copies a file into the note and returns its Markdown snippet, dispatching to image
        /// embedding or attachment linking based on the file extension.
        /// </summary>
        /// <param name="noteId">The owning note identifier.</param>
        /// <param name="sourceFilePath">The absolute path of the source file.</param>
        /// <returns>A Markdown snippet ending with a trailing newline.</returns>
        public static string BuildSnippet(string noteId, string sourceFilePath)
        {
            return BuildSnippet(noteId, sourceFilePath, autoSyncEnabled: true, thresholdBytes: long.MaxValue);
        }

        /// <summary>
        /// Builds the Markdown snippet for a dropped file, copying it into the note when auto-sync is
        /// enabled and the file is at or under the threshold, otherwise linking it in place. Images
        /// are always embedded (copied).
        /// </summary>
        /// <param name="noteId">The owning note identifier.</param>
        /// <param name="sourceFilePath">The absolute path of the source file.</param>
        /// <param name="autoSyncEnabled">Whether attachments are copied into the note.</param>
        /// <param name="thresholdBytes">The maximum size in bytes for copying an attachment.</param>
        /// <returns>A Markdown snippet ending with a trailing newline.</returns>
        public static string BuildSnippet(string noteId, string sourceFilePath, bool autoSyncEnabled, long thresholdBytes)
        {
            if (IsImageFile(sourceFilePath))
            {
                return InsertImage(noteId, sourceFilePath);
            }

            EnsureSourceExists(sourceFilePath);
            long size = new FileInfo(sourceFilePath).Length;
            bool copy = autoSyncEnabled && size <= thresholdBytes;
            return copy ? InsertAttachment(noteId, sourceFilePath) : LinkAttachment(sourceFilePath);
        }

        /// <summary>
        /// Builds a Markdown link to a file in place by its absolute path, without copying it.
        /// </summary>
        /// <param name="sourceFilePath">The absolute path of the source file.</param>
        /// <returns>An attachment link Markdown snippet pointing at the original file.</returns>
        public static string LinkAttachment(string sourceFilePath)
        {
            EnsureSourceExists(sourceFilePath);

            string displayName = Path.GetFileName(sourceFilePath);
            string uri = new Uri(Path.GetFullPath(sourceFilePath)).AbsoluteUri;
            return $"[{displayName}]({uri}){Environment.NewLine}";
        }

        /// <summary>
        /// Copies an image into the note assets directory and returns an embedding Markdown snippet.
        /// </summary>
        /// <param name="noteId">The owning note identifier.</param>
        /// <param name="sourceFilePath">The absolute path of the source image.</param>
        /// <returns>An image Markdown snippet using a relative note-assets path.</returns>
        public static string InsertImage(string noteId, string sourceFilePath)
        {
            EnsureSourceExists(sourceFilePath);

            string fileName = $"{Guid.NewGuid()}{Path.GetExtension(sourceFilePath)}";
            string destinationPath = Path.Combine(AppPaths.GetNoteAssetsDirectory(noteId), fileName);
            File.Copy(sourceFilePath, destinationPath, overwrite: true);

            string altText = Path.GetFileNameWithoutExtension(sourceFilePath);
            string relativePath = $"note-assets/{noteId}/{fileName}";
            return $"![{altText}]({relativePath}){Environment.NewLine}";
        }

        /// <summary>
        /// Copies a file into the note attachments directory and returns a linking Markdown snippet.
        /// </summary>
        /// <param name="noteId">The owning note identifier.</param>
        /// <param name="sourceFilePath">The absolute path of the source file.</param>
        /// <returns>An attachment link Markdown snippet using a relative note-assets path.</returns>
        public static string InsertAttachment(string noteId, string sourceFilePath)
        {
            EnsureSourceExists(sourceFilePath);

            string displayName = Path.GetFileName(sourceFilePath);
            string fileName = $"{Guid.NewGuid()}{Path.GetExtension(sourceFilePath)}";
            string destinationPath = Path.Combine(AppPaths.GetNoteAttachmentsDirectory(noteId), fileName);
            File.Copy(sourceFilePath, destinationPath, overwrite: true);

            string relativePath = $"note-assets/attachments/{noteId}/{fileName}";
            return $"[{displayName}]({relativePath}){Environment.NewLine}";
        }

        /// <summary>
        /// Throws when the source file is missing so callers can surface a clear error.
        /// </summary>
        /// <param name="sourceFilePath">The source path to validate.</param>
        private static void EnsureSourceExists(string sourceFilePath)
        {
            if (string.IsNullOrWhiteSpace(sourceFilePath) || !File.Exists(sourceFilePath))
            {
                throw new FileNotFoundException("The source file to insert does not exist.", sourceFilePath);
            }
        }
    }
}
