#!/usr/bin/env bash
#
# bundle-macos.sh — assemble a signed YASN.app bundle from a dotnet publish output.
#
# Builds the standard macOS .app layout described by the Avalonia macOS deployment guide
# (Contents/MacOS, Contents/Resources, Contents/Info.plist), converts a 1024x1024 PNG into
# a Retina .icns, and ad-hoc code signs the bundle (codesign --sign -) so Gatekeeper on
# Apple Silicon will run it after the user approves it once.
#
# Usage:
#   bundle-macos.sh <publish-dir> <icon-png> <plist-template> <version> <out-app>
#
#   publish-dir     Directory produced by `dotnet publish` (contains the YASN executable,
#                   native dylibs, and content such as style/ and tutorial.md).
#   icon-png        Square 1024x1024 PNG source for the app icon.
#   plist-template  Info.plist template containing the __VERSION__ token.
#   version         Version string (e.g. 1.2.3) substituted into the plist.
#   out-app         Output bundle path, e.g. dist/YASN.app.
#
# Must run on macOS: it uses sips, iconutil and codesign.

set -euo pipefail

if [ "$#" -ne 5 ]; then
    echo "usage: $0 <publish-dir> <icon-png> <plist-template> <version> <out-app>" >&2
    exit 2
fi

PUBLISH_DIR="$1"
ICON_PNG="$2"
PLIST_TEMPLATE="$3"
VERSION="$4"
OUT_APP="$5"

EXECUTABLE_NAME="YASN"

for required in "$PUBLISH_DIR" "$ICON_PNG" "$PLIST_TEMPLATE"; do
    if [ ! -e "$required" ]; then
        echo "error: required input not found: $required" >&2
        exit 1
    fi
done

if [ ! -f "$PUBLISH_DIR/$EXECUTABLE_NAME" ]; then
    echo "error: publish output has no '$EXECUTABLE_NAME' executable: $PUBLISH_DIR" >&2
    exit 1
fi

CONTENTS="$OUT_APP/Contents"
MACOS_DIR="$CONTENTS/MacOS"
RESOURCES_DIR="$CONTENTS/Resources"
FRAMEWORKS_DIR="$CONTENTS/Frameworks"

# Start from a clean bundle so reruns are deterministic.
rm -rf "$OUT_APP"
mkdir -p "$MACOS_DIR" "$RESOURCES_DIR"

# 1. Payload: copy the publish output (executable, native dylibs, content) into Contents/MacOS.
#    The dylibs and content are relocated out of Contents/MacOS in steps 1b/1c below so that
#    codesign sees only the apphost as nested code there.
cp -R "$PUBLISH_DIR"/. "$MACOS_DIR"/
chmod +x "$MACOS_DIR/$EXECUTABLE_NAME"

# 1b. Relocate native libraries into Contents/Frameworks. A single-file self-contained
#     publish drops the dylibs loose beside the apphost; Apple's bundle layout requires
#     nested Mach-O under Contents/Frameworks, and codesign --deep --strict rejects
#     dotted-name dylibs in Contents/MacOS. The NativeLibraryResolver hook loads them back
#     from ../Frameworks at runtime.
mkdir -p "$FRAMEWORKS_DIR"
find "$MACOS_DIR" -maxdepth 1 -type f -name '*.dylib' -exec mv {} "$FRAMEWORKS_DIR"/ \;

# 1c. Relocate read-only content (style/, Resources/) into Contents/Resources. codesign treats
#     every file under Contents/MacOS except the apphost as nested Mach-O code and rejects plain
#     text such as style/*.md as an unsigned subcomponent. Contents/Resources is the canonical,
#     non-code location; AppPaths.BundledContentRoot resolves there (../Resources) on macOS so the
#     app reads styles from Resources/style and the tutorial from Resources/Resources/tutorial.md.
for content in style Resources; do
    if [ -e "$MACOS_DIR/$content" ]; then
        mv "$MACOS_DIR/$content" "$RESOURCES_DIR"/
    fi
done

# 2. Icon: build a Retina .icns from the 1024x1024 PNG via an .iconset.
ICONSET="$(mktemp -d)/AppIcon.iconset"
mkdir -p "$ICONSET"
for size in 16 32 128 256 512; do
    double=$((size * 2))
    sips -z "$size" "$size"   "$ICON_PNG" --out "$ICONSET/icon_${size}x${size}.png"      >/dev/null
    sips -z "$double" "$double" "$ICON_PNG" --out "$ICONSET/icon_${size}x${size}@2x.png" >/dev/null
done
iconutil -c icns "$ICONSET" -o "$RESOURCES_DIR/AppIcon.icns"
rm -rf "$ICONSET"

# 3. Info.plist: substitute the version token from the template.
sed "s/__VERSION__/$VERSION/g" "$PLIST_TEMPLATE" > "$CONTENTS/Info.plist"

# 4. Ad-hoc code signing, inside-out (Apple's required order). Sign each nested dylib first,
#    then the apphost, then the bundle. "--sign -" is an ad-hoc signature (no Developer ID, no
#    notarization); "--force" overwrites the signatures that ship on the prebuilt native
#    libraries and the .NET apphost. --deep is deprecated for signing, so it is used only for
#    the verification step below, not here.
find "$FRAMEWORKS_DIR" -type f -name '*.dylib' -print0 |
    while IFS= read -r -d '' dylib; do
        /usr/bin/codesign --force --sign - "$dylib"
    done

/usr/bin/codesign --force --sign - "$MACOS_DIR/$EXECUTABLE_NAME"
/usr/bin/codesign --force --sign - "$OUT_APP"

# 5. Fail loud if the signature is not valid.
/usr/bin/codesign --verify --deep --strict --verbose=2 "$OUT_APP"

echo "built and ad-hoc signed: $OUT_APP (version $VERSION)"
