#!/usr/bin/env pwsh
# Compares two .mibc files by method: how many each has, how many they share, and what is
# only in one. Used to decide whether a fresh recording should be merged into the committed
# profile or replace it.
#
#   pwsh scripts/Compare-Mibc.ps1 -Old tmp/mibc-old/ios.mibc -New src/dotnet/App.Maui/_Profiling/ios.mibc
#
# The .mibc files are in Git LFS, so `git show` yields a pointer: copy the file from disk before recording.

param(
    [Parameter(Mandatory)] [string] $Old,
    [Parameter(Mandatory)] [string] $New
)

$ErrorActionPreference = "Stop"
$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
$pgo = Join-Path $repoRoot "dotnet-pgo.cmd"
$dumpDir = Join-Path $repoRoot "tmp/mibc"
New-Item -ItemType Directory -Force -Path $dumpDir | Out-Null

function Get-Methods([string] $path) {
    $dump = Join-Path $dumpDir ([IO.Path]::GetFileNameWithoutExtension($path) + "-compare-dump.txt")
    & $pgo dump -i $path -o $dump | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "dump failed for $path" }
    $set = [System.Collections.Generic.HashSet[string]]::new()
    foreach ($line in [IO.File]::ReadLines($dump)) {
        if ($line -match '"Method":\s*"(.*)"') { [void] $set.Add($Matches[1]) }
    }
    , $set
}

$oldSet = Get-Methods $Old
$newSet = Get-Methods $New
$common = [System.Collections.Generic.HashSet[string]]::new($oldSet)
$common.IntersectWith($newSet)
$onlyOld = $oldSet.Count - $common.Count
$onlyNew = $newSet.Count - $common.Count

"{0,-10} {1,8}" -f "old", $oldSet.Count
"{0,-10} {1,8}" -f "new", $newSet.Count
"{0,-10} {1,8}  ({2:P1} of old, {3:P1} of new)" -f "common", $common.Count, ($common.Count / [Math]::Max(1, $oldSet.Count)), ($common.Count / [Math]::Max(1, $newSet.Count))
"{0,-10} {1,8}  <- lost if the new file replaces the old one" -f "old only", $onlyOld
"{0,-10} {1,8}  <- gained either way" -f "new only", $onlyNew
"{0,-10} {1,8}  <- size of a merge" -f "union", ($common.Count + $onlyOld + $onlyNew)

if ($onlyOld -gt 0) {
    "`nold-only, by assembly (top 10):"
    $oldSet.Where({ -not $newSet.Contains($_) }) |
        ForEach-Object { if ($_ -match '^\[([^\]]*)\]') { $Matches[1] } } |
        Group-Object | Sort-Object Count -Descending | Select-Object -First 10 |
        ForEach-Object { "{0,8}  {1}" -f $_.Count, $_.Name }
}
