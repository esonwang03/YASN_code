# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

YASN ("Yet Another Sticky Notes") is a Windows-only WPF sticky-notes app with Markdown preview, attachments, and WebDAV sync. Targets `.NET 10` (`net10.0-windows10.0.19041.0`); the build uses a prerelease SDK pinned via `global.json`.

## Commands

```bash
dotnet restore YASN.sln                                  # restore (CI step)
dotnet build YASN.sln -c Release --no-restore            # build (CI step — must pass)
dotnet run --project src/YASN.App/YASN.App.csproj        # run locally
dotnet format YASN.sln                                   # apply .editorconfig + analyzer fixes before review
dotnet publish src/YASN.App/YASN.App.csproj -c Release -r win-x64 --self-contained false -o publish
```

Note the asymmetry: restore/build operate on `YASN.sln`, but run/publish target `src/YASN.App/YASN.App.csproj` directly.

There is **no test project**. CI (`.github/workflows/ci.yml`) only validates restore + Release build. Verify changes with manual checks: app startup, note create/edit, markdown preview, settings persistence, and WebDAV sync when touching `Sync/`.

## Hard rules (from `AGENTS.md`, do not relax)

1. No fallback / hacks / heuristics / local stabilization / post-processing bandages. Write faithful general algorithms only.
2. Always write module docs and function docs (XML `<summary>` is the established style).
3. Keep files short. When a file grows too long, split into another file in the same directory — this is why windows are partial classes (see below).

## 12-rules for Implement

These rules apply to every task in this project unless explicitly overridden.
Bias: caution over speed on non-trivial work. Use judgment on trivial tasks.

### Rule 1 — Think Before Coding
State assumptions explicitly. If uncertain, ask rather than guess.
Present multiple interpretations when ambiguity exists.
Push back when a simpler approach exists.
Stop when confused. Name what's unclear.

### Rule 2 — Simplicity First
Minimum code that solves the problem. Nothing speculative.
No features beyond what was asked. No abstractions for single-use code.
Test: would a senior engineer say this is overcomplicated? If yes, simplify.

### Rule 3 — Surgical Changes
Touch only what you must. Clean up only your own mess.
Don't "improve" adjacent code, comments, or formatting.
Don't refactor what isn't broken. Match existing style.

### Rule 4 — Goal-Driven Execution
Define success criteria. Loop until verified.
Don't follow steps. Define success and iterate.
Strong success criteria let you loop independently.

### Rule 5 — Use the model only for judgment calls
Use me for: classification, drafting, summarization, extraction.
Do NOT use me for: routing, retries, deterministic transforms.
If code can answer, code answers.

### Rule 6 — Token budgets are not advisory
Per-task: 4,000 tokens. Per-session: 30,000 tokens.
If approaching budget, summarize and start fresh.
Surface the breach. Do not silently overrun.

### Rule 7 — Surface conflicts, don't average them
If two patterns contradict, pick one (more recent / more tested).
Explain why. Flag the other for cleanup.
Don't blend conflicting patterns.

### Rule 8 — Read before you write
Before adding code, read exports, immediate callers, shared utilities.
"Looks orthogonal" is dangerous. If unsure why code is structured a way, ask.

### Rule 9 — Tests verify intent, not just behavior
Tests must encode WHY behavior matters, not just WHAT it does.
A test that can't fail when business logic changes is wrong.

### Rule 10 — Checkpoint after every significant step
Summarize what was done, what's verified, what's left.
Don't continue from a state you can't describe back.
If you lose track, stop and restate.

### Rule 11 — Match the codebase's conventions, even if you disagree
Conformance > taste inside the codebase.
If you genuinely think a convention is harmful, surface it. Don't fork silently.

### Rule 12 — Fail loud
"Completed" is wrong if anything was skipped silently.
"Tests pass" is wrong if any were skipped.
Default to surfacing uncertainty, not hiding it.


## Architecture

**Single-assembly, three-folder layout.** Despite the three `src/` directories, there is exactly **one** `.csproj` (`YASN.App`). `YASN.Core` and `YASN.Infrastructure` have no project files — `YASN.App.csproj` pulls their `.cs` files in via `<Compile Include="..\YASN.Core\**\*.cs">` globs. Everything compiles into one assembly named `YASN` with `RootNamespace` `YASN`. There are no inter-project references to manage; adding a `.cs` file under those folders automatically includes it. `GlobalUsings.cs` globally imports all the namespaces.

