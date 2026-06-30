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

        /// <summary>
        /// Prefix of the message posted to the host when a preview block is double-clicked. The payload
        /// is the block's 0-based <c>data-source-line</c>, so the host can move the editor caret to that
        /// line — the reverse of the caret-driven preview scroll sync.
        /// </summary>
        public const string FocusEditorLineMessagePrefix = "preview-focus-line:";

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

        // Makes Markdig's task-list checkboxes interactive (Markdig renders them disabled). Defines a
        // reusable wiring function on window so it can run on first load and again after each
        // incremental body patch (innerHTML replacement destroys the prior listeners). Enables each
        // checkbox and, on change, posts the toggle to the host with the 0-based source line read from
        // the nearest data-source-line ancestor (the list-item block), so the host rewrites the source.
        private const string TaskCheckboxScript = """
            (() => {
              const post = (message) => {
                if (window.chrome && window.chrome.webview) {
                  window.chrome.webview.postMessage(message);
                }
              };
              window.__wireTaskCheckboxes = () => {
                const boxes = document.querySelectorAll('li.task-list-item input[type="checkbox"]');
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
              };
              window.__wireTaskCheckboxes();
            })();
            """;

        // Closes the caret-sync loop: a double-click on any rendered block posts its 0-based
        // data-source-line to the host so the editor caret jumps to that line. Uses the capture phase
        // and the nearest data-source-line ancestor so clicks on inline children (text, spans) still
        // resolve to their block. Left single-clicks are untouched, so text selection still works.
        // Attached to document, so it survives incremental body patches.
        private const string EditorJumpBridgeScript = """
            (() => {
              const post = (message) => {
                if (window.chrome && window.chrome.webview) {
                  window.chrome.webview.postMessage(message);
                }
              };
              document.addEventListener('dblclick', (event) => {
                const node = event.target && event.target.closest
                  ? event.target.closest('[data-source-line]')
                  : null;
                if (!node) {
                  return;
                }
                const line = parseInt(node.getAttribute('data-source-line'), 10);
                if (isNaN(line)) {
                  return;
                }
                post('preview-focus-line:' + line);
              }, true);
            })();
            """;

        // Enables incremental preview updates without a full document reload (which tears down and
        // white-repaints the WebView, the source of the typing flicker). Replaces only the rendered
        // body inside #page, then re-runs the per-element wiring (task checkboxes, math) that the
        // innerHTML replacement discarded. The document-level bridges (right-click, scroll-sync,
        // editor-jump) are attached to document and persist across patches.
        private const string BodyPatchScript = """
            (() => {
              window.__setBody = (html) => {
                const page = document.getElementById('page');
                if (!page) {
                  return;
                }
                page.innerHTML = html;
                if (typeof window.__wireTaskCheckboxes === 'function') {
                  window.__wireTaskCheckboxes();
                }
                if (typeof window.__renderMath === 'function') {
                  window.__renderMath();
                }
              };
            })();
            """;

        // Renders the KaTeX auto-render pass after the document loads, typesetting the spans/divs the
        // Markdig Mathematics extension emits (inline \( \) and display \[ \]). KaTeX, its CSS, and its
        // fonts are vendored under style/katex so math renders fully offline. throwOnError:false leaves
        // malformed math as its source text rather than blanking the preview. Defines a reusable
        // window.__renderMath so an incremental body patch can re-typeset the new content.
        private const string MathRenderScript = """
            (() => {
              window.__renderMath = () => {
                if (typeof renderMathInElement !== 'function') {
                  return;
                }
                renderMathInElement(document.getElementById('page') || document.body, {
                  delimiters: [
                    { left: '\\[', right: '\\]', display: true },
                    { left: '\\(', right: '\\)', display: false }
                  ],
                  throwOnError: false
                });
              };
              window.__renderMath();
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
        /// <param name="katexBaseHref">
        /// Optional base URL of the vendored KaTeX asset folder (containing <c>katex.min.css</c>,
        /// <c>katex.min.js</c>, and <c>contrib/auto-render.min.js</c>). When empty, math falls back to its
        /// source text and no KaTeX assets are referenced.
        /// </param>
        /// <returns>A complete HTML document.</returns>
        public static string Render(string markdown, string styleHref, string baseHref = "", string katexBaseHref = "")
        {
            string body = RenderBody(markdown);
            string encodedStyleHref = WebUtility.HtmlEncode(styleHref ?? string.Empty);
            string baseTag = string.IsNullOrEmpty(baseHref)
                ? string.Empty
                : $"  <base href=\"{WebUtility.HtmlEncode(baseHref)}\">\n";

            (string katexHead, string katexScripts) = BuildKatexFragments(katexBaseHref);

            return $"""
                <!doctype html>
                <html>
                <head>
                  <meta charset="utf-8">
                  <meta name="viewport" content="width=device-width, initial-scale=1">
                {baseTag}  <link rel="stylesheet" href="{encodedStyleHref}">
                {katexHead}</head>
                <body>
                  <main id="page">
                {body}
                  </main>
                  <script>{RightClickBridgeScript}</script>
                  <script>{ScrollSyncScript}</script>
                  <script>{TaskCheckboxScript}</script>
                  <script>{EditorJumpBridgeScript}</script>
                  <script>{BodyPatchScript}</script>
                {katexScripts}</body>
                </html>
                """;
        }

        /// <summary>
        /// Renders just the Markdown body HTML (the inner content of <c>#page</c>), for incremental
        /// preview updates that replace the body without reloading the whole document. Uses the same
        /// Markdig pipeline as <see cref="Render"/> so the output is identical to a full render's body.
        /// </summary>
        /// <param name="markdown">The Markdown content to render.</param>
        /// <returns>The rendered body HTML fragment.</returns>
        public static string RenderBody(string markdown)
        {
            return Markdown.ToHtml(markdown ?? string.Empty, Pipeline);
        }

        /// <summary>
        /// Builds the KaTeX <c>&lt;link&gt;</c>/<c>&lt;script&gt;</c> fragments for the document head and
        /// footer, or empty strings when no KaTeX base is supplied (math then shows as source text).
        /// </summary>
        /// <param name="katexBaseHref">The base URL of the vendored KaTeX folder, or empty to disable.</param>
        /// <returns>The head fragment (stylesheet link) and the footer fragment (scripts + render call).</returns>
        private static (string Head, string Scripts) BuildKatexFragments(string katexBaseHref)
        {
            if (string.IsNullOrEmpty(katexBaseHref))
            {
                return (string.Empty, string.Empty);
            }

            string root = katexBaseHref.EndsWith('/') ? katexBaseHref : katexBaseHref + "/";
            string cssHref = WebUtility.HtmlEncode(root + "katex.min.css");
            string jsHref = WebUtility.HtmlEncode(root + "katex.min.js");
            string autoRenderHref = WebUtility.HtmlEncode(root + "contrib/auto-render.min.js");

            string head = $"  <link rel=\"stylesheet\" href=\"{cssHref}\">\n";
            string scripts = $"  <script src=\"{jsHref}\"></script>\n" +
                $"  <script src=\"{autoRenderHref}\"></script>\n" +
                $"  <script>{MathRenderScript}</script>\n";
            return (head, scripts);
        }
    }
}
