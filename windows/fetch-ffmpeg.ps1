# Puts a Windows ffmpeg build next to Chomp.exe, in <dir>\ffmpeg\.
# BtbN's GPL build: it has x264, LAME, VP9, Opus and SVT-AV1, which Chomp's video, audio and AVIF options use.
# Usage: windows/fetch-ffmpeg.ps1 <publish dir>
param([Parameter(Mandatory)][string]$Dir)
$ErrorActionPreference = 'Stop'

$url = 'https://github.com/BtbN/FFmpeg-Builds/releases/download/latest/ffmpeg-n8.1-latest-win64-gpl-8.1.zip'
$tmp = Join-Path ([IO.Path]::GetTempPath()) "chomp-ffmpeg-$([guid]::NewGuid())"
New-Item -ItemType Directory $tmp | Out-Null
try {
    Invoke-WebRequest $url -OutFile "$tmp\ff.zip"
    Expand-Archive "$tmp\ff.zip" "$tmp\x"
    $root = Get-ChildItem "$tmp\x" -Directory | Select-Object -First 1
    $dest = Join-Path $Dir 'ffmpeg'
    New-Item -ItemType Directory -Force $dest | Out-Null
    Copy-Item "$($root.FullName)\bin\ffmpeg.exe" $dest
    # GPL: ship its licence, and say where the source lives.
    if (Test-Path "$($root.FullName)\LICENSE.txt") { Copy-Item "$($root.FullName)\LICENSE.txt" $dest }
    "ffmpeg built by https://github.com/BtbN/FFmpeg-Builds from https://ffmpeg.org source.`r`nDownloaded from $url" |
        Set-Content "$dest\SOURCE.txt"
    & "$dest\ffmpeg.exe" -hide_banner -version | Select-Object -First 1
}
finally {
    Remove-Item -Recurse -Force $tmp
}
