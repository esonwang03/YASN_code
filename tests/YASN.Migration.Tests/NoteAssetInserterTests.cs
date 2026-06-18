using YASN.Infrastructure;
using YASN.SingleNote;

namespace YASN.Migration.Tests
{
    /// <summary>
    /// Verifies that note asset insertion copies files into the note directories and produces the
    /// expected relative Markdown snippets for images and attachments.
    /// </summary>
    public sealed class NoteAssetInserterTests : IDisposable
    {
        // A high, unique identifier keeps the test's note directories clear of any real note data.
        private readonly string noteId = "test-" + Guid.NewGuid().ToString("N");
        private readonly string sourceDir = Path.Combine(Path.GetTempPath(), "yasn-inserter-tests", Guid.NewGuid().ToString("N"));

        /// <summary>
        /// Removes the temporary source files and the note's asset directories.
        /// </summary>
        public void Dispose()
        {
            if (Directory.Exists(sourceDir))
            {
                Directory.Delete(sourceDir, recursive: true);
            }

            string assetsDir = AppPaths.GetNoteAssetsDirectory(noteId);
            if (Directory.Exists(assetsDir))
            {
                Directory.Delete(assetsDir, recursive: true);
            }

            string attachmentsDir = AppPaths.GetNoteAttachmentsDirectory(noteId);
            if (Directory.Exists(attachmentsDir))
            {
                Directory.Delete(attachmentsDir, recursive: true);
            }
        }

        /// <summary>
        /// Recognizes supported image extensions and rejects other file types.
        /// </summary>
        [Theory]
        [InlineData("photo.png", true)]
        [InlineData("photo.JPG", true)]
        [InlineData("clip.webp", true)]
        [InlineData("notes.pdf", false)]
        [InlineData("archive.zip", false)]
        public void IsImageFileClassifiesByExtension(string fileName, bool expected)
        {
            Assert.Equal(expected, NoteAssetInserter.IsImageFile(fileName));
        }

        /// <summary>
        /// Copies an image into the note assets directory and returns an embedding snippet.
        /// </summary>
        [Fact]
        public void InsertImageCopiesFileAndReturnsImageMarkdown()
        {
            string source = CreateSourceFile("diagram.png", new byte[] { 1, 2, 3 });

            string snippet = NoteAssetInserter.InsertImage(noteId, source);

            string idText = noteId;
            Assert.StartsWith($"![diagram](note-assets/{idText}/", snippet, StringComparison.Ordinal);
            Assert.EndsWith($".png){Environment.NewLine}", snippet, StringComparison.Ordinal);
            Assert.Single(Directory.GetFiles(AppPaths.GetNoteAssetsDirectory(noteId), "*.png"));
        }

        /// <summary>
        /// Copies a non-image file into the attachments directory and returns a link snippet.
        /// </summary>
        [Fact]
        public void InsertAttachmentCopiesFileAndReturnsLinkMarkdown()
        {
            string source = CreateSourceFile("report.pdf", new byte[] { 9, 9 });

            string snippet = NoteAssetInserter.InsertAttachment(noteId, source);

            string idText = noteId;
            Assert.StartsWith($"[report.pdf](note-assets/attachments/{idText}/", snippet, StringComparison.Ordinal);
            Assert.EndsWith($".pdf){Environment.NewLine}", snippet, StringComparison.Ordinal);
            Assert.Single(Directory.GetFiles(AppPaths.GetNoteAttachmentsDirectory(noteId), "*.pdf"));
        }

        /// <summary>
        /// Routes image files to embedding and other files to attachment linking.
        /// </summary>
        [Fact]
        public void BuildSnippetDispatchesByFileType()
        {
            string image = CreateSourceFile("shot.gif", new byte[] { 4 });
            string document = CreateSourceFile("data.txt", new byte[] { 5 });

            Assert.StartsWith("![shot]", NoteAssetInserter.BuildSnippet(noteId, image), StringComparison.Ordinal);
            Assert.StartsWith("[data.txt]", NoteAssetInserter.BuildSnippet(noteId, document), StringComparison.Ordinal);
        }

        /// <summary>
        /// Copies a non-image file when auto-sync is on and it is at or under the threshold.
        /// </summary>
        [Fact]
        public void BuildSnippetCopiesAttachmentUnderThreshold()
        {
            string document = CreateSourceFile("small.txt", new byte[] { 1, 2, 3 });

            string snippet = NoteAssetInserter.BuildSnippet(noteId, document, autoSyncEnabled: true, thresholdBytes: 1024);

            string idText = noteId;
            Assert.StartsWith($"[small.txt](note-assets/attachments/{idText}/", snippet, StringComparison.Ordinal);
            Assert.Single(Directory.GetFiles(AppPaths.GetNoteAttachmentsDirectory(noteId), "*.txt"));
        }

        /// <summary>
        /// Links a non-image file in place when it exceeds the threshold, without copying.
        /// </summary>
        [Fact]
        public void BuildSnippetLinksAttachmentOverThreshold()
        {
            string document = CreateSourceFile("big.bin", new byte[] { 1, 2, 3, 4, 5 });

            string snippet = NoteAssetInserter.BuildSnippet(noteId, document, autoSyncEnabled: true, thresholdBytes: 2);

            Assert.StartsWith("[big.bin](file://", snippet, StringComparison.Ordinal);
            Assert.Empty(AttachmentFiles());
        }

        /// <summary>
        /// Links a non-image file in place when auto-sync is disabled regardless of size.
        /// </summary>
        [Fact]
        public void BuildSnippetLinksAttachmentWhenAutoSyncDisabled()
        {
            string document = CreateSourceFile("doc.txt", new byte[] { 1 });

            string snippet = NoteAssetInserter.BuildSnippet(noteId, document, autoSyncEnabled: false, thresholdBytes: long.MaxValue);

            Assert.StartsWith("[doc.txt](file://", snippet, StringComparison.Ordinal);
            Assert.Empty(AttachmentFiles());
        }

        /// <summary>
        /// An image is always embedded (copied) regardless of the threshold or auto-sync flag.
        /// </summary>
        [Fact]
        public void BuildSnippetAlwaysEmbedsImages()
        {
            string image = CreateSourceFile("pic.png", new byte[] { 1, 2, 3, 4, 5 });

            string snippet = NoteAssetInserter.BuildSnippet(noteId, image, autoSyncEnabled: false, thresholdBytes: 1);

            Assert.StartsWith("![pic]", snippet, StringComparison.Ordinal);
            Assert.Single(Directory.GetFiles(AppPaths.GetNoteAssetsDirectory(noteId), "*.png"));
        }

        /// <summary>
        /// Throws when the source file is missing so callers can surface the failure.
        /// </summary>
        [Fact]
        public void InsertImageThrowsWhenSourceMissing()
        {
            string missing = Path.Combine(sourceDir, "ghost.png");
            Assert.Throws<FileNotFoundException>(() => NoteAssetInserter.InsertImage(noteId, missing));
        }

        private string CreateSourceFile(string fileName, byte[] content)
        {
            Directory.CreateDirectory(sourceDir);
            string path = Path.Combine(sourceDir, fileName);
            File.WriteAllBytes(path, content);
            return path;
        }

        private string[] AttachmentFiles()
        {
            string dir = Path.Combine(AppPaths.NoteAttachmentsRoot, noteId);
            return Directory.Exists(dir) ? Directory.GetFiles(dir) : Array.Empty<string>();
        }
    }
}
