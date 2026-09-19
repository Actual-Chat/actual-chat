#!/usr/bin/env pwsh
# Counts the ART GCs of the running app over a window and reports rate, cause and pause time.
# This is the A/B for DOTNET_GCgen0size (android-release-env.txt): the Java GC bridge follows
# every managed GC with an explicit full ART GC, so "Explicit" is the bridge's share and the
# number that should drop. Drive the same minute of use on the same phone before and after.
#
# ART tags its GC lines with the process name, not "art", so this reads the unfiltered log.

param(
    [int] $Seconds = 60,
    [string] $Package = "chat.actual.app",
    [string] $Adb = "adb"
)

$ErrorActionPreference = "Stop"

$pid_ = (& $Adb shell pidof $Package 2>$null | Out-String).Trim()
if (-not $pid_) {
    throw "$Package is not running - start the app (and the scenario) first."
}

# Clear, wait, dump: a streaming logcat stopped mid-flight loses whatever adb hasn't flushed,
# which at a couple of lines per minute is all of it.
& $Adb logcat -c
Write-Host "Recording GC lines of $Package (pid $pid_) for ${Seconds}s..."
Start-Sleep -Seconds $Seconds
$lines = @(& $Adb logcat -d -v threadtime --pid=$pid_ "*:I" | Where-Object { $_ -match ' GC freed ' })

$byCause = @{}
$pausedMs = 0.0
$totalMs = 0.0
foreach ($line in $lines) {
    # "<Cause> <collector> GC freed ... paused 1.2ms,3.4ms total 56.7ms" - cause is the first word
    if ($line -match ':\s+(\w+) .* GC freed .* paused ([^ ]+) total ([0-9.]+)(ms|us)') {
        $cause = $Matches[1]
        $byCause[$cause] = 1 + ($byCause[$cause] ?? 0)
        foreach ($pause in $Matches[2] -split ',') {
            if ($pause -match '([0-9.]+)(ms|us)') {
                $pausedMs += [double]$Matches[1] * ($Matches[2] -eq 'us' ? 0.001 : 1)
            }
        }
        if ($line -match 'total ([0-9.]+)(ms|us)') {
            $totalMs += [double]$Matches[1] * ($Matches[2] -eq 'us' ? 0.001 : 1)
        }
    }
}

$count = $lines.Count
Write-Host ""
Write-Host ("GCs: {0} in {1}s = {2:F1}/min" -f $count, $Seconds, ($count * 60.0 / $Seconds))
foreach ($cause in ($byCause.Keys | Sort-Object)) {
    Write-Host ("  {0,-12} {1}" -f $cause, $byCause[$cause])
}
Write-Host ("Paused (stop-the-world) total: {0:F1}ms; GC wall total: {1:F0}ms" -f $pausedMs, $totalMs)
