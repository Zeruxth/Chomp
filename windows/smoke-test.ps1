# Runs real conversions through `Chomp.exe --run` and checks the files appear. No UI involved.
# Usage (from the repo root): windows/smoke-test.ps1 <path to Chomp.exe>
param([Parameter(Mandatory)][string]$Exe)
$ErrorActionPreference = 'Stop'

$Exe = (Resolve-Path $Exe).Path
$work = Join-Path ([IO.Path]::GetTempPath()) 'chomp-smoke'
Remove-Item -Recurse -Force $work -ErrorAction SilentlyContinue
New-Item -ItemType Directory $work | Out-Null

Copy-Item Resources/AppIcon.iconset/icon_256x256.png "$work\icon.png"
# A picture of words, for the text-recognition test.
Add-Type -AssemblyName System.Drawing
$bmp = New-Object System.Drawing.Bitmap 900, 220
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.Clear([System.Drawing.Color]::White)
$g.TextRenderingHint = 'AntiAlias'
$g.DrawString('Chomp eats files', (New-Object System.Drawing.Font 'Arial', 60), [System.Drawing.Brushes]::Black, 20, 50)
$bmp.Save("$work\text.png")
$g.Dispose(); $bmp.Dispose()
@"
# Notes

Chomp eats **files** and *spits out* new ones.

- Café crumbs
- Brick

1. First
2. Second

> A quote.
"@ | Set-Content -Encoding utf8 "$work\notes.md"

$ff = Join-Path (Split-Path $Exe) 'ffmpeg\ffmpeg.exe'
$hasFF = Test-Path $ff
if ($hasFF) {
    & $ff -hide_banner -loglevel error -f lavfi -i testsrc=duration=2:size=320x240:rate=15 -f lavfi -i sine=duration=2 `
        -shortest -c:v libx264 -pix_fmt yuv420p -c:a aac "$work\clip.mp4"
}

$failed = 0

# The file's text must match the pattern (a regex, line by line).
function Text([string]$file, [string]$pattern) {
    if (-not (Test-Path "$work\$file") -or -not (Select-String -Quiet -Encoding utf8 -Path "$work\$file" -Pattern $pattern)) {
        Write-Host "FAIL $file doesn't contain /$pattern/"
        if (Test-Path "$work\$file") { Get-Content "$work\$file" | Write-Host }
        $script:failed++
    } else {
        Write-Host "ok   $file has /$pattern/"
    }
}

function Chomp([string]$op, [string[]]$files, [string[]]$expect) {
    $argv = @('--run', $op) + ($files | ForEach-Object { "`"$work\$_`"" })
    $p = Start-Process $Exe -ArgumentList $argv -Wait -PassThru -NoNewWindow
    $missing = $expect | Where-Object { -not (Test-Path "$work\$_") }
    if ($p.ExitCode -ne 0 -or $missing) {
        Write-Host "FAIL $op (exit $($p.ExitCode)) missing: $($missing -join ', ')"
        $script:failed++
    } else {
        Write-Host "ok   $op -> $($expect -join ', ')"
    }
}

# Images
Chomp to.jpg icon.png icon.jpg
Chomp to.webp icon.png icon.webp
Chomp to.tiff icon.png icon.tiff
Chomp to.gif icon.png icon.gif
Chomp to.bmp icon.png icon.bmp
Chomp to.png icon.webp 'icon 2.png'
Chomp to.pdf icon.png icon.pdf
Chomp img.half icon.png 'icon (half).png'
Chomp img.1080 icon.jpg 'icon (1080).jpg'
Chomp img.rotate icon.png 'icon (rotated).png'
Chomp img.strip icon.jpg 'icon (clean).jpg'
Chomp img.compress icon.png 'icon (small).webp'
Chomp img.pdf @('icon.png', 'icon.jpg') Combined.pdf

