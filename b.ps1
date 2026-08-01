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

& dotnet $dll.FullName @args
exit $LASTEXITCODE
