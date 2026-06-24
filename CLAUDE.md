# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

YASN ("Yet Another Sticky Notes") is a cross-platform (Windows + macOS) **Avalonia** sticky-notes app with Markdown preview, attachments, reminders, and WebDAV sync. Targets `.NET 10` (`net10.0`, multi-RID: `win-x64;osx-x64;osx-arm64`); the build uses a prerelease SDK pinned via `global.json` (`10.0.0`, `rollForward: latestMajor`, `allowPrerelease: true`).

## Commands

```bash
dotnet restore YASN.sln                                  # restore (CI step)
dotnet format YASN.sln --verify-no-restore               # lint — CI runs --verify-no-changes; must be clean
dotnet build YASN.sln -c Release --no-restore            # build (CI step — must pass)
dotnet test YASN.sln -c Release --no-build               # run xUnit tests (CI step)
dotnet run --project src/YASN.App/YASN.App.csproj        # run locally
dotnet publish src/YASN.App/YASN.App.csproj -c Release -r win-x64 --self-contained false -o publish
dotnet publish src/YASN.App/YASN.App.csproj -c Release -p:EnableAot=true -r <rid> -o publish/aot   # opt-in NativeAOT
```

Note the asymmetry: restore/build/test/format operate on `YASN.sln`, but run/publish target `src/YASN.App/YASN.App.csproj` directly.

There **is** a test project — `tests/YASN.Migration.Tests` (xUnit), in `YASN.sln`. CI (`.github/workflows/ci.yml`) runs restore → format-check → Release build → `dotnet test`, plus a NativeAOT publish smoke test on Windows and macOS. Tests focus on migration/persistence/reminders/localization; there are no UI-level tests, so still verify UI changes manually: app startup, note create/edit, markdown preview, settings persistence, and WebDAV sync when touching `Sync/`.

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

**Single-app-project layout.** Despite the three `src/` directories, there is exactly **one** application `.csproj` (`YASN.App`). `YASN.Core` and `YASN.Infrastructure` have no project files — `YASN.App.csproj` pulls their `.cs` files in via `<Compile Include="..\YASN.Core\**\*.cs">` globs. Everything compiles into one assembly named `YASN` with `RootNamespace` `YASN`. There are no inter-project references to manage; adding a `.cs` file under those folders automatically includes it. `GlobalUsings.cs` globally imports the common namespaces. (The solution also contains `tests/YASN.Migration.Tests` and `tools/YASN.Migrator`, which are separate projects.)

- `src/YASN.App` — Avalonia shell, windows (`.axaml`/`.axaml.cs`), notes model, settings UI/schema, tray integration, CLI/IPC, packaged `style/` CSS and `Resources/`.
- `src/YASN.Core` — small stable domain enums/types (`WindowLevel`, `EditorDisplayMode`, etc.).
- `src/YASN.Infrastructure` — persistence, logging, markdown pipeline, settings store, sync.

**Cross-platform via PlatformServices.** Platform-specific behavior (single-instance guard, auto-start, global hotkeys, window levels, notifications, native-library resolution) is abstracted behind factories under `src/YASN.App/PlatformServices/`, created by `PlatformServiceFactory.Create()`. Windows and macOS each get their own implementation (e.g. `MutexSingleInstanceGuard` on Windows vs. `FileLockSingleInstanceGuard` elsewhere). Don't hardcode platform assumptions — add to the relevant factory.

**No shell main window.** `YasnApplication.OnFrameworkInitializationCompleted` (`src/YASN.App/App.axaml.cs`) runs from the system tray (Avalonia `TrayIcon` + `NativeMenu`, built in `src/YASN.App/Application/TrayShell.cs`) and enforces a single instance through the platform guard. Each note is its own `FloatingNoteWindow`; `NoteWindowManager` drives note-window creation/restoration. (`Views/MainWindow` is a manager window, not the app shell.)

**The exe doubles as a CLI.** `Program.Main` (`src/YASN.App/Program.cs`) routes `args.Length > 0` to `CliEntry.Run(args)` instead of starting Avalonia. Read-only/folder verbs are served directly; state/UI verbs route over IPC to the running tray instance (named pipe on Windows, Unix domain socket on macOS), auto-launching it if needed. Key files: `Cli/CliEntry.cs`, `Cli/CliIpcServer.cs`, `Cli/CliIpcClient.cs`, `Cli/CliCommandRouter.cs`.

