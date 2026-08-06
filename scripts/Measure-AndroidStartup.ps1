#!/usr/bin/env pwsh
# Cold-start timing for the installed Android app, from `am start -W`.
#
# Run this against a NON-tracing build. A tracing build suspends until a tracer attaches,
# and EventPipe then emits hundreds of MB per run - the overhead swamps what is being
# measured. Record-AndroidStartupProfiles.ps1 answers "what got jitted"; this answers
# "how long did it take", and the two need different builds.
#
# TotalTime is Android's launch-to-first-frame. Voxt keeps working well past that -
# the *ServiceStarter work runs for several more seconds - so treat this as a lower
# bound on startup, not as time-to-usable.

# Report the minimum, not the mean: the spread here is background noise on the device,
# so the fastest run is the one least polluted by it.

param(
    [int] $Runs = 5,
    [string] $Package = "chat.actual.dev.app",
    [int] $SettleSeconds = 2
)

$ErrorActionPreference = "Continue"
$times = @()
$chatList = @()
$rendered = @()

for ($i = 1; $i -le $Runs; $i++) {
    adb shell am force-stop $Package | Out-Null
    Start-Sleep -Seconds $SettleSeconds
    adb logcat -c | Out-Null

    $out = adb shell am start -S -W -n "$Package/.MainActivity"
    $total = ($out | Select-String -Pattern '^TotalTime:\s*(\d+)').Matches.Groups[1].Value

    # MarkChatListLoaded is LoadingUI.ChatListLoadTime - the number the app itself shows -
    # in seconds since process start. am start's TotalTime stops at the first frame, which
    # for a Blazor WebView app is the splash, well before anything is usable.
    Start-Sleep -Seconds 6
    $log = adb logcat -d
    function Mark([string] $name) {
        $m = $log | Select-String -Pattern "LoadingUI[^:]*:\s*([\d.]+)s $name" | Select-Object -First 1
        if ($m) { [int] ([double] $m.Matches.Groups[1].Value * 1000) } else { $null }
    }
    $ms = Mark "MarkChatListLoaded"
    $rms = Mark "MarkRendered"

    if ($total) { $times += [int] $total }
    if ($ms) { $chatList += $ms }
    if ($rms) { $rendered += $rms }
    Write-Output ("run {0}/{1}: TotalTime={2}  ChatListLoaded={3}  Rendered={4}" -f `
        $i, $Runs, $(if ($total) { "$total ms" } else { "?" }),
        $(if ($ms) { "$ms ms" } else { "?" }), $(if ($rms) { "$rms ms" } else { "?" }))
}

adb shell am force-stop $Package | Out-Null

function Report([string] $label, [int[]] $values) {
    if ($values.Count -eq 0) { return }
    $stats = $values | Measure-Object -Average -Minimum -Maximum
    Write-Output ("{0,-14} n={1}  min={2}  mean={3}  max={4}" -f `
        $label, $values.Count, $stats.Minimum, [Math]::Round($stats.Average), $stats.Maximum)
}

Write-Output ""
Report "TotalTime" $times
Report "ChatListLoaded" $chatList
Report "Rendered" $rendered
