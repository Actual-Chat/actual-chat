#!/usr/bin/env pwsh
# Records N cold-start profiles of the unpackaged Windows Release build, for
# _Profiling/CreateMibc-R.ps1 -Platform Windows.
#
# Unlike Android this needs no dsrouter and no IsTracingEnabled build: `dotnet-trace
# collect -- <exe>` launches the app itself and suspends it until the session is live,
# so the trace covers process startup against the build that is already published.
#
# Mode Methods - JIT + R2R method events, for building a .mibc.
# Mode Jit     - JIT events only; the resulting method list is what startup had to jit.

param(
    [int] $Runs = 2,
    [string] $Duration = "00:00:15",
    [string] $OutDir = "tmp/win-profiles",
    [ValidateSet("Methods", "Jit")]
    [string] $Mode = "Methods",
    [int] $BufferSizeMB = 1024,
    [string] $Exe = "",
    [int] $CallCountingDelayMs = 15000
)

$ErrorActionPreference = "Continue"
$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
if ($Exe -eq "") {
    $Exe = Join-Path $repoRoot "artifacts/publish/App.Maui/release_net11.0-windows10.0.22621.0_win-x64/ActualChat.exe"
}
if (-not (Test-Path $Exe)) {
    throw "No published Windows build at $Exe - publish with -p:WindowsPackageType=None first."
}
New-Item -ItemType Directory -Force -Path $OutDir | Out-Null

# The child inherits this, so the recording describes the tiering the app actually ships
# with (Start-W.cmd sets the same thing). 0 clears it instead of setting a zero delay,
# which is what you want when testing whether runtimeconfig alone can carry the setting.
if ($CallCountingDelayMs -gt 0) {
    $env:DOTNET_TC_CallCountingDelayMs = $CallCountingDelayMs
}
else {
    Remove-Item Env:DOTNET_TC_CallCountingDelayMs -ErrorAction SilentlyContinue
}

$mask = if ($Mode -eq "Jit") { "0x1C000080018" } else { "0x1F000080018" }
$providers = "Microsoft-Windows-DotNETRuntime:${mask}:5"

for ($i = 1; $i -le $Runs; $i++) {
    $out = Join-Path $OutDir "startup-$i.nettrace"
    Write-Output "=== run $i/$Runs [$Mode] -> $out"

    Get-Process ActualChat -ErrorAction SilentlyContinue | Stop-Process -Force
    Start-Sleep -Seconds 2

    dotnet-trace collect `
        --duration $Duration `
        --buffersize $BufferSizeMB `
        --providers $providers `
        -o $out `
        -- $Exe | Select-String -Pattern "Output File|Trace completed|error|Error"

    Get-Process ActualChat -ErrorAction SilentlyContinue | Stop-Process -Force
    Start-Sleep -Seconds 2

    if (Test-Path $out) {
        Write-Output ("    captured: {0} MB" -f [Math]::Round((Get-Item $out).Length / 1MB, 1))
    }
    else {
        Write-Output "    FAILED: no file produced"
    }
}
