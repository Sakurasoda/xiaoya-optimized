# Build and package a ready-to-run zip: XiaoYaOpt.exe + skill timing table + README.
# Usage: powershell -ExecutionPolicy Bypass -File .\release.ps1 -Version 1.0.0
# Keep this file ASCII-only (Windows PowerShell 5.1 reads scripts in the ANSI code page).
param([Parameter(Mandatory = $true)][string]$Version)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path

& (Join-Path $root 'build.ps1')

$dist = Join-Path $root 'dist'
$stage = Join-Path $dist "XiaoYaOpt-$Version"
if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }
New-Item -ItemType Directory -Force $stage | Out-Null

Copy-Item (Join-Path $root 'bin\XiaoYaOpt.exe') $stage
Copy-Item (Join-Path $root 'data\*.csv') $stage
Copy-Item (Join-Path $root 'README.md') $stage
# Screenshots used by the README guide (same relative path, so the links work after unzipping)
$images = Join-Path $root 'docs\images'
if (Test-Path $images) {
    New-Item -ItemType Directory -Force (Join-Path $stage 'docs\images') | Out-Null
    Copy-Item (Join-Path $images '*.png') (Join-Path $stage 'docs\images')
}

$zip = Join-Path $dist "XiaoYaOpt-$Version.zip"
if (Test-Path $zip) { Remove-Item $zip -Force }
Add-Type -AssemblyName System.IO.Compression.FileSystem
[System.IO.Compression.ZipFile]::CreateFromDirectory($stage, $zip, [System.IO.Compression.CompressionLevel]::Optimal, $false)
Write-Host "Packaged: $zip"
Get-ChildItem $stage | ForEach-Object { Write-Host ("  " + $_.Name + "  " + $_.Length) }
