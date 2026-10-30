# Runs real conversions through `Chomp.exe --run` and checks the files appear. No UI involved.
# Usage (from the repo root): windows/smoke-test.ps1 <path to Chomp.exe>
param([Parameter(Mandatory)][string]$Exe)
$ErrorActionPreference = 'Stop'

$Exe = (Resolve-Path $Exe).Path
$work = Join-Path ([IO.Path]::GetTempPath()) 'chomp-smoke'
Remove-Item -Recurse -Force $work -ErrorAction SilentlyContinue
New-Item -ItemType Directory $work | Out-Null

Copy-Item Resources/AppIcon.iconset/icon_256x256.png "$work\icon.png"
"# Notes`r`n`r`nChomp eats **files**." | Set-Content "$work\notes.md"

$ff = Join-Path (Split-Path $Exe) 'ffmpeg\ffmpeg.exe'
$hasFF = Test-Path $ff
if ($hasFF) {
    & $ff -hide_banner -loglevel error -f lavfi -i testsrc=duration=2:size=320x240:rate=15 -f lavfi -i sine=duration=2 `
        -shortest -c:v libx264 -pix_fmt yuv420p -c:a aac "$work\clip.mp4"
}

$failed = 0
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

# PDF
Chomp to.jpg icon.pdf 'icon 2.jpg'
Chomp to.txt icon.pdf icon.txt
Chomp pdf.merge @('icon.pdf', 'Combined.pdf') 'Combined 2.pdf'
Chomp pdf.split Combined.pdf @('Combined (p01).pdf', 'Combined (p02).pdf')
Chomp pdf.rotate icon.pdf 'icon (rotated).pdf'

# Documents and actions
Chomp doc.clean notes.md 'notes (clean).txt'
Chomp doc.count notes.md @()
Chomp act.zip icon.png icon.zip

# Video and audio
if ($hasFF) {
    Chomp to.avif icon.png icon.avif
    Chomp to.gif clip.mp4 clip.gif
    Chomp to.webm clip.mp4 clip.webm
    Chomp to.mp3 clip.mp4 clip.mp3
    Chomp vid.10mb clip.mp4 'clip (10MB).mp4'
    Chomp vid.frame clip.mp4 'clip (frame).png'
    Chomp aud.loud clip.mp3 'clip (loud).mp3'
} else {
    Write-Host 'skip video/audio: no ffmpeg next to Chomp.exe'
}

if ($failed) { throw "$failed smoke test(s) failed" }
Write-Host 'All good.'
