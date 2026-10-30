#!/bin/bash
# Puts a universal (Apple Silicon + Intel) static ffmpeg in vendor/ffmpeg/, which build.sh then ships inside Chomp.app.
# Martin Riedl's builds: x264, LAME, libvpx, Opus, SVT-AV1, aom and dav1d, everything Chomp's video, audio and AVIF use.
# Pinned to one release and checked against its SHA-256 files. Usage: ./fetch-ffmpeg.sh
set -euo pipefail
cd "$(dirname "$0")"

VERSION="9.0.2"
BASE="https://ffmpeg.martin-riedl.de/download/macos"
ARM="$BASE/arm64/1789931890_$VERSION/ffmpeg.zip"
INTEL="$BASE/amd64/1789931006_$VERSION/ffmpeg.zip"

tmp="$(mktemp -d)"
trap 'rm -rf "$tmp"' EXIT
for arch in arm64 x86_64; do
  url="$ARM"; [[ "$arch" == "x86_64" ]] && url="$INTEL"
  mkdir -p "$tmp/$arch"
  curl -fsSL "$url" -o "$tmp/$arch/ffmpeg.zip"
  want="$(curl -fsSL "$url.sha256" | awk '{print $1}')"
  got="$(shasum -a 256 "$tmp/$arch/ffmpeg.zip" | awk '{print $1}')"
  [[ "$want" == "$got" ]] || { echo "ffmpeg $arch checksum $got, expected $want"; exit 1; }
  unzip -q "$tmp/$arch/ffmpeg.zip" -d "$tmp/$arch"
done

mkdir -p vendor/ffmpeg
lipo -create "$tmp/arm64/ffmpeg" "$tmp/x86_64/ffmpeg" -output vendor/ffmpeg/ffmpeg
chmod +x vendor/ffmpeg/ffmpeg
cat > vendor/ffmpeg/SOURCE.txt <<TXT
Chomp includes ffmpeg $VERSION (https://ffmpeg.org), licensed under the GNU GPL version 2 or later
because it is built with GPL libraries such as x264. Source code: https://ffmpeg.org/download.html
Static builds by Martin Riedl (https://ffmpeg.martin-riedl.de), from:
  $ARM
  $INTEL
TXT
echo "→ $(vendor/ffmpeg/ffmpeg -hide_banner -version | head -1) ($(lipo -archs vendor/ffmpeg/ffmpeg))"
