# YASN - Yet Another Sticky Notes

![YASN_intro.png](YASN_intro.png)

YASN is a cross-platform Avalonia sticky-notes app for Windows and macOS. It keeps notes in Markdown, renders live previews, persists window metadata, supports reminders, and retains WebDAV sync primitives.

## Repository Layout

- `src/YASN.App`: the Avalonia executable project, tray shell, note windows, platform service abstractions, localization, and packaged resources.
- `src/YASN.Core`: stable shared domain types kept as source-only directories for now.
- `src/YASN.Infrastructure`: persistence, logging, markdown, settings, and sync implementation code.
- `tests/YASN.Migration.Tests`: migration coverage for the Avalonia shell, persistence, reminders, notifications, and localization.

## Build

- `dotnet restore YASN.sln`
- `dotnet test YASN.sln --no-restore`
- `dotnet build YASN.sln -c Release --no-restore`
- `dotnet run --project src/YASN.App/YASN.App.csproj`
- `dotnet publish src/YASN.App/YASN.App.csproj -c Release -r win-x64 --self-contained false -o publish\win-x64`
- `dotnet publish src/YASN.App/YASN.App.csproj -c Release -r osx-x64 --self-contained false -o publish\osx-x64`
- `dotnet publish src/YASN.App/YASN.App.csproj -c Release -r osx-arm64 --self-contained false -o publish\osx-arm64`

## Verification

CI restores, tests, and builds the solution on Windows and macOS. Platform behavior that requires the host OS, such as tray/menu-bar integration, native notifications, auto-start, and window-level semantics, still needs manual verification on the matching desktop platform. Virtual desktop integration is not supported in the Avalonia shell; quick move and resize use Avalonia's cross-platform window and screen APIs.
