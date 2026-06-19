# macOS packaging assets

This directory holds the inputs used by `scripts/bundle-macos.sh` to produce the
`YASN.app` bundle during the `Release` workflow.

## `AppIcon.png` (required — must be committed)

A square **1024x1024** PNG used as the application icon. The bundler converts it into a
Retina `AppIcon.icns` (via `sips` + `iconutil`) on the macOS runner.

The build **fails loudly** if this file is missing — there is no fallback icon by design.
Replace this note by committing the actual `AppIcon.png` here.

## `Info.plist.template`

The bundle `Info.plist`, with `__VERSION__` substituted from the release tag at build time.
YASN is a tray-only agent app, so `LSUIElement` is set (no Dock icon).
