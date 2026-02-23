#!/bin/bash
set -euo pipefail

# Update the Homebrew formula with SHA256 hashes from a GitHub release.
# Usage: ./build/update-homebrew-formula.sh [TAG]
# If TAG is omitted, uses the latest release.

REPO="DavidObando/Oahu"
SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
FORMULA="$SCRIPT_DIR/../Formula/oahu.rb"

if [[ ! -f "$FORMULA" ]]; then
  echo "Error: Formula not found at $FORMULA"
  exit 1
fi

# Determine the tag
if [[ $# -ge 1 ]]; then
  TAG="$1"
else
  TAG=$(gh release view --repo "$REPO" --json tagName -q .tagName)
fi
VERSION="${TAG#v}"

echo "Updating formula for $REPO $TAG (version $VERSION)..."

TMPDIR="$(mktemp -d)"
trap 'rm -rf "$TMPDIR"' EXIT

# Define the expected tarballs
PLATFORMS=("osx-arm64" "osx-x64" "linux-arm64" "linux-x64")

declare -A SHAS

for PLATFORM in "${PLATFORMS[@]}"; do
  TARBALL="Oahu-${VERSION}-${PLATFORM}.tar.gz"
  echo "  Downloading $TARBALL..."
  gh release download "$TAG" --repo "$REPO" --pattern "$TARBALL" --dir "$TMPDIR" 2>/dev/null || {
    echo "  Warning: $TARBALL not found in release $TAG, skipping"
    continue
  }
  SHA=$(shasum -a 256 "$TMPDIR/$TARBALL" | awk '{print $1}')
  SHAS[$PLATFORM]="$SHA"
  echo "    SHA256: $SHA"
done

# Patch the formula
echo ""
echo "Patching $FORMULA..."

# Update version
sed -i.bak -E "s/^  version \".*\"/  version \"$VERSION\"/" "$FORMULA"

# Update SHA256 for each platform using contextual replacement
if [[ -n "${SHAS[osx-arm64]:-}" ]]; then
  # macOS ARM64: the sha256 line right after the osx-arm64 URL
  sed -i.bak "/osx-arm64\.tar\.gz/{n;s/sha256 \".*\"/sha256 \"${SHAS[osx-arm64]}\"/;}" "$FORMULA"
fi
if [[ -n "${SHAS[osx-x64]:-}" ]]; then
  sed -i.bak "/osx-x64\.tar\.gz/{n;s/sha256 \".*\"/sha256 \"${SHAS[osx-x64]}\"/;}" "$FORMULA"
fi
if [[ -n "${SHAS[linux-arm64]:-}" ]]; then
  sed -i.bak "/linux-arm64\.tar\.gz/{n;s/sha256 \".*\"/sha256 \"${SHAS[linux-arm64]}\"/;}" "$FORMULA"
fi
if [[ -n "${SHAS[linux-x64]:-}" ]]; then
  sed -i.bak "/linux-x64\.tar\.gz/{n;s/sha256 \".*\"/sha256 \"${SHAS[linux-x64]}\"/;}" "$FORMULA"
fi

# Clean up sed backup files
rm -f "$FORMULA.bak"

echo ""
echo "Done! Updated formula:"
cat "$FORMULA"
