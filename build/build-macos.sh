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
CODESIGN_IDENTITY=""
ENTITLEMENTS=""
NOTARIZE="false"
APPLE_ID=""
APPLE_ID_PASSWORD=""
APPLE_TEAM_ID=""
SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"
SRC_DIR="$REPO_ROOT/src"
PROJECT="$SRC_DIR/Connect.app.mac.core/Connect.app.mac.core.csproj"
INFO_PLIST="$SRC_DIR/Connect.app.mac.core/Info.plist"

# Parse arguments
while [[ $# -gt 0 ]]; do
  case "$1" in
    --configuration)    CONFIGURATION="$2"; shift 2 ;;
    --runtime)          RUNTIME="$2"; shift 2 ;;
    --output)           OUTPUT_DIR="$2"; shift 2 ;;
    --codesign-identity) CODESIGN_IDENTITY="$2"; shift 2 ;;
    --entitlements)     ENTITLEMENTS="$2"; shift 2 ;;
    --notarize)         NOTARIZE="true"; shift ;;
    --apple-id)         APPLE_ID="$2"; shift 2 ;;
    --apple-id-password) APPLE_ID_PASSWORD="$2"; shift 2 ;;
    --apple-team-id)    APPLE_TEAM_ID="$2"; shift 2 ;;
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
# Resolve version from Nerdbank.GitVersioning
dotnet tool restore
if dotnet nbgv get-version -v SimpleVersion --project "$SRC_DIR/Connect.app.mac.core" &> /dev/null; then
  APP_VERSION=$(dotnet nbgv get-version -v SimpleVersion --project "$SRC_DIR/Connect.app.mac.core")
else
  # Fallback: read base version from version.json
  APP_VERSION=$(grep '"version"' "$REPO_ROOT/version.json" | head -1 | sed 's/.*"version"[[:space:]]*:[[:space:]]*"\([^"]*\)".*/\1.0/')
fi
echo "Version:       $APP_VERSION"
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

# Copy Info.plist and stamp the version
sed "s/__VERSION__/$APP_VERSION/g" "$INFO_PLIST" > "$CONTENTS/Info.plist"

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

# Code sign the .app bundle
if [[ -n "$CODESIGN_IDENTITY" ]]; then
  echo "==> Code signing .app bundle..."

  # Resolve entitlements path
  if [[ -z "$ENTITLEMENTS" ]]; then
    ENTITLEMENTS="$SCRIPT_DIR/entitlements.plist"
  fi

  if [[ ! -f "$ENTITLEMENTS" ]]; then
    echo "  Error: Entitlements file not found at $ENTITLEMENTS"
    exit 1
  fi

  # Sign all nested binaries and dylibs first (deep sign)
  find "$APP_BUNDLE" -type f \( -name "*.dylib" -o -perm +111 \) -not -name "*.plist" -not -name "*.json" | while read -r bin; do
    codesign --force --options runtime \
      --entitlements "$ENTITLEMENTS" \
      --sign "$CODESIGN_IDENTITY" \
      --timestamp \
      "$bin" 2>/dev/null || true
  done

  # Sign the .app bundle itself
  codesign --force --deep --options runtime \
    --entitlements "$ENTITLEMENTS" \
    --sign "$CODESIGN_IDENTITY" \
    --timestamp \
    "$APP_BUNDLE"

  echo "  Verifying signature..."
  codesign --verify --deep --strict "$APP_BUNDLE"
  echo "  Signature OK"
else
  echo "==> Skipping code signing (no --codesign-identity provided)"
fi

echo "==> .app bundle created at: $APP_BUNDLE"

# Create DMG
DMG_NAME="BookLibConnect-${APP_VERSION}-${RUNTIME}"
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

# Sign the DMG
if [[ -n "$CODESIGN_IDENTITY" ]]; then
  echo "==> Code signing DMG..."
  codesign --force --sign "$CODESIGN_IDENTITY" --timestamp "$DMG_PATH"
  echo "  DMG signed"
fi

# Notarize the DMG
if [[ "$NOTARIZE" == "true" ]]; then
  if [[ -z "$APPLE_ID" || -z "$APPLE_ID_PASSWORD" || -z "$APPLE_TEAM_ID" ]]; then
    echo "  Error: --notarize requires --apple-id, --apple-id-password, and --apple-team-id"
    exit 1
  fi

  echo "==> Submitting DMG for notarization..."
  xcrun notarytool submit "$DMG_PATH" \
    --apple-id "$APPLE_ID" \
    --password "$APPLE_ID_PASSWORD" \
    --team-id "$APPLE_TEAM_ID" \
    --wait

  echo "==> Stapling notarization ticket..."
  xcrun stapler staple "$DMG_PATH"
  echo "  Notarization complete"
fi

# Clean up staging
rm -rf "$DMG_STAGING"
rm -rf "$OUTPUT_DIR/publish"

echo ""
echo "=== Build complete ==="
echo "Artifacts:"
echo "  .app bundle: $APP_BUNDLE"
echo "  .dmg:        $DMG_PATH"
