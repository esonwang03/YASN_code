using System.Net;
using Markdig;
using YASN.Infrastructure.Markdown;

namespace YASN.SingleNote
{
    /// <summary>
    /// Builds complete HTML documents for Markdown preview rendering.
    /// </summary>
    public static class MarkdownPreviewDocument
    {
        private static readonly MarkdownPipeline Pipeline = MarkdownPipelineConfig.Create();

        /// <summary>
        /// Message posted to the host when the preview detects a double right-click.
        /// </summary>
        public const string DoubleRightClickMessage = "preview-right-double-click";

        /// <summary>
        /// Message posted to the host on a single (non-double) right-click in the preview. The host
        /// toggles the note window title bar so the chrome can be shown or hidden without leaving the
        /// preview. Deferred past the double-click threshold so a double-click focuses the editor
        /// instead of toggling.
        /// </summary>
        public const string ToggleChromeMessage = "preview-toggle-chrome";

        /// <summary>
        /// Prefix of the message posted to the host when a link in the preview is clicked. The host
        /// opens the resolved absolute URL in the operating system's default application instead of
        /// navigating the embedded WebView.
        /// </summary>
        public const string OpenLinkMessagePrefix = "preview-open-link:";

        /// <summary>
        /// Prefix of the message posted to the host when a task-list checkbox is toggled in the preview.
        /// The payload is <c>&lt;sourceLine&gt;:&lt;0|1&gt;</c> — the 0-based source line of the task
        /// item and its new checked state — so the host can rewrite the underlying Markdown.
        /// </summary>
        public const string TaskToggleMessagePrefix = "preview-task-toggle:";

        // Forwards preview right-clicks to the Avalonia host via the WebView message channel. A single
        // right-click toggles the title bar; a double right-click returns focus to the editor. The
        // single action is deferred past the double-click threshold and cancelled when a second click
        // arrives, so a double-click does not also fire the toggle. Anchor clicks are intercepted and
        // forwarded as open-link messages so attachments and external links open in the OS default app
        // rather than inside the preview WebView.
        private const string RightClickBridgeScript = """
            (() => {
              const post = (message) => {
                if (window.chrome && window.chrome.webview) {
                  window.chrome.webview.postMessage(message);
                }
              };
              const thresholdMs = 400;
              let lastRightClickAt = 0;
              let pendingSingle = 0;
              document.addEventListener('contextmenu', (event) => {
                const now = Date.now();
                const isDouble = now - lastRightClickAt <= thresholdMs;
                lastRightClickAt = now;
                event.preventDefault();
                if (isDouble) {
                  if (pendingSingle) {
                    clearTimeout(pendingSingle);
                    pendingSingle = 0;
                  }
                  post('preview-right-double-click');
                  return;
                }
                pendingSingle = setTimeout(() => {
                  pendingSingle = 0;
                  post('preview-toggle-chrome');
                }, thresholdMs);
              }, true);
              document.addEventListener('click', (event) => {
                const anchor = event.target && event.target.closest ? event.target.closest('a[href]') : null;
                if (!anchor) {
                  return;
                }
                const href = anchor.href;
                if (href && !href.startsWith('javascript:') && !href.startsWith('#')) {
                  event.preventDefault();
                  post('preview-open-link:' + href);
                }
              }, true);
            })();
            """;

        // Exposes window.__scrollToSourceLine(line, opts): aligns the preview to the block whose
        // data-source-line is the greatest value <= the editor's caret line. Source-line anchors are
        // emitted by the Markdig SourceLineExtension. opts.onlyIfOffscreen skips the scroll when the
        // target is already visible (used on caret moves, so typing within view does not jolt the
        // preview); opts.smooth animates (caret moves) vs. an instant jump (after a re-render).
        private const string ScrollSyncScript = """
            (() => {
              window.__scrollToSourceLine = (line, opts) => {
                opts = opts || {};
                const nodes = document.querySelectorAll('[data-source-line]');
                if (!nodes.length) {
                  return;
                }
                let target = nodes[0];
                for (const node of nodes) {
                  const nodeLine = parseInt(node.getAttribute('data-source-line'), 10);
                  if (!isNaN(nodeLine) && nodeLine <= line) {
                    target = node;
                  } else if (nodeLine > line) {
                    break;
                  }
                }
                const scroller = document.getElementById('page') || document.scrollingElement || document.body;
                if (opts.onlyIfOffscreen) {
                  const c = scroller.getBoundingClientRect();
                  const t = target.getBoundingClientRect();
                  if (t.top >= c.top && t.bottom <= c.bottom) {
                    return;
                  }
                }
                target.scrollIntoView({
                  block: opts.smooth ? 'center' : 'start',
                  behavior: opts.smooth ? 'smooth' : 'auto'
                });
              };
            })();
            """;

        // Makes Markdig's task-list checkboxes interactive (Markdig renders them disabled). Enables each
        // checkbox and, on change, posts the toggle to the host with the 0-based source line read from
        // the nearest data-source-line ancestor (the list-item block), so the host rewrites the source.
        private const string TaskCheckboxScript = """
            (() => {
              const post = (message) => {
                if (window.chrome && window.chrome.webview) {
                  window.chrome.webview.postMessage(message);
                }
              };
              const boxes = document.querySelectorAll('li.task-list-item > input[type="checkbox"]');
              for (const box of boxes) {
                box.disabled = false;
                box.addEventListener('change', () => {
                  let node = box.closest('[data-source-line]');
                  if (!node) {
                    return;
                  }
                  const line = parseInt(node.getAttribute('data-source-line'), 10);
                  if (isNaN(line)) {
                    return;
                  }
                  post('preview-task-toggle:' + line + ':' + (box.checked ? '1' : '0'));
                });
              }
            })();
            """;

        /// <summary>
        /// Renders Markdown to a standalone HTML document with the selected style sheet.
        /// </summary>
        /// <param name="markdown">The Markdown content to render.</param>
        /// <param name="styleHref">The style-sheet URL included in the document.</param>
        /// <param name="baseHref">
        /// Optional document base URL so relative asset links (for example embedded note images)
        /// resolve against the data root rather than the cached HTML file location. Omitted when empty.
        /// </param>
        /// <returns>A complete HTML document.</returns>
        public static string Render(string markdown, string styleHref, string baseHref = "")
        {
            string body = Markdown.ToHtml(markdown ?? string.Empty, Pipeline);
            string encodedStyleHref = WebUtility.HtmlEncode(styleHref ?? string.Empty);
            string baseTag = string.IsNullOrEmpty(baseHref)
                ? string.Empty
                : $"  <base href=\"{WebUtility.HtmlEncode(baseHref)}\">\n";

            return $"""
                <!doctype html>
                <html>
                <head>
                  <meta charset="utf-8">
                  <meta name="viewport" content="width=device-width, initial-scale=1">
                {baseTag}  <link rel="stylesheet" href="{encodedStyleHref}">
                </head>
                <body>
                  <main id="page">
                {body}
                  </main>
                  <script>{RightClickBridgeScript}</script>
                  <script>{ScrollSyncScript}</script>
                  <script>{TaskCheckboxScript}</script>
                </body>
                </html>
                """;
        }
    }
}
