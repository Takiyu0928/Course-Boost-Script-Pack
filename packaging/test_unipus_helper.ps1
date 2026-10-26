$ErrorActionPreference = 'Stop'
$taskRoot = Join-Path ([IO.Path]::GetTempPath()) ("Unipus helper's test " + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $taskRoot | Out-Null
try {
    $stub = Join-Path $taskRoot 'stub.exe'
    Add-Type -TypeDefinition @'
using System;
using System.IO;
class ImportStub {
    static int Main(string[] args) {
        File.WriteAllText("called.txt", string.Join("|", args));
        return int.Parse(File.ReadAllText("config.json"));
    }
}
'@ -OutputAssembly $stub -OutputType ConsoleApplication
    $helper = Join-Path $PSScriptRoot '../docs/unipus/Import-Missing-Units.cmd'
    foreach ($mode in @('inside', 'root', 'missing-config', 'missing-exe', 'failure')) {
        $case = Join-Path $taskRoot $mode
        $tool = if ($mode -eq 'root') { Join-Path $case 'UnipusAI-v3.6-windows-x86-64' } else { $case }
        New-Item -ItemType Directory -Path $tool -Force | Out-Null
        if ($mode -ne 'missing-exe') { Copy-Item -LiteralPath $stub -Destination (Join-Path $tool 'UnipusAI.exe') }
        if ($mode -ne 'missing-config') {
            $code = if ($mode -eq 'failure') { '23' } else { '0' }
            [IO.File]::WriteAllText((Join-Path $tool 'config.json'), $code)
        }
        $entry = Join-Path $case 'Import-Missing-Units.cmd'
        Copy-Item -LiteralPath $helper -Destination $entry
        & $entry --no-pause | Out-Null
        $actual = $LASTEXITCODE
        $expected = if ($mode.StartsWith('missing')) { 2 } elseif ($mode -eq 'failure') { 23 } else { 0 }
        if ($actual -ne $expected) { throw "$mode exit code: $actual expected $expected" }
        $called = Join-Path $tool 'called.txt'
        if ($expected -eq 2) {
            if (Test-Path -LiteralPath $called) { throw "$mode unexpectedly ran EXE" }
        } elseif ([IO.File]::ReadAllText($called) -ne 'dump-text') { throw "$mode incorrect arguments" }
        Write-Output "PASS $mode"
    }
} finally {
    $resolved = [IO.Path]::GetFullPath($taskRoot)
    $temp = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
    if (-not $resolved.StartsWith($temp, [StringComparison]::OrdinalIgnoreCase)) { throw 'Unsafe test cleanup path' }
    Remove-Item -LiteralPath $resolved -Recurse -Force
}
