# YASN preview styling

`default.css` is the stylesheet applied to the Markdown **preview** that renders
inside each `FloatingNoteWindow` (via WebView2). This document explains the design
system and maps every CSS selector to the Markdown construct that produces it, so
that authoring a new theme is a matter of overriding tokens rather than guessing
which classes the pipeline emits.

## How a preview is built

1. `MarkdownPipelineConfig.Create()` builds the Markdig pipeline:
   `UseAdvancedExtensions()` + `UseHexColorText()` + `UseNoteReminders()`.
2. `MarkdownPreviewDocument.Render(...)` converts the note's Markdown to an HTML
   fragment, wraps it in `<main id="page">`, and links the resolved stylesheet.
3. `PreviewStyleManager` materialises bundled styles into the data directory and
   resolves the configured `note.previewStyle` (default `default.css`).

Because the document body carries **no theme class**, the stylesheet must theme
itself. It does so from `:root` plus `@media (prefers-color-scheme: dark)`. The
`body.theme-light` / `body.theme-dark` classes are kept as explicit overrides for
a host that later chooses to pin a theme, but nothing depends on them being set.

## Design system

Identity: **"ink & marker" — an editorial sticky note.**

- **Type.** Serif `Constantia`/`Cambria` for the display/heading voice; sans
  `Segoe UI Variable` for body; `Cascadia Code` for monospace. All ship on
  Windows. CJK falls back to `Microsoft YaHei UI` throughout, matching the app's
  bilingual UI.
- **Colour.** Slate *ink* (`--fg`) on the translucent note paper, one teal
  *accent* (`--accent`/`--link`) for links and structural rules, and a single
  amber *highlighter* (`--marker`) reserved for `==mark==`. Every surface fill
  uses alpha so the preview layers correctly over each note's own colour and the
  desktop showing through the translucent window.
- **Signature.** `==highlight==` is rendered as a marker swipe (a clipped linear
  gradient with `box-decoration-break: clone`) that bleeds slightly past the text
  edge. It is the one bold element; everything else stays quiet.

## Tokens

Override these to retheme. Defaults are in `:root`; dark values mirror them under
`prefers-color-scheme: dark` and `.theme-dark`.

| Token | Role |
| --- | --- |
| `--font-display` | Headings, table headers, definition terms, marker math |
| `--font-body` | Body text, small-caps `h5`/`h6` |
| `--font-mono` | `code`, `pre`, `kbd`, diagram source |
| `--fg` | Primary text |
| `--muted` | Secondary text (captions, footnotes, blockquotes) |
| `--hair` | Hairline rules and borders |
| `--page-bg` | `#page` translucent paper fill |
| `--panel` | Subtle fills (blockquote, table header, admonitions) |
| `--code-bg` | Code background |
| `--accent` | Links, structural accents, focus ring |
| `--link` | Link text |
| `--marker` / `--marker-edge` | `==mark==` highlighter fill / warning edge |
| `--danger` | Invalid/destructive state |

## Construct → selector map

Grouped by the Markdig extension that produces the markup. Constructs without an
explicit selector inherit base element styling.

