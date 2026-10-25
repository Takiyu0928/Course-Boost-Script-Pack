# Time Saver launcher build - in-box .NET Framework 4.8 compiler (no SDK, no downloads).
#
# Target: Windows x64, framework-dependent. The launcher REQUIRES the Windows built-in
# .NET Framework 4.8 runtime; nothing is bundled, so it is NOT self-contained.
#
# Usage (from any directory):
#   powershell -NoProfile -ExecutionPolicy Bypass -File build.ps1
#   powershell -NoProfile -ExecutionPolicy Bypass -File build.ps1 -Debug
#   powershell -NoProfile -ExecutionPolicy Bypass -File build.ps1 -Test     # also build tests
#   powershell -NoProfile -ExecutionPolicy Bypass -File build.ps1 -NoCopyToRoot

param(
    [switch]$Debug,
    [switch]$Test,
    [switch]$NoCopyToRoot
)

$ErrorActionPreference = 'Stop'
$launcher = Split-Path -Parent $MyInvocation.MyCommand.Path
$root = Split-Path -Parent $launcher
$fw = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319'
$csc = Join-Path $fw 'csc.exe'

if (-not (Test-Path $csc)) { throw ('In-box C# compiler not found: ' + $csc) }

function Step([string]$message) { Write-Host ('==> ' + $message) }

# PowerShell 5.1 does NOT quote native arguments for you, so every path is quoted explicitly.
function Q([string]$value) {
    if ($null -eq $value) { return '""' }
    if ($value -match '[\s"]') { return '"' + $value + '"' }
    return $value
}

$outDir = Join-Path $launcher 'build'
if ($Debug) { $outDir = Join-Path $launcher 'build\debug' }
if (-not (Test-Path $outDir)) { New-Item -ItemType Directory -Force -Path $outDir | Out-Null }

$refs = @(
    (Join-Path $fw 'System.dll'),
    (Join-Path $fw 'System.Core.dll'),
    (Join-Path $fw 'System.Drawing.dll'),
    (Join-Path $fw 'System.Windows.Forms.dll'),
    (Join-Path $fw 'System.Xaml.dll'),
    (Join-Path $fw 'System.Xml.dll'),
    (Join-Path $fw 'System.Runtime.Serialization.dll'),
    (Join-Path $fw 'WPF\WindowsBase.dll'),
    (Join-Path $fw 'WPF\PresentationCore.dll'),
    (Join-Path $fw 'WPF\PresentationFramework.dll'),
    (Join-Path $fw 'WPF\PresentationUI.dll')
)
foreach ($r in $refs) { if (-not (Test-Path $r)) { throw ('Missing reference assembly: ' + $r) } }

$manifest = Join-Path $launcher 'src\app.manifest'
$appIcon = Join-Path $launcher 'assets\icon-v1\TimeSaver.ico'
if (-not (Test-Path -LiteralPath $appIcon)) { throw ('Missing application icon: ' + $appIcon) }

function Invoke-Csc {
    param(
        [string[]]$Sources,
        [string]$Target,
        [string]$OutFile,
        [switch]$WithManifest
    )
    $arguments = New-Object System.Collections.Generic.List[string]
    $arguments.Add('/nologo')
    $arguments.Add('/target:' + $Target)
    $arguments.Add('/platform:x64')
    $arguments.Add('/langversion:5')
    $arguments.Add('/utf8output')
    $arguments.Add('/out:' + (Q $OutFile))
    $arguments.Add('/win32icon:' + (Q $appIcon))
    $arguments.Add('/resource:' + (Q $appIcon) + ',TimeSaver.AppIcon.ico')
    if ($WithManifest -and (Test-Path $manifest)) { $arguments.Add('/win32manifest:' + (Q $manifest)) }
    if ($Debug) { $arguments.Add('/debug:portable'); $arguments.Add('/define:DEBUG') } else { $arguments.Add('/optimize+') }
    foreach ($r in $refs) { $arguments.Add('/r:' + (Q $r)) }
    foreach ($s in $Sources) { $arguments.Add((Q $s)) }

    Step ('csc -> ' + (Split-Path -Leaf $OutFile) + ' (' + $Sources.Count + ' sources)')
    $output = & $csc $arguments.ToArray() 2>&1
    $code = $LASTEXITCODE
    if ($output) { $output | ForEach-Object { Write-Host ('    ' + $_) } }
    if ($code -ne 0) { throw ('Compilation failed with exit code ' + $code) }
}

$srcDir = Join-Path $launcher 'src'
$sources = @(Get-ChildItem -Path $srcDir -Filter *.cs -Recurse -File | Sort-Object FullName | ForEach-Object { $_.FullName })
if ($sources.Count -eq 0) { throw ('No .cs sources under ' + $srcDir) }

$exeName = 'TimeSaver.exe'
$exePath = Join-Path $outDir $exeName
Invoke-Csc -Sources $sources -Target 'winexe' -OutFile $exePath -WithManifest

if (-not $NoCopyToRoot) {
    $rootExe = Join-Path $root $exeName
    Copy-Item -Path $exePath -Destination $rootExe -Force
    Step ('copied -> ' + $rootExe)
}

if ($Test) {
    $testDir = Join-Path $launcher 'tests'
    $testSources = @(Get-ChildItem -Path $testDir -Filter *.cs -Recurse -File | Sort-Object FullName | ForEach-Object { $_.FullName })
    if ($testSources.Count -eq 0) { throw ('No test sources under ' + $testDir) }
    # tests reference the launcher sources directly (same compilation) so internal types are visible
    $allSources = @($sources) + @($testSources) | Where-Object { $_ -notlike '*\Program.cs' }
    $testExe = Join-Path $outDir 'TimeSaver.Tests.exe'
    Invoke-Csc -Sources $allSources -Target 'exe' -OutFile $testExe
    Step ('tests built -> ' + $testExe)
}

Step 'build finished'
Write-Host ''
Write-Host ('  launcher : ' + $exePath)
if (-not $NoCopyToRoot) { Write-Host ('  root exe : ' + (Join-Path $root $exeName)) }
Write-Host '  runtime  : Windows built-in .NET Framework 4.8 (framework-dependent, NOT self-contained)'
