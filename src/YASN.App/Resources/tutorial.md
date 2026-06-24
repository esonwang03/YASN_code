# Welcome to YASN

This note is a living cheat sheet. It shows every Markdown feature the preview
understands. Switch the editor mode (the view button in the title bar) between
**Edit**, **Split**, and **Preview** to compare the source with the result.

You can safely delete this note — reopen it any time from **Settings → Show
tutorial note**.

## Headings

Use `#` through `######` for levels 1–6.

### Third level
#### Fourth level
##### Fifth level
###### Sixth level

## Text emphasis

*Italic* with `*` · **bold** with `**` · ***both*** · `inline code`.

Extras from the pipeline:

- ~~Strikethrough~~ with `~~ ~~`
- ++Inserted++ with `++ ++`
- H~2~O subscript with `~ ~`
- 1^st^ superscript with `^ ^`
- ==Highlighted== with `== ==` (the marker swipe)

## Lists

Unordered:

- First item
- Second item
  - Nested item
  - Another nested item

Ordered, and ordered with letters/roman numerals (ListExtras):

1. Step one
2. Step two

a. Alpha
b. Bravo

i. One
ii. Two

Task list (TaskLists):

- [x] Write a note
- [ ] Sync it
- [ ] Take a break

## Quotes and rules

> A blockquote. It can hold **formatting** and multiple lines.
>
> — and even a second paragraph.

A horizontal rule:

---

## Links and images

An [external link](https://github.com/xoofx/markdig) opens in your browser.
Bare URLs autolink too: https://github.com/xoofx/markdig

![Alt text describing the image](note-assets/example.png)

## Code

Inline `code` sits in a line. Fenced blocks keep their spacing:

```csharp
public static string Greet(string name)
{
    return $"Hello, {name}!";
}
```

```bash
dotnet build YASN.sln -c Release
```

## Tables

Pipe tables with column alignment:

| Feature      | Syntax        | Aligned |
| :----------- | :-----------: | ------: |
| Bold         | `**text**`    |     yes |
| Highlight    | `==text==`    |     yes |
| Reminder     | `[!…]…`        |     yes |

## Definition lists

Term
: The definition of the term.

Markdown
: A lightweight markup language for plain-text formatting.

## Footnotes

Here is a statement with a footnote.[^1]

[^1]: And here is the footnote's text, shown at the bottom of the note.

## Abbreviations

The HTML specification is maintained by the W3C.

*[HTML]: HyperText Markup Language
*[W3C]: World Wide Web Consortium

## Math

Inline math like $a^2 + b^2 = c^2$ and a block:

$$
\sum_{i=1}^{n} i = \frac{n(n+1)}{2}
$$

> Math is rendered by KaTeX, bundled with the app so it works offline.

## Containers and figures

Custom containers become callout blocks. The documented names are `note`,
`tip`, `warning`, and `danger`:

:::tip
Press the view button in the title bar to cycle Edit → Split → Preview.
:::

:::warning
Deleting a note cannot be undone.
:::

## Coloured text (YASN)

Wrap text in `{#colour|text}` to recolour it. Use a 6- or 8-digit hex value or a
named alias:

- {#FF0000|This is red} and {#0067c0|this is blue}.
- Aliases work too: {#g|green}, {#orange|orange}, {#purple|purple}.

The colour applies to **rendered Markdown**, so {#1f7a8c|**bold accent text**}
also works.

## Reminders (YASN)

A reminder badge has the form `[!display][control]{cron}{content}`:

- `display` — the label shown on the badge (Markdown allowed)
- `control` — flags: empty enables it (recurring), `1` fires once then auto-disables, `X` disables
- `cron` — a 5- or 6-field cron schedule
- `content` — the reminder message (shown as Markdown in the reminder window when it fires)

Enabled, every weekday at 9:00:

[!Standup][]{0 9 * * 1-5}{Daily standup}

Disabled (note the `X`), so it never fires:

[!Pay rent][X]{0 9 1 * *}{Rent is due}

Fire once, then turn itself off (becomes `[X1]` after it fires):

[!Call plumber][1]{0 10 * * *}{Ring the plumber back}

Every 15 minutes (a 5-field schedule):

[!Stretch][]{*/15 * * * *}{Stand up and stretch}

When a reminder fires you get a desktop notification and a small reminder window
that renders the message as Markdown. That is the whole syntax set. Edit this note
freely — it is yours now.


