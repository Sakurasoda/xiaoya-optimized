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
# Optional app icon: assets\app.ico is kept local only (official game artwork, ignored by git)
$iconArgs = @()
$ico = Join-Path $root 'assets\app.ico'
if (Test-Path $ico) { $iconArgs = @("/win32icon:$ico") }
& $csc /nologo /target:winexe /optimize+ /codepage:65001 "/out:$exe" $iconArgs $refs $sources
if ($LASTEXITCODE -ne 0) { throw "Build failed (exit $LASTEXITCODE)" }
Write-Host "Built: $exe"

# Skill timing table shipped with the app (wiki data only; skill names are read from the game folder at run time)
Copy-Item (Join-Path $root 'data\*.csv') $out -Force
Write-Host "Copied: data\*.csv"

# Archive extractor (command line): unpacks ECO .hed/.dat archives
$tool = Join-Path $out 'XlsExtract.exe'
& $csc /nologo /target:exe /optimize+ /codepage:65001 "/out:$tool" (Join-Path $root 'tools\XlsExtract.cs') (Join-Path $src 'EcoArchive.cs')
if ($LASTEXITCODE -ne 0) { throw "Tool build failed (exit $LASTEXITCODE)" }
Write-Host "Built: $tool"

# Skill table generator (command line): merges client effect.ssp files with the Nekogame wiki pages
$merge = Join-Path $out 'SkillMerge.exe'
& $csc /nologo /target:exe /optimize+ /codepage:65001 "/out:$merge" (Join-Path $root 'tools\SkillMerge.cs')
if ($LASTEXITCODE -ne 0) { throw "SkillMerge build failed (exit $LASTEXITCODE)" }
Write-Host "Built: $merge"
