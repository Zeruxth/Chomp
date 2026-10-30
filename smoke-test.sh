#!/bin/bash
# Runs real conversions through `Chomp --run` and checks the files appear. No UI involved.
# Usage: ./smoke-test.sh build/Chomp.app
set -uo pipefail
APP="$(cd "$(dirname "$1")" && pwd)/$(basename "$1")"
EXE="$APP/Contents/MacOS/Chomp"
work="$(mktemp -d)"
trap 'rm -rf "$work"' EXIT
failed=0

cp Resources/AppIcon.iconset/icon_256x256.png "$work/icon.png"
printf '# Notes\n\nChomp eats **files**.\n\n- Café crumbs\n' > "$work/notes.md"
# A picture of words, for text recognition: Chomp itself turns a heading into a PDF, then the page into a PNG.
printf '# Chomp eats files\n' > "$work/words.md"
"$EXE" --run to.pdf "$work/words.md" >/dev/null 2>&1
"$EXE" --run to.png "$work/words.pdf" >/dev/null 2>&1

ff="$APP/Contents/MacOS/ffmpeg"
if [[ -x "$ff" ]]; then
  "$ff" -hide_banner -loglevel error -f lavfi -i testsrc=duration=2:size=320x240:rate=15 -f lavfi -i sine=duration=2 \
    -shortest -c:v libx264 -pix_fmt yuv420p -c:a aac "$work/clip.mp4"
fi

chomp() {   # op, files (comma separated), expected outputs (comma separated)
  local op="$1"; IFS=',' read -ra ins <<< "$2"; IFS=',' read -ra outs <<< "${3:-}"
  local args=(); for f in ${ins[@]+"${ins[@]}"}; do args+=("$work/$f"); done
  "$EXE" --run "$op" "${args[@]}" >"$work/.log" 2>&1
  local code=$? missing=()
  for o in ${outs[@]+"${outs[@]}"}; do [[ -e "$work/$o" ]] || missing+=("$o"); done
  if [[ $code -ne 0 || ${#missing[@]} -gt 0 ]]; then
    echo "FAIL $op (exit $code) missing: ${missing[*]:-}"; cat "$work/.log"; failed=$((failed + 1))
  else
    echo "ok   $op -> ${outs[*]:-}"
  fi
}
has() {     # file, text it must contain
  if grep -q "$2" "$work/$1" 2>/dev/null; then echo "ok   $1 has '$2'"; else echo "FAIL $1 lacks '$2'"; failed=$((failed + 1)); fi
}

chomp to.jpg icon.png icon.jpg
chomp to.webp icon.png icon.webp
chomp to.heic icon.png icon.heic
chomp img.half icon.png "icon (half).png"
chomp img.compress icon.png "icon (small).webp"
chomp img.cutout icon.png "icon (cutout).png"
chomp img.text words.png words.txt
has words.txt "Chomp"
chomp to.pdf notes.md notes.pdf
chomp to.docx notes.md notes.docx
chomp doc.count notes.md ""
chomp to.txt notes.pdf notes.txt
chomp act.zip icon.png icon.zip

if [[ -x "$ff" ]]; then
  "$ff" -hide_banner -encoders 2>/dev/null | grep -q libx264 && echo "ok   bundled ffmpeg has x264" || { echo "FAIL bundled ffmpeg lacks x264"; failed=$((failed + 1)); }
  chomp to.gif clip.mp4 clip.gif
  chomp to.webm clip.mp4 clip.webm
  chomp to.mp3 clip.mp4 clip.mp3
  chomp to.avif icon.png icon.avif
  chomp vid.fit clip.mp4 "clip (10MB).mp4"
  chomp vid.frame clip.mp4 "clip (frame).png"
  chomp aud.loud clip.mp3 "clip (loud).mp3"
else
  echo "FAIL no ffmpeg inside the app"; failed=$((failed + 1))
fi

[[ $failed -eq 0 ]] && echo "All good." || { echo "$failed smoke test(s) failed"; exit 1; }
