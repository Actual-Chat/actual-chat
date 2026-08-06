#!/usr/bin/env pwsh
# Converts the .nettrace files recorded by
#   pwsh scripts/Record-AndroidStartupProfiles.ps1 -Mode Methods   (-Platform Android)
#   pwsh scripts/Record-WindowsStartupProfiles.ps1 -Mode Methods   (-Platform Windows)
# into android.mibc / windows.mibc.
#
# The references have to be the R2R assemblies, not the pre-R2R ones in
# linked/: crossgen2 rewrites each assembly, so linked/ resolves to the same methods but
# makes dotnet-pgo report an MVID mismatch for every module ("Unable to validate match
# between assembly ...").
#
# Every trace is converted separately and the results merged - startup order is
# not deterministic (thread pool, background init), so one cold start is not the
# set of methods startup can touch. A merge is a union, so sessions that exercised
# different parts of the app only ever add coverage; -Accumulate keeps parts from
# earlier recording rounds in the merge as well.

param(
    [ValidateSet("Android", "AndroidNotification", "Windows")]
    [string] $Platform = "Android",
    [string] $TraceDir = "",
    [string] $Output = "",
    [switch] $Accumulate
)

$ErrorActionPreference = "Stop"
$here = $PSScriptRoot
$repoRoot = Resolve-Path (Join-Path $here "..")
$pgo = Join-Path $repoRoot "dotnet-pgo.cmd"

$objName, $mibcName, $defaultTraceDir = switch ($Platform) {
    "Android" { "release_net11.0-android_android-arm64", "android.mibc", "tmp/profiles" }
    "AndroidNotification" { "release_net11.0-android_android-arm64", "android-notif.mibc", "tmp/notif-profiles" }
    "Windows" { "release_net11.0-windows10.0.22621.0_win-x64", "windows.mibc", "tmp/win-profiles" }
}
if ($TraceDir -eq "") { $TraceDir = $defaultTraceDir }
if ($Output -eq "") { $Output = Join-Path $repoRoot "src/dotnet/App.Maui/_Profiling/$mibcName" }

$refs = Join-Path $repoRoot "artifacts/obj/App.Maui/$objName/R2R"
if (-not (Test-Path $refs)) {
    throw "No Release assemblies at $refs - publish $Platform Release first."
}

$traces = @(Get-ChildItem (Join-Path $repoRoot $TraceDir) -Filter *.nettrace | Sort-Object Name)
if ($traces.Count -eq 0) {
    throw "No .nettrace files in $TraceDir."
}

$partsRoot = Join-Path $repoRoot "tmp/mibc"
# Parts are namespaced by trace dir: separate recording rounds reuse startup-N.nettrace
# names, and a flat parts dir would have each round overwrite the last.
$tmpDir = Join-Path $partsRoot (Split-Path $TraceDir -Leaf)
New-Item -ItemType Directory -Force -Path $tmpDir | Out-Null

$parts = @()
foreach ($trace in $traces) {
    $part = Join-Path $tmpDir ($trace.BaseName + ".mibc")
    Write-Output "=== $($trace.Name) -> $(Split-Path $part -Leaf)"
    & $pgo create-mibc `
        --trace $trace.FullName `
        --output $part `
        --compressed `
        --reference "$refs/*.dll"
    if ($LASTEXITCODE -ne 0) {
        Write-Output "    FAILED (exit $LASTEXITCODE) - skipping"
        continue
    }
    $parts += $part
}

if ($Accumulate) {
    $parts = @(Get-ChildItem $partsRoot -Recurse -Filter *.mibc | ForEach-Object { $_.FullName } | Sort-Object -Unique)
}

if ($parts.Count -eq 0) {
    throw "No .mibc parts were produced."
}

Write-Output "=== merging $($parts.Count) part(s) -> $Output"
$mergeArgs = @("merge")
foreach ($part in $parts) { $mergeArgs += @("-i", $part) }
$mergeArgs += @("-o", $Output, "--compressed")
& $pgo @mergeArgs
if ($LASTEXITCODE -ne 0) {
    throw "merge failed (exit $LASTEXITCODE)"
}

& $pgo dump -i $Output -o (Join-Path $repoRoot "tmp/mibc/$([IO.Path]::GetFileNameWithoutExtension($Output))-dump.txt")

# merged.mibc is what both platforms actually compile against, so it has to be rebuilt
# whenever either input changes - regenerating it here is the only way that invariant
# survives contact with someone re-recording just one platform. The union costs a little
# image size and buys coverage: each platform picks up the shared startup path as the
# other one exercised it, and crossgen2 skips whatever does not resolve for its target.
# android-notif.mibc is a kept artifact, not something re-recorded on every pass: capturing
# it needs a push sent by hand. So it is merged in from disk each time rather than being a
# by-product of this run.
$profilingDir = Join-Path $repoRoot "src/dotnet/App.Maui/_Profiling"
$inputs = @("android.mibc", "android-notif.mibc", "windows.mibc") |
    ForEach-Object { Join-Path $profilingDir $_ } |
    Where-Object { Test-Path $_ }
if ($inputs.Count -eq 0) {
    Write-Output "=== no per-platform .mibc files found - merged.mibc not updated"
    return
}

$merged = Join-Path $profilingDir "merged.mibc"
Write-Output "=== merging $($inputs.Count) platform profile(s) -> $merged"
$mergeArgs = @("merge")
foreach ($i in $inputs) { $mergeArgs += @("-i", $i) }
$mergeArgs += @("-o", $merged, "--compressed")
& $pgo @mergeArgs
if ($LASTEXITCODE -ne 0) {
    throw "merged.mibc merge failed (exit $LASTEXITCODE)"
}
& $pgo dump -i $merged -o (Join-Path $repoRoot "tmp/mibc/merged-dump.txt") | Select-String "# Methods:"
