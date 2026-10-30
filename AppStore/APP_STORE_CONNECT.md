# Chomp: App Store Connect copy

Paste these into App Store Connect → your app → **App Store** tab (version 1.0) and **App Information**.
Character limits are Apple's; every field below fits.

---

## App Information

| Field | Value |
|---|---|
| **Name** (30) | `Chomp` · if it's taken: `Chomp: File Converter` |
| **Subtitle** (30) | `Drag a file, tap Shift, done.` |
| **Bundle ID** | the one in `project.yml` (set by whoever publishes) |
| **Primary category** | Utilities |
| **Secondary category** | Productivity |
| **Content rights** | Does not contain, show or access third-party content |
| **Age rating** | Answer "None" / "No" to everything → **4+** |
| **Copyright** | `2026 Aki Yamin` |

## Pricing and Availability

- **Price:** Free
- **Availability:** All countries

## App Privacy

- **Privacy Policy URL:** `https://zeruxth.github.io/Chomp/privacy.html` (page text is in `privacy.html` next to this file)
- **Data collection:** choose **"No, we do not collect data from this app."** → shows as **Data Not Collected**

---

## Version 1.0

### Promotional Text (170)

```
A tiny treasure chest that lives in your menu bar and eats your files. Drag one, tap Shift, and get back a JPG, a smaller video or a PDF.
```

### Description (4000)

```
Chomp is a file converter for your Mac that happens to be a treasure chest.

Start dragging any file. Tap Shift. A wheel pops up under your cursor. Drop the file on what you want it to become, and Chomp makes it and saves the new file for you. Your original stays exactly as it was.

TAP SHIFT TO CONVERT
• Photos: JPG, PNG, WebP, HEIC, AVIF, TIFF, GIF or PDF
• Videos: MP4, MOV, or just the sound as M4A
• PDFs: a picture of every page, or plain text
• Documents: PDF, Word, RTF, plain text, HTML

TAP ⌘ FOR TOOLS
• Make photos smaller, resize to 1080 for Instagram, or halve them
• Cut out the background of a photo
• Remove location data from photos before you share them
• Shrink a video under 10 MB for Discord, make it half size, mute it, speed it up, or grab a still
• Merge PDFs, split them into pages, or rotate them
• Combine images or documents into one PDF
• Count the words in a document

TAP ⌃ FOR ACTIONS
• AirDrop, zip, copy, copy the path, open in Terminal, or copy to the Desktop

GOOD TO KNOW
• Everything happens on your Mac. Nothing is uploaded, ever.
• Your original file is never touched. You always get a new copy.
• Made a mistake? Every result has an Undo button.
• New files go to Downloads/Chomped, or next to the original, or to any folder you pick.

AND ALSO
• Chomp eats a random snack with every file. There are ten. The brick is the crunchiest.
• Comes in six colours. One of them you have to earn by feeding it 100 files.
• Pixel text can be switched off if you'd rather read a regular font.

Chomp lives in the menu bar, works offline, and needs no account.
```

### Keywords (100, comma separated, no spaces after commas)

```
file converter,heic,jpg,png,webp,avif,pdf,compress,resize,video,mp4,image,zip,airdrop,drag,menu bar
```

### Support URL

```
https://github.com/Zeruxth/Chomp/issues
```

### Marketing URL

```
https://zeruxth.github.io/Chomp/
```

### Screenshots (Mac, 2880 × 1800)

Upload these in order, from `AppStore/screenshots/`:

1. `1-convert.png`: "Drag a file. Tap Shift."
2. `2-tools.png`: "Tap ⌘ for tools."
3. `3-done.png`: "Chomp eats it."
4. `4-actions.png`: "Tap ⌃ for actions."

To re-render them after changing the app: build, then run
`Chomp.app/Contents/MacOS/Chomp --store-shots ~/Downloads/shots`

### App Review Information

- **Sign-in required:** No
- **Contact:** the publisher's name, phone and email
- **Notes for the reviewer:**

```
Chomp is a menu bar app (no Dock icon). Look for the small chest in the menu bar.

To try it: start dragging any image from Finder, and while still holding the mouse button, tap the Shift key once. A wheel appears under the cursor. Drop the file on "JPG". The converted file is saved to ~/Downloads/Chomped.

Clicking the menu bar chest also opens a panel where you can drop or pick files, so the app works without the drag gesture too.

Chomp watches for mouse-down events only to notice when a file drag starts, and reads the modifier keys while that drag is in progress. It does not record keystrokes, needs no Accessibility permission, and has no network code. All conversion uses Apple frameworks (ImageIO, AVFoundation, PDFKit) and the bundled open-source libwebp.

If "Save to" is set to "Beside original", the app asks once, with the standard open panel, for permission to write into that folder.
```

### Export Compliance

- "Does your app use encryption?" → **No** (also declared in Info.plist with `ITSAppUsesNonExemptEncryption = NO`)

### What's New (not needed for 1.0; for 1.0.1 onward)

```
Chomp got hungrier. Bug fixes and a few new snacks.
```