### CommonMark core
| Markdown | HTML | Selector |
| --- | --- | --- |
| `# … ######` | `<h1>`–`<h6>` | `h1`…`h6` (serif; `h1`/`h2` underlined; `h5`/`h6` small-caps) |
| paragraph | `<p>` | `p` |
| `- ` / `1. ` | `<ul>` / `<ol>` | `ul`, `ol`, `li` |
| `> ` | `<blockquote>` | `blockquote` (accent spine) |
| ` ```lang ` | `<pre><code>` | `pre`, `pre code` |
| `` `code` `` | `<code>` | `code` |
| `---` | `<hr>` | `hr` |
| `[t](url)` | `<a>` | `a` (underline grows on hover) |
| `![alt](src)` | `<img>` | `img` (responsive, centred) |
| `**b**` / `*i*` | `<strong>` / `<em>` | `strong`, `em` |

### PipeTables / GridTables
`| a | b |` → `<table><thead><tbody>`. Selectors: `table`, `th`, `td`,
`thead th` (serif header on `--panel`), zebra `tbody tr:nth-child(even)`.

### EmphasisExtras
| Markdown | HTML | Selector |
| --- | --- | --- |
| `~~del~~` | `<del>` / `<s>` | `del, s` |
| `++ins++` | `<ins>` | `ins` |
| `~sub~` | `<sub>` | `sub` |
| `^sup^` | `<sup>` | `sup` |
| `==mark==` | `<mark>` | `mark` — **signature** marker swipe |

### TaskLists
`- [ ]` / `- [x]` → `<li class="task-list-item">` with a disabled
`<input type="checkbox">`. Selectors: `li.task-list-item`, the checkbox
(`accent-color: var(--accent)`), and checked-item muting.

### ListExtras
Ordered lists with `a.`/`A.`/`i.`/`I.` markers carry a `type` attribute.
Selectors: `ol[type="a"|"A"|"i"|"I"]`.

### DefinitionLists
`Term` / `: definition` → `<dl><dt><dd>`. Selectors: `dl`, `dt` (serif), `dd`.

### Footnotes
`[^1]` and `[^1]: …` → inline `<a class="footnote-ref">` and a trailing
`<div class="footnotes">` (ordered list + back-references). Selectors:
`.footnote-ref`, `.footnotes`, `.footnote-back-ref`.

### Figures / Footers / Citations
`^^^` figure → `<figure><figcaption>`; `^^ ` footer → `<footer>`;
`""cite""` → `<cite>`. Selectors: `figure`, `figcaption`, `footer`/`.footer`,
`cite`.

### Abbreviations
`*[HTML]: …` → `<abbr title="…">`. Selector: `abbr[title]` (dotted underline,
help cursor).

### Mathematics
`$inline$` / `$$block$$` → `<span class="math">` / `<div class="math">`. KaTeX is
bundled under `style/katex/` (CSS, JS, fonts) and typesets these at preview load,
fully offline. The `.math` CSS rule is only a fallback for when those assets are
missing.

### MediaLinks
Recognised media URLs → `<iframe>`/`<video>`/`<audio>`. Selectors constrain them
to a responsive, centred frame.

### CustomContainers
`:::name … :::` → `<div class="name">`. The four documented admonition names are
`note`, `tip`, `warning`, `danger` (accent spine + `--panel` fill, colour-coded).
Other names render as a neutral block — add a rule keyed on the class to style it.

### Diagrams
` ```mermaid ` / ` ```nomnoml ` → `<div class="mermaid">` / `<div class="nomnoml">`.
Not executed in the preview, so the source is shown in a dashed monospaced block
rather than a blank gap.

## YASN custom extensions

### Reminder badges (`UseNoteReminders`)
Syntax `[!display][control]{cron}{content}` → `<span class="yasn-reminder">` with
a `🔔` icon (`.yasn-reminder-icon`) and a tooltip. State classes:

| Class | When | Style |
| --- | --- | --- |
| `yasn-reminder` | enabled, valid cron | accent pill |
| `yasn-reminder-disabled` | control disables it | struck through, muted |
| `yasn-reminder-invalid` | cron fails to parse | `--danger` outline |

### Hex-colour text (`UseHexColorText`)
Syntax `{#RRGGBB|text}` or `{#colorName|text}` → `<span style="color:#…">` with an
inline style. No class is emitted, so there is nothing to theme — the author's
colour always wins.

## Authoring a new theme

1. Copy `default.css` to `style/<name>.css` in the data directory.
2. Override the tokens in `:root` (and the `prefers-color-scheme: dark` block).
   Avoid touching selectors unless a construct needs a genuinely different layout.
3. Keep surface fills alpha-based so the note's own colour and the desktop behind
   the translucent window remain visible.
4. Select it via the `note.previewStyle` setting.

The accessibility floor (`:focus-visible` ring, `prefers-reduced-motion`) lives at
the end of the sheet — preserve it when adapting.
