#!/bin/bash
# Builds Chomp.app. Usage: ./build.sh            → build/Chomp.app
#                         ./build.sh --install  → also copies it to /Applications
# Environment:
#   UNIVERSAL=1   one app for both Apple Silicon and Intel (what releases use)
#   VERSION=0.3.0 stamps that version into the app (releases set it from the tag)
#   CI=true       builds without opening the app (set by GitHub Actions)
set -euo pipefail
cd "$(dirname "$0")"

if ! command -v swift >/dev/null 2>&1; then
  echo "→ Apple's command line tools are missing. Run: xcode-select --install"
  exit 1
fi

ARCH_FLAGS=()
if [[ "${UNIVERSAL:-}" == "1" ]]; then
  ARCH_FLAGS=(--arch arm64 --arch x86_64)
fi

echo "→ Building…"
swift build -c release ${ARCH_FLAGS[@]+"${ARCH_FLAGS[@]}"}
BIN="$(swift build -c release ${ARCH_FLAGS[@]+"${ARCH_FLAGS[@]}"} --show-bin-path)/Chomp"

APP="build/Chomp.app"
rm -rf "$APP"
mkdir -p "$APP/Contents/MacOS" "$APP/Contents/Resources"
cp "$BIN" "$APP/Contents/MacOS/Chomp"
cp Resources/Info.plist "$APP/Contents/Info.plist"
if [[ -n "${VERSION:-}" ]]; then
  plutil -replace CFBundleShortVersionString -string "$VERSION" "$APP/Contents/Info.plist"
  plutil -replace CFBundleVersion -string "${BUILD_NUMBER:-$VERSION}" "$APP/Contents/Info.plist"
fi
cp -R Resources/Fonts "$APP/Contents/Resources/Fonts"
iconutil -c icns Resources/AppIcon.iconset -o "$APP/Contents/Resources/AppIcon.icns"
codesign --force --deep --sign - "$APP" >/dev/null 2>&1 || true

if [[ "${1:-}" == "--install" ]]; then
  pkill -x Chomp 2>/dev/null || true
  rm -rf /Applications/Chomp.app
  cp -R "$APP" /Applications/
  APP="/Applications/Chomp.app"
  echo "→ Installed to /Applications"
fi

if ! command -v ffmpeg >/dev/null 2>&1 && [ ! -x /opt/homebrew/bin/ffmpeg ] && [ ! -x /usr/local/bin/ffmpeg ]; then
  echo "! Video and audio formats need ffmpeg: brew install ffmpeg"
fi

echo "→ Done: $APP ($(lipo -archs "$APP/Contents/MacOS/Chomp"))"
if [[ "${CI:-}" != "true" ]]; then
  echo "→ Look for the little chest in your menu bar."
  open "$APP"
fi
