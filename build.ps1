# Build with the C# compiler that ships with Windows (.NET Framework 4). No SDK required.
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path $csc)) { $csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe' }

$out = Join-Path $root 'bin'
New-Item -ItemType Directory -Force $out | Out-Null
$src = Join-Path $root 'src'
$refs = '/r:System.dll', '/r:System.Drawing.dll', '/r:System.Windows.Forms.dll', '/r:System.Xml.dll'

# Main app
$exe = Join-Path $out 'XiaoYaOpt.exe'
$sources = Get-ChildItem $src -Filter *.cs | ForEach-Object { $_.FullName }
& $csc /nologo /target:winexe /optimize+ /codepage:65001 "/out:$exe" $refs $sources
if ($LASTEXITCODE -ne 0) { throw "Build failed (exit $LASTEXITCODE)" }
Write-Host "Built: $exe"

# Archive extractor (command line): unpacks ECO .hed/.dat archives
$tool = Join-Path $out 'XlsExtract.exe'
& $csc /nologo /target:exe /optimize+ /codepage:65001 "/out:$tool" (Join-Path $root 'tools\XlsExtract.cs') (Join-Path $src 'EcoArchive.cs')
if ($LASTEXITCODE -ne 0) { throw "Tool build failed (exit $LASTEXITCODE)" }
Write-Host "Built: $tool"