- `src/YASN.App` — WPF shell, windows, notes model, settings UI/schema, tray integration, packaged `style/` CSS and `Resources/`.
- `src/YASN.Core` — small stable domain enums/types (`WindowLevel`, `EditorDisplayMode`, etc.).
- `src/YASN.Infrastructure` — persistence, logging, markdown pipeline, settings store, sync.

**No main window.** `App.OnStartup` (`src/YASN.App/App.xaml.cs`) hides `MainWindow`, runs from the system tray (`NotifyIcon`), and enforces a single instance via a named `Mutex`. Each note is its own `FloatingWindow`. Tray menu and `NoteManager.RestoreOpenNotes()` drive note creation/restoration.

**Notes persistence is split: content vs. metadata.** `NoteManager` (singleton, `NoteManager.Instance`) holds an `ObservableCollection<NoteData>`. Each note's Markdown **content** is a separate `<id>.md` file under `notes/`; note **metadata** (position, size, level, colors, open state) lives in a single `notes.index.json`. `RepairIndexFromLocalMarkdownFiles()` rebuilds the index from orphaned `.md` files. A schema version (currently 2) migrates the legacy single-file `notes.json`.

**`AppPaths` is the single source of truth for all file locations** (`src/YASN.Infrastructure/AppPaths.cs`). The data directory is user-configurable via the `app.dataDirectory` setting; everything (notes, assets, style, html-cache, sync manifest) is derived from it. Never hardcode paths — add a member to `AppPaths`.

**Two-tier settings.** `SettingsStore` keeps two dictionaries: synced settings (`settings.sync.json`, in the data dir, replicated by sync) and local settings (`settings.local.json`, next to the exe, machine-specific). `SettingField.ShouldSync` decides which file a field belongs to. The settings UI is **schema-driven**: `SettingsSchema` + the `*SettingsFieldFactory` classes declare fields; `SettingsWindow` renders them generically from `SettingFieldType`.

**Sync is backend-abstracted.** `SyncManager` (exposed as static `App.SyncManager`) runs on a timer, serializes runs with a `SemaphoreSlim`, and delegates all storage to an `ISyncClient`. WebDAV is the only current implementation (`Sync/WebDav/`); `WebDavSyncBootstrapper` restores saved config at startup. Change detection uses a content-hash manifest (`sync.manifest.json` / `SignatureStore`) compared against remote signatures — not timestamps.

**Markdown → HTML preview via WebView2.** `MarkdownPipelineConfig` builds the Markdig pipeline (custom extensions live in `Markdown/Extensions/`, e.g. hex-color rendering). `PreviewStyleManager` materializes the packaged `style/` CSS into the data dir; previews render in a `WebView2` host inside `FloatingWindow`.

**Windows are partial classes split by concern.** `FloatingWindow` is `FloatingWindow.xaml.cs` plus `.Appearance.cs`, `.ContentAssets.cs`, `.Preview.cs` — follow this pattern (rule 3) rather than growing one file. `WindowName.xaml` always pairs with `WindowName.xaml.cs`.

## Conventions

- Follow `.editorconfig`: UTF-8, **CRLF**, 4-space indent, braces on their own line, trailing whitespace trimmed.
- Prefer **explicit types over `var`** unless the type is already obvious nearby.
- PascalCase for public types/methods/properties/XAML controls; camelCase for locals and private fields.
- Log through `AppLogger` (`Info`/`Warn`/`Debug`); catch specific exception types, not bare `Exception` (see the per-exception-type `catch` blocks in `App.xaml.cs`).
- Commits: short imperative subjects, optional issue ref like `(#7)`. PRs: summarize user-visible behavior, note config/migration impact, attach screenshots for XAML/style changes.

## Gotchas

- `AGENTS.md` predates the `src/` refactor (commit `262830f`) and still describes a single root `YASN.csproj` with root-level entry points — its **paths and `dotnet run`/`publish` commands are stale**; its rules and style guidance are still authoritative. Trust this file and the README for layout.
- `floatingwindow_head.tmp.cs` at the repo root is a stray scratch file, not part of the build (it's outside the `src/` compile globs). Don't treat it as a source of truth.
