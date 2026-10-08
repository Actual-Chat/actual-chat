#!/usr/bin/env pwsh
# The `b` command line - see build/Cli/CliApp.cs.
# Rebuilds the build project only when its sources changed, so a warm `b` starts in ~100ms
# instead of the ~2-4s `dotnet run --project build` costs.

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot

function Get-BuildDll {
    Get-ChildItem -Path (Join-Path $root 'artifacts/tools') -Recurse -File -Filter 'Build.dll' -ErrorAction SilentlyContinue |
        Sort-Object LastWriteTimeUtc -Descending |
        Select-Object -First 1
}

$newestSource = Get-ChildItem -Path (Join-Path $root 'build') -Recurse -File -Include *.cs, *.csproj |
    Measure-Object -Property LastWriteTimeUtc -Maximum |
    Select-Object -ExpandProperty Maximum

$dll = Get-BuildDll
if (-not $dll -or $dll.LastWriteTimeUtc -lt $newestSource) {
    Write-Host 'Rebuilding the build project...' -ForegroundColor DarkGray
    dotnet build (Join-Path $root 'build') -c Release --nologo -v q
    if ($LASTEXITCODE -ne 0) {
        exit $LASTEXITCODE
    }
    $dll = Get-BuildDll
}

# `pwsh -File` reads "-p:X=1" as a parameter with a value and hands the script "-p" and "X=1",
# so when this script is the process entry point its arguments are taken from the command line.
$rawArgs = $args
$commandLine = [Environment]::GetCommandLineArgs()
$scriptName = Split-Path -Leaf $PSCommandPath
for ($i = 1; $i -lt $commandLine.Count; $i++) {
    if ([IO.Path]::GetFileName($commandLine[$i]) -eq $scriptName) {
        $rawArgs = @($commandLine | Select-Object -Skip ($i + 1))
        break
    }
}

& dotnet $dll.FullName @rawArgs
exit $LASTEXITCODE