**Notes persistence is split: content vs. metadata.** `NoteRepository` (`src/YASN.App/AvaloniaNotes/`) loads/persists notes; `NoteWindowManager` manages their open windows. Each note's Markdown **content** is a separate `<id>.md` file under `notes/`; note **metadata** (position, size, level, colors, open state) lives in a single `notes.index.json`. The legacy single-file WPF store is migrated forward by `WpfNoteStorageMigrator` at startup. Note ids are GUIDs (an older integer-id scheme is migrated via the same migrator).

**`AppPaths` is the single source of truth for all file locations** (`src/YASN.Infrastructure/AppPaths.cs`). The data directory is user-configurable via the `app.dataDirectory` setting; everything (notes, assets, style, html-cache, sync database, IPC pipe/socket) is derived from it. Never hardcode paths — add a member to `AppPaths`.

**Two-tier settings.** `SettingsStore` (`src/YASN.Infrastructure/Settings/`) keeps two dictionaries: synced settings (`settings.sync.json`, in the data dir, replicated by sync) and local settings (`settings.local.json`, next to the exe, machine-specific). `SettingField.ShouldSync` decides which file a field belongs to. The settings UI is **schema-driven**: `SettingsSchemaBuilder` (`src/YASN.App/SettingsUi/`) declares fields in per-module builder methods (`BuildGeneralModule`, `BuildSyncModule`, …); `SettingsWindow` renders them generically from `SettingFieldType` and persists on Save.

**Sync is backend-abstracted.** `SyncComposition` (`src/YASN.App/Application/SyncComposition.cs`) wires the sync stack and exposes a `ThreeWaySyncEngine Engine`; `SyncComposition.ApplyConfiguration(settings)` (re)configures it at startup and after a settings save. Sync metadata is tracked in a SQLite store (`SyncStateStore`, via `Microsoft.Data.Sqlite` — content stays in `.md` files). WebDAV is the only `ISyncClient` implementation (`src/YASN.Infrastructure/Sync/WebDav/`).

**Markdown → HTML preview via WebView2.** `MarkdownPipelineConfig` (`src/YASN.Infrastructure/Markdown/`) builds the Markdig pipeline; custom extensions live in `Markdown/Extensions/` (e.g. `SourceLineExtension` emits `data-source-line` anchors for caret↔preview scroll sync, plus hex-color and reminder extensions). `MarkdownPreviewDocument.Render()` (`src/YASN.App/SingleNote/`) assembles the HTML document and its bridge scripts; `PreviewStyleManager` materializes the packaged `style/` CSS (and vendored KaTeX) into the data dir. The preview renders in an `Avalonia.Controls.WebView` `NativeWebView` control hosted in `FloatingNoteWindow`.

**Windows are partial classes split by concern.** `FloatingNoteWindow` is `FloatingNoteWindow.axaml.cs` plus `.Editor.cs` and `.Collapse.cs` — follow this pattern (rule 3) rather than growing one file. `WindowName.axaml` always pairs with `WindowName.axaml.cs`.

## Conventions

- Follow `.editorconfig`: UTF-8, **CRLF**, 4-space indent, braces on their own line, trailing whitespace trimmed.
- Prefer **explicit types over `var`** unless the type is already obvious nearby.
- PascalCase for public types/methods/properties/XAML controls; camelCase for locals and private fields.
- Log through `AppLogger` (`Info`/`Warn`/`Debug`); catch specific exception types, not bare `Exception` (see the per-exception-type `catch` blocks in `App.axaml.cs`).
- Commits: short imperative subjects, optional issue ref like `(#7)`. PRs: summarize user-visible behavior, note config/migration impact, attach screenshots for XAML/style changes.

## Gotchas

- `AGENTS.md` predates the Avalonia/`src/` refactor and still describes a WPF app with a single root `YASN.csproj` and root-level entry points — its **layout, paths, and `dotnet run`/`publish` commands are stale**; its hard rules and style guidance remain authoritative. Trust this file and the README for layout.
- The app is **GUI-subsystem** (`OutputType=WinExe`) yet doubles as a CLI: in tray mode it has no console, so `AppLogger` only echoes to a console under `#if DEBUG` or when diagnose mode raises one (see `Diagnostics/DiagnoseMode.cs`). The CLI path attaches to the parent terminal (`Cli/ConsoleInterop.cs`).
