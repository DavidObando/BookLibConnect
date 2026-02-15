#!/bin/bash
set -euo pipefail

# Build the cross-platform Avalonia application for Linux.
# Usage: ./build-linux.sh [--configuration Release|Debug] [--runtime linux-x64|linux-arm64] [--output ./artifacts]

APP_NAME="BookLibConnect"

CONFIGURATION="Release"
RUNTIME=""
OUTPUT_DIR="./artifacts"
SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"
SRC_DIR="$REPO_ROOT/src"
PROJECT="$SRC_DIR/Connect.app.avalonia.core/Connect.app.avalonia.core.csproj"

# Parse arguments
while [[ $# -gt 0 ]]; do
  case "$1" in
    --configuration)    CONFIGURATION="$2"; shift 2 ;;
    --runtime)          RUNTIME="$2"; shift 2 ;;
    --output)           OUTPUT_DIR="$2"; shift 2 ;;
    *) echo "Unknown option: $1"; exit 1 ;;
  esac
done

# Auto-detect runtime if not specified
if [[ -z "$RUNTIME" ]]; then
  ARCH="$(uname -m)"
  if [[ "$ARCH" == "aarch64" || "$ARCH" == "arm64" ]]; then
    RUNTIME="linux-arm64"
  else
    RUNTIME="linux-x64"
  fi
fi

echo "=== BookLibConnect Linux Build ==="
echo "Configuration: $CONFIGURATION"
echo "Runtime:       $RUNTIME"
echo "Output:        $OUTPUT_DIR"

# Resolve version from Nerdbank.GitVersioning
dotnet tool restore
APP_VERSION=""
if dotnet nbgv get-version -v SimpleVersion --project "$SRC_DIR/Connect.app.avalonia.core" &> /dev/null; then
  APP_VERSION=$(dotnet nbgv get-version -v SimpleVersion --project "$SRC_DIR/Connect.app.avalonia.core")
else
  # Fallback: read base version from version.json
  APP_VERSION=$(grep '"version"' "$REPO_ROOT/version.json" | head -1 | sed 's/.*"version"[[:space:]]*:[[:space:]]*"\([^"]*\)".*/\1.0/')
fi
echo "Version:       $APP_VERSION"
echo ""

# Clean output
rm -rf "$OUTPUT_DIR"
mkdir -p "$OUTPUT_DIR"

PUBLISH_DIR="$OUTPUT_DIR/publish"

# Publish self-contained with IL trimming
echo "==> Publishing..."
dotnet publish "$PROJECT" \
  --configuration "$CONFIGURATION" \
  --runtime "$RUNTIME" \
  --self-contained true \
  -p:PublishTrimmed=true \
  --output "$PUBLISH_DIR"

echo "  Published to: $PUBLISH_DIR"

# Make the executable actually executable
chmod +x "$PUBLISH_DIR/$APP_NAME"

# Create a tarball
TARBALL_NAME="${APP_NAME}-${APP_VERSION}-${RUNTIME}.tar.gz"
TARBALL_PATH="$OUTPUT_DIR/$TARBALL_NAME"

echo "==> Creating tarball..."
tar -czf "$TARBALL_PATH" -C "$PUBLISH_DIR" .

echo "  Tarball: $TARBALL_PATH"

# Clean up publish directory
rm -rf "$PUBLISH_DIR"

echo ""
echo "=== Build complete ==="
echo "Artifacts:"
echo "  Tarball: $TARBALL_PATH"
