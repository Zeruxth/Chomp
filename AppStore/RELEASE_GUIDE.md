# Releasing Chomp on the Mac App Store

For whoever publishes Chomp from their Apple Developer account. About 30 minutes the first time.

## 1. Point the project at your team

Open `project.yml` and change the two lines marked `CHANGE ME`:

```yaml
DEVELOPMENT_TEAM: "ABCDE12345"                  # your Team ID (developer.apple.com → Membership)
PRODUCT_BUNDLE_IDENTIFIER: com.yourname.chomp    # any reverse-domain ID you own
```

Then regenerate the Xcode project:

```bash
brew install xcodegen   # once
xcodegen generate
```

(Or skip XcodeGen: open `Chomp.xcodeproj`, select the **Chomp** target → **Signing & Capabilities**, and pick your team and bundle ID there. XcodeGen would overwrite that on the next `xcodegen generate`, so prefer editing `project.yml`.)

## 2. Create the app in App Store Connect

1. [appstoreconnect.apple.com](https://appstoreconnect.apple.com) → **Apps** → **+** → **New App**.
2. Platform **macOS**, name **Chomp** (or `Chomp: File Converter` if taken), language English, the bundle ID from step 1, SKU `chomp-mac`.

## 3. Upload a build

1. Open `Chomp.xcodeproj` in Xcode.
2. Set the run destination to **Any Mac**.
3. **Product → Archive**.
4. In the Organizer: **Distribute App → App Store Connect → Upload**. Keep the defaults (automatic signing).
5. Wait for the "build processed" email (5 to 30 minutes).

For later versions, bump `MARKETING_VERSION` (e.g. `1.0.1`) and `CURRENT_PROJECT_VERSION` (e.g. `2`) in `project.yml`, regenerate, and archive again.

## 4. Fill in the listing

Everything is written already in `APP_STORE_CONNECT.md`: name, subtitle, description, keywords, URLs, review notes, age rating and privacy answers. Screenshots are in `screenshots/`, already 2880 × 1800.

The privacy policy is already live at `https://zeruxth.github.io/Chomp/privacy.html`.

## 5. Submit

Pick the uploaded build on the version page, then **Add for Review → Submit**. Review usually takes 1 to 3 days.

## Unsigned test builds on GitHub

The `appstore` branch has its own workflow (`.github/workflows/appstore.yml`). Every push builds the
app on GitHub. To put a downloadable, unsigned copy on the Releases page:

```bash
git tag appstore-v1.0.0
git push origin appstore-v1.0.0
```

These releases are never marked "latest", so the website keeps offering the regular version.

## Good to know

- The App Store listing shows **your** name or company as the seller, since it's your account. You can transfer the app to Aki's own account later (App Store Connect → App Information → Transfer App) without losing reviews or users.
- This build is sandboxed and has no ffmpeg (sandboxed apps can't run it, and its licence doesn't fit the store). Video and audio use Apple's AVFoundation, so a few formats from the GitHub version (WebM, MKV, GIF from video, MP3, OGG, FLAC, loudness, trim silence) aren't in this version.
- First-time saves go to `~/Downloads/Chomped`. "Beside original" asks once per folder.
- If review asks about input monitoring: Chomp only watches for mouse-down to notice a file drag, then reads the modifier keys during that drag. No Accessibility permission, no keystroke logging. This is also in the review notes.
