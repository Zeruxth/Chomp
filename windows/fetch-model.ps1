# Puts the background-removal model next to Chomp.exe, in <dir>\models\cutout.onnx.
# Silueta: a 43 MB U²-Net trained for salient objects, from the rembg project (Apache 2.0, like U²-Net itself).
# Usage: windows/fetch-model.ps1 <publish dir>
param([Parameter(Mandatory)][string]$Dir)
$ErrorActionPreference = 'Stop'

$url = 'https://github.com/danielgatis/rembg/releases/download/v0.0.0/silueta.onnx'
$md5 = '55e59e0d8062d2f5d013f4725ee84782'   # the checksum rembg itself pins
$dest = Join-Path $Dir 'models'
New-Item -ItemType Directory -Force $dest | Out-Null
Invoke-WebRequest $url -OutFile "$dest\cutout.onnx"
$got = (Get-FileHash "$dest\cutout.onnx" -Algorithm MD5).Hash.ToLower()
if ($got -ne $md5) { Remove-Item "$dest\cutout.onnx"; throw "cutout.onnx checksum $got, expected $md5" }
@"
cutout.onnx is Silueta (https://github.com/xuebinqin/U-2-Net, slimmed by https://github.com/danielgatis/rembg).
Apache License 2.0. Downloaded from $url
"@ | Set-Content "$dest\SOURCE.txt"
Write-Host "cutout.onnx $((Get-Item "$dest\cutout.onnx").Length) bytes"
