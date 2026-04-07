# YASN - Yet Another Sticky Notes

YASN is a Windows sticky-notes app with Markdown preview, attachments, and WebDAV sync support.

## Repository Layout

- `src/YASN.App`: the single WPF executable project, windows, application shell, packaged resources, and static style assets.
- `src/YASN.Core`: stable shared domain types kept as source-only directories for now.
- `src/YASN.Infrastructure`: persistence, logging, markdown, and sync implementation code.

## Build

- `dotnet restore YASN.sln`
- `dotnet build YASN.sln -c Release --no-restore`
- `dotnet run --project src/YASN.App/YASN.App.csproj`
- `dotnet publish src/YASN.App/YASN.App.csproj -c Release -r win-x64 --self-contained false -o publish`
