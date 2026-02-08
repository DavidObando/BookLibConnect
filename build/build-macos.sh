#!/bin/bash
set -euo pipefail

# Build a macOS .app bundle for BookLibConnect
# Usage: ./build-macos.sh [--configuration Release|Debug] [--runtime osx-arm64|osx-x64] [--output ./artifacts]

APP_NAME="Book Lib Connect"
BUNDLE_ID="com.audiamus.booklibconnect"
EXECUTABLE_NAME="BookLibConnect.Mac"

CONFIGURATION="Release"
RUNTIME=""
OUTPUT_DIR="./artifacts"
SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"
SRC_DIR="$REPO_ROOT/src"
PROJECT="$SRC_DIR/Connect.app.mac.core/Connect.app.mac.core.csproj"
INFO_PLIST="$SRC_DIR/Connect.app.mac.core/Info.plist"

# Parse arguments
while [[ $# -gt 0 ]]; do
  case "$1" in
    --configuration) CONFIGURATION="$2"; shift 2 ;;
    --runtime)       RUNTIME="$2"; shift 2 ;;
    --output)        OUTPUT_DIR="$2"; shift 2 ;;
    *) echo "Unknown option: $1"; exit 1 ;;
  esac
done

# Auto-detect runtime if not specified
if [[ -z "$RUNTIME" ]]; then
  ARCH="$(uname -m)"
  if [[ "$ARCH" == "arm64" ]]; then
    RUNTIME="osx-arm64"
  else
    RUNTIME="osx-x64"
  fi
fi

echo "=== BookLibConnect macOS Build ==="
echo "Configuration: $CONFIGURATION"
echo "Runtime:       $RUNTIME"
echo "Output:        $OUTPUT_DIR"
echo ""

# Clean output
rm -rf "$OUTPUT_DIR"
mkdir -p "$OUTPUT_DIR"

# Publish self-contained
echo "==> Publishing..."
dotnet publish "$PROJECT" \
  --configuration "$CONFIGURATION" \
  --runtime "$RUNTIME" \
  --self-contained true \
  -p:PublishSingleFile=false \
  -p:PublishTrimmed=false \
  --output "$OUTPUT_DIR/publish"

# Build the .app bundle
APP_BUNDLE="$OUTPUT_DIR/${APP_NAME}.app"
CONTENTS="$APP_BUNDLE/Contents"
MACOS_DIR="$CONTENTS/MacOS"
RESOURCES_DIR="$CONTENTS/Resources"

echo "==> Creating .app bundle..."
mkdir -p "$MACOS_DIR"
mkdir -p "$RESOURCES_DIR"

# Copy published output into MacOS/
cp -R "$OUTPUT_DIR/publish/"* "$MACOS_DIR/"

# Copy Info.plist
cp "$INFO_PLIST" "$CONTENTS/Info.plist"

# Copy icon if it exists; generate from PNG if needed
ICON_FILE="$SRC_DIR/Connect.app.mac.core/audio.icns"
ICON_PNG="$SRC_DIR/Connect.app.gui.core/Resources/audio.png"
if [[ ! -f "$ICON_FILE" && -f "$ICON_PNG" ]]; then
  echo "  Generating audio.icns from audio.png..."
  ICONSET="$(mktemp -d)/audio.iconset"
  mkdir -p "$ICONSET"
  sips -z 16 16     "$ICON_PNG" --out "$ICONSET/icon_16x16.png"      > /dev/null
  sips -z 32 32     "$ICON_PNG" --out "$ICONSET/icon_16x16@2x.png"   > /dev/null
  sips -z 32 32     "$ICON_PNG" --out "$ICONSET/icon_32x32.png"      > /dev/null
  sips -z 64 64     "$ICON_PNG" --out "$ICONSET/icon_32x32@2x.png"   > /dev/null
  sips -z 128 128   "$ICON_PNG" --out "$ICONSET/icon_128x128.png"    > /dev/null
  sips -z 256 256   "$ICON_PNG" --out "$ICONSET/icon_128x128@2x.png" > /dev/null
  sips -z 256 256   "$ICON_PNG" --out "$ICONSET/icon_256x256.png"    > /dev/null
  sips -z 512 512   "$ICON_PNG" --out "$ICONSET/icon_256x256@2x.png" > /dev/null
  sips -z 512 512   "$ICON_PNG" --out "$ICONSET/icon_512x512.png"    > /dev/null
  sips -z 1024 1024 "$ICON_PNG" --out "$ICONSET/icon_512x512@2x.png" > /dev/null
  iconutil -c icns "$ICONSET" -o "$ICON_FILE"
  rm -rf "$(dirname "$ICONSET")"
fi
if [[ -f "$ICON_FILE" ]]; then
  cp "$ICON_FILE" "$RESOURCES_DIR/audio.icns"
else
  echo "  Warning: Icon file not found at $ICON_FILE"
fi

# Make the executable actually executable
chmod +x "$MACOS_DIR/$EXECUTABLE_NAME"

echo "==> .app bundle created at: $APP_BUNDLE"

# Create DMG
DMG_NAME="BookLibConnect-${RUNTIME}"
DMG_PATH="$OUTPUT_DIR/${DMG_NAME}.dmg"
DMG_STAGING="$OUTPUT_DIR/dmg-staging"

echo "==> Creating DMG..."
mkdir -p "$DMG_STAGING"
cp -R "$APP_BUNDLE" "$DMG_STAGING/"

# Add a symlink to /Applications for drag-install
ln -s /Applications "$DMG_STAGING/Applications"

# Create the DMG
hdiutil create -volname "$APP_NAME" \
  -srcfolder "$DMG_STAGING" \
  -ov -format UDZO \
  "$DMG_PATH"

echo "==> DMG created at: $DMG_PATH"

# Clean up staging
rm -rf "$DMG_STAGING"
rm -rf "$OUTPUT_DIR/publish"

echo ""
echo "=== Build complete ==="
echo "Artifacts:"
echo "  .app bundle: $APP_BUNDLE"
echo "  .dmg:        $DMG_PATH"
