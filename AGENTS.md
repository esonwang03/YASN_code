# Repository Guidelines

# Rules of Implementations

YOU SHOULD NOT CHANGE THIS SECTION DURING UPDATE AGENTS.md

1. Avoid degradation handling, fallback, hacks, heuristics, local stabilization, or post-processing bandages that are not faithful general algorithms.
2. Always write module docs and function docs.
3. Keep the file short, if the file is too long, separates code to other file under the same dir.

## Project Structure & Module Organization
`YASN.csproj` is a single Windows desktop app targeting `.NET 10` with WPF, WinForms interop, WebView2, and Markdig. Most app entry points live at the repository root: `App.xaml`, `MainWindow.xaml`, `FloatingWindow.xaml`, and their `.cs` code-behind files. Keep feature-specific logic in the existing folders: `Settings/` for persisted options and settings UI helpers, `Sync/` for sync abstractions and WebDAV support, `Markdown/` for preview extensions, `Logging/` for diagnostics, `Resources/` for icons and `.resx` assets, and `style/` for packaged CSS. Ignore build output under `bin/`, `obj/`, and local NuGet cache content under `.nuget/`.

## Build, Test, and Development Commands
Use the same commands as CI on Windows:

- `dotnet restore YASN.sln` restores packages.
- `dotnet build YASN.sln -c Release --no-restore` builds the app in the CI configuration.
- `dotnet run --project YASN.csproj` launches the app locally for interactive testing.
- `dotnet publish YASN.csproj -c Release -r win-x64 --self-contained false -o publish` creates the release artifact used by `.github/workflows/release.yml`.
- `dotnet format YASN.sln` applies `.editorconfig` formatting and analyzer fixes before review.

## Coding Style & Naming Conventions
Follow `.editorconfig`: UTF-8, CRLF, 4-space indentation, trailing whitespace trimmed, and braces on new lines. Prefer explicit C# types over `var` unless the type is obvious and already established nearby. Use PascalCase for public types, methods, XAML controls, and properties; use camelCase for locals and private fields. Match the current partial-class pairing pattern: `WindowName.xaml` with `WindowName.xaml.cs`.

## Testing Guidelines
There is no dedicated test project yet, and CI currently validates restore and release builds only. For changes, add focused manual checks: app startup, note editing, markdown preview rendering, settings persistence, and WebDAV sync when touching `Sync/`. If you introduce automated tests later, place them in a separate `*.Tests` project and keep test names descriptive, for example `SyncManagerTests`.

## Commit & Pull Request Guidelines
Recent history favors short, imperative subjects such as `fix on settings loading` or `add linter`, sometimes with issue references like `(#7)`. Keep commit titles concise, scoped to one change, and include an issue number when applicable. PRs should summarize user-visible behavior, note config or migration impact, and include screenshots or short recordings for XAML or styling changes.
