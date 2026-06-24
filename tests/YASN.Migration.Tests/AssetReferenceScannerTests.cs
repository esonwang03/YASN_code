using YASN.Infrastructure.Sync;

namespace YASN.Migration.Tests
{
    /// <summary>
    /// Verifies the scan that finds the <c>note-assets/…</c> paths a note's Markdown references, which
    /// drives asset replication during sync.
    /// </summary>
    public sealed class AssetReferenceScannerTests
    {
        [Fact]
        public void FindsImageAndAttachmentPaths()
        {
            const string content = "![pic](note-assets/42/a1b2.png)\n" +
                                   "[file](note-assets/attachments/42/c3d4.pdf)";

            IReadOnlyCollection<string> paths = AssetReferenceScanner.Collect([content]);

            Assert.Contains("note-assets/42/a1b2.png", paths);
            Assert.Contains("note-assets/attachments/42/c3d4.pdf", paths);
            Assert.Equal(2, paths.Count);
        }

        [Fact]
        public void DeduplicatesRepeatedPathsAcrossNotes()
        {
            string a = "![x](note-assets/1/img.png)";
            string b = "see ![x again](note-assets/1/img.png)";

            IReadOnlyCollection<string> paths = AssetReferenceScanner.Collect([a, b]);

            Assert.Single(paths);
        }

        [Fact]
        public void IgnoresOrdinaryExternalLinks()
        {
            const string content = "[site](https://example.com/page.html)\n" +
                                   "![ok](note-assets/7/keep.png)";

            IReadOnlyCollection<string> paths = AssetReferenceScanner.Collect([content]);

            Assert.Equal(["note-assets/7/keep.png"], paths);
        }

        [Fact]
        public void EmptyAndNullContentsYieldNothing()
        {
            IReadOnlyCollection<string> paths = AssetReferenceScanner.Collect([null, string.Empty, "no assets here"]);

            Assert.Empty(paths);
        }

        [Fact]
        public void IgnoresLinkedAttachmentFileUris()
        {
            // Over-threshold attachments are linked in place as absolute file:// URIs
            // (NoteAssetInserter.LinkAttachment), not copied under note-assets/. They are machine-local
            // and must not sync; only the copied image and copied attachment should be collected.
            string content =
                "[big.zip](file:///C:/Users/me/Downloads/big.zip)\n" +
                "![pasted](note-assets/3/img.png)\n" +
                "[small.pdf](note-assets/attachments/3/doc.pdf)";

            IReadOnlyCollection<string> paths = AssetReferenceScanner.Collect([content]);

            Assert.DoesNotContain(paths, p => p.Contains("big.zip", StringComparison.OrdinalIgnoreCase));
            Assert.Contains("note-assets/3/img.png", paths);
            Assert.Contains("note-assets/attachments/3/doc.pdf", paths);
            Assert.Equal(2, paths.Count);
        }
    }
}
