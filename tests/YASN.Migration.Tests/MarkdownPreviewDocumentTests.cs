using YASN.SingleNote;

namespace YASN.Migration.Tests
{
    /// <summary>
    /// Verifies Markdown-to-HTML preview document generation.
    /// </summary>
    public sealed class MarkdownPreviewDocumentTests
    {
        /// <summary>
        /// Converts Markdown into a complete HTML document.
        /// </summary>
        [Fact]
        public void RenderCreatesCompleteHtmlDocument()
        {
            string html = MarkdownPreviewDocument.Render("# Title\n\n- item", "style/default.css");

            Assert.Contains("<!doctype html>", html, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("<h1", html, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("Title", html, StringComparison.Ordinal);
            Assert.Contains("style/default.css", html, StringComparison.Ordinal);
        }

        /// <summary>
        /// Embeds the right-click bridge script that posts messages to the host.
        /// </summary>
        [Fact]
        public void RenderEmbedsRightClickBridge()
        {
            string html = MarkdownPreviewDocument.Render("body", "style/default.css");

            Assert.Contains("window.chrome.webview.postMessage", html, StringComparison.Ordinal);
            Assert.Contains(MarkdownPreviewDocument.DoubleRightClickMessage, html, StringComparison.Ordinal);
        }

        /// <summary>
        /// Emits a base href so relative asset links resolve against the data root when provided.
        /// </summary>
        [Fact]
        public void RenderEmitsBaseHrefWhenProvided()
        {
            string withBase = MarkdownPreviewDocument.Render("body", "style/default.css", "file:///C:/data/");
            string withoutBase = MarkdownPreviewDocument.Render("body", "style/default.css");

            Assert.Contains("<base href=\"file:///C:/data/\">", withBase, StringComparison.Ordinal);
            Assert.DoesNotContain("<base", withoutBase, StringComparison.Ordinal);
        }

        /// <summary>
        /// Embeds the anchor-click bridge so links open via the host instead of the WebView.
        /// </summary>
        [Fact]
        public void RenderEmbedsOpenLinkBridge()
        {
            string html = MarkdownPreviewDocument.Render("[file](note-assets/attachments/1/x.pdf)", "style/default.css");

            Assert.Contains("preview-open-link:", html, StringComparison.Ordinal);
            Assert.Contains("a[href]", html, StringComparison.Ordinal);
            Assert.StartsWith("preview-open-link:", MarkdownPreviewDocument.OpenLinkMessagePrefix, StringComparison.Ordinal);
        }

        /// <summary>
        /// Annotates rendered blocks with 0-based source-line anchors used by cursor scroll-sync.
        /// </summary>
        [Fact]
        public void RenderAnnotatesBlocksWithSourceLines()
        {
            // Lines (0-based): 0 "# Title", 1 blank, 2 "para", 3 blank, 4 "- item".
            string html = MarkdownPreviewDocument.Render("# Title\n\npara\n\n- item", "style/default.css");

            Assert.Contains("data-source-line=\"0\"", html, StringComparison.Ordinal);
            Assert.Contains("data-source-line=\"2\"", html, StringComparison.Ordinal);
            Assert.Contains("data-source-line=\"4\"", html, StringComparison.Ordinal);
        }

        /// <summary>
        /// Embeds the scroll-sync function the host invokes when the editor caret moves.
        /// </summary>
        [Fact]
        public void RenderEmbedsScrollSyncFunction()
        {
            string html = MarkdownPreviewDocument.Render("# Title", "style/default.css");

            Assert.Contains("window.__scrollToSourceLine", html, StringComparison.Ordinal);
            Assert.Contains("data-source-line", html, StringComparison.Ordinal);
            Assert.Contains("scrollIntoView", html, StringComparison.Ordinal);
        }

        /// <summary>
        /// Embeds the double-click bridge so the host can move the editor caret to a preview block's
        /// source line — the reverse of the caret-driven preview scroll sync.
        /// </summary>
        [Fact]
        public void RenderEmbedsEditorJumpBridge()
        {
            string html = MarkdownPreviewDocument.Render("# Title", "style/default.css");

            Assert.Contains("dblclick", html, StringComparison.Ordinal);
            Assert.Contains(MarkdownPreviewDocument.FocusEditorLineMessagePrefix, html, StringComparison.Ordinal);
            Assert.StartsWith("preview-focus-line:", MarkdownPreviewDocument.FocusEditorLineMessagePrefix, StringComparison.Ordinal);
        }
    }
}