# Text in a picture: Windows' own recognition, if the build machine has an OCR language.
$p = Start-Process $Exe -ArgumentList @('--run', 'img.text', "`"$work\text.png`"") -Wait -PassThru -NoNewWindow
if (Test-Path "$work\text.txt") { Text text.txt 'Chomp' }
elseif ($p.ExitCode -eq 1) { Write-Host 'ok   img.text fails cleanly (no OCR language here)' }
else { Write-Host "FAIL img.text (exit $($p.ExitCode))"; $failed++ }

# PDF
Chomp to.jpg icon.pdf 'icon 2.jpg'
Chomp to.txt icon.pdf icon.txt
Chomp pdf.merge @('icon.pdf', 'Combined.pdf') 'Combined 2.pdf'
Chomp pdf.split Combined.pdf @('Combined (p01).pdf', 'Combined (p02).pdf')
Chomp pdf.rotate icon.pdf 'icon (rotated).pdf'

# Documents: out to every format, then back in from each one
Chomp doc.clean notes.md 'notes (clean).txt'
Chomp doc.count notes.md @()
Chomp to.docx notes.md notes.docx
Chomp to.rtf notes.md notes.rtf
Chomp to.html notes.md notes.html
Chomp to.odt notes.md notes.odt
Chomp to.pdf notes.md notes.pdf
Chomp to.txt notes.docx notes.txt
Chomp to.md notes.docx 'notes 2.md'
Chomp to.md notes.rtf 'notes 3.md'
Chomp to.md notes.html 'notes 4.md'
Chomp to.md notes.odt 'notes 5.md'
Chomp to.docx notes.pdf 'notes 2.docx'
Chomp to.rtf notes.pdf 'notes 2.rtf'
Chomp doc.merge @('notes.md', 'notes.docx', 'icon.pdf') 'Combined 3.pdf'
Text notes.txt 'Chomp eats files'
foreach ($f in 'notes 2.md', 'notes 3.md', 'notes 4.md', 'notes 5.md') {
    Text $f 'Chomp eats \*\*files\*\*'
    Text $f 'Café crumbs'
}
Text 'notes 2.md' '^# Notes'
Text 'notes 2.md' '^2\. Second'

# Actions
Chomp act.zip icon.png icon.zip

# Background removal, when the model is there
if (Test-Path (Join-Path (Split-Path $Exe) 'models\cutout.onnx')) {
    Chomp img.cutout icon.png 'icon (cutout).png'
} else {
    Write-Host 'skip cut out: no models\cutout.onnx next to Chomp.exe'
}

# HEIC needs Microsoft Store codecs that build machines usually don't have: with them it must work, without them
# it must fail cleanly (the message says what to install).
if (Get-AppxPackage Microsoft.HEVCVideoExtension* -ErrorAction SilentlyContinue) {
    Chomp to.heic icon.png icon.heic
} else {
    $p = Start-Process $Exe -ArgumentList @('--run', 'to.heic', "`"$work\icon.png`"") -Wait -PassThru -NoNewWindow
    if ($p.ExitCode -eq 1 -and -not (Test-Path "$work\icon.heic")) { Write-Host 'ok   to.heic fails cleanly without the HEVC extension' }
    else { Write-Host "FAIL to.heic without the HEVC extension (exit $($p.ExitCode))"; $failed++ }
}

# Video and audio
if ($hasFF) {
    Chomp to.avif icon.png icon.avif
    Chomp to.gif clip.mp4 clip.gif
    Chomp to.webm clip.mp4 clip.webm
    Chomp to.mp3 clip.mp4 clip.mp3
    Chomp vid.fit clip.mp4 'clip (10MB).mp4'
    Chomp vid.frame clip.mp4 'clip (frame).png'
    Chomp aud.loud clip.mp3 'clip (loud).mp3'
} else {
    Write-Host 'skip video/audio: no ffmpeg next to Chomp.exe'
}

if ($failed) { throw "$failed smoke test(s) failed" }
Write-Host 'All good.'
