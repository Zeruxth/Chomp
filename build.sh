#!/bin/bash
# Builds Chomp.app. Usage: ./build.sh            → build/Chomp.app
#                         ./build.sh --install  → also copies it to /Applications
set -euo pipefail
cd "$(dirname "$0")"

if ! command -v swift >/dev/null 2>&1; then
  echo "→ Apple's command line tools are missing. Run: xcode-select --install"
  exit 1
fi

echo "→ Building…"
swift build -c release
BIN="$(swift build -c release --show-bin-path)/Chomp"

APP="build/Chomp.app"
rm -rf "$APP"
mkdir -p "$APP/Contents/MacOS" "$APP/Contents/Resources"
cp "$BIN" "$APP/Contents/MacOS/Chomp"
cp Resources/Info.plist "$APP/Contents/Info.plist"
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
  echo "! Video, audio, WebP and AVIF need ffmpeg: brew install ffmpeg"
fi

echo "→ Done. Look for the little chest in your menu bar."
open "$APP"
