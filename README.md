# Chomp

Zero-click file converter for Mac. Lives in the menu bar.

## Use

Start dragging any file or folder, then tap:

- **⇧ Shift**: convert (JPG, WebP, HEIC, MP4, GIF, MP3, PDF, DOCX…)
- **⌘ Command**: tools (compress, 10 MB video, cut out background, merge PDFs, word count…)
- **⌃ Control**: actions (AirDrop, copy, copy path, zip, Terminal, Desktop)

A wheel opens under the cursor. Drop on a segment and you're done. Drop in the middle to cancel. While dragging, you can also hover the pills under the wheel to switch modes.

There are other ways in, too: right-click a file and choose Open With → Chomp, or drop files on the menu bar panel. Then click a segment, use ←/→ and Return, or press 1–9.

Your originals are never touched. Results go next to the original (or to Downloads/Chomp, which you can set in the menu bar panel). After each job, the toast at the bottom shows how much space you saved, plus **Show** and **Undo** buttons.

## Build

```bash
xcode-select --install     # once, if you don't have Apple's command line tools
brew install ffmpeg        # for video, audio, WebP, AVIF
./build.sh --install
```

Requires macOS 13+. Cut out needs macOS 14+.

## Files

- `DragWatcher.swift`: spots file drags anywhere and catches the key taps. No Accessibility permission needed.
- `Wheel.swift`: the wheel window, hit-testing, layout.
- `PixelArt.swift`: chest skins, pixel icons, pixel fonts, frames, and the pixel-by-pixel wheel disc.
- `Mimic.swift`: Chomp himself, frame by frame.
- `Catalog.swift`: which options show for which file type.
- `Engine.swift`, `Converters.swift`: the actual work (ImageIO, PDFKit, textutil, ffmpeg).
- `HUD.swift`: the toast. `MenuPanel.swift`: menu bar panel and settings.
- `Snapshot.swift`: `Chomp.app/Contents/MacOS/Chomp --snapshot <folder> [--skin slime]` saves PNGs of the wheel, toast and panel.

Fonts: Pixelify Sans and Silkscreen, both SIL Open Font License, in `Resources/Fonts`.
