#!/bin/bash
# App Store build of Chomp. Usage: ./build.sh   → regenerates the Xcode project and builds a
# sandboxed, locally signed Chomp.app in build/ to try out. Releases go through Xcode's
# Product → Archive (see AppStore/RELEASE_GUIDE.md).
set -euo pipefail
cd "$(dirname "$0")"
command -v xcodegen >/dev/null || { echo "→ Needs XcodeGen: brew install xcodegen"; exit 1; }
xcodegen generate >/dev/null
xcodebuild -project Chomp.xcodeproj -scheme Chomp -configuration Release -derivedDataPath .dd \
  CODE_SIGN_IDENTITY=- CODE_SIGN_STYLE=Manual DEVELOPMENT_TEAM= build | grep -E "error:|\*\* BUILD"
rm -rf build && mkdir -p build && cp -R .dd/Build/Products/Release/Chomp.app build/
echo "→ Done: build/Chomp.app"
[[ "${CI:-}" == "true" ]] || open build/Chomp.app
