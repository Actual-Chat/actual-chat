#!/usr/bin/env pwsh
# Records the startup path taken when a push notification wakes the app, which is not the
# path scripts/Record-AndroidStartupProfiles.ps1 captures:
#
#   FirebaseMessagingService.OnMessageReceived runs in a process Android started for the
#   *service*, with no Activity and none of the MauiProgram UI path. Tapping the
#   notification then reaches MainActivity.OnCreate (or OnNewIntent) with the notification
#   Intent. Neither shows up in a launcher-tap recording.
#
# Requires a tracing build (IsTracingEnabled=true) installed and signed in, and a way to
# send this device a push - on a dev build that means a *dev* account, not the prod bots.
#
# Usage:
#   pwsh scripts/Record-AndroidNotificationProfile.ps1 -Warm
#   ...send the push while the collector is waiting...
#   pwsh scripts/New-StartupMibc.ps1 -Platform Android -TraceDir tmp/notif-profiles

param(
    [string] $Duration = "00:00:40",
    [string] $OutDir = "tmp/notif-profiles",
    [string] $Package = "chat.actual.dev.app",
    [int] $BufferSizeMB = 1024,
    # Launches the app once first. Needed after anything that force-stopped it (every other
    # script here does, at the end of each run).
    [switch] $Warm
)

$ErrorActionPreference = "Continue"
New-Item -ItemType Directory -Force -Path $OutDir | Out-Null
$providers = "Microsoft-Windows-DotNETRuntime:0x1F000080018:5"

adb reverse tcp:9000 tcp:9001 | Out-Null

if ($Warm) {
    # A tracing build blocks at launch until something attaches, so warming it needs a
    # throwaway collector running at the same time.
    Write-Output "=== warming (clearing the stopped state)"
    $warm = Start-Process -PassThru -NoNewWindow -FilePath "dotnet-trace" -ArgumentList @(
        "collect", "--dsrouter", "android", "--duration", "00:00:25",
        "--buffersize", "64", "--providers", $providers,
        "-o", (Join-Path $OutDir "warmup.nettrace"))
    Start-Sleep -Seconds 4
    adb shell am start -S -W -n "$Package/.MainActivity" | Select-String "Status|Error"
    $warm | Wait-Process -Timeout 120
    adb shell input keyevent KEYCODE_HOME | Out-Null
    Start-Sleep -Seconds 3
}

# am kill, NEVER am force-stop. force-stop puts the app in Android's stopped state, and a
# stopped app receives no FCM at all - the push would simply never arrive and the capture
# would look like a tooling failure.
Write-Output "=== killing the process (leaving the app deliverable)"
adb shell am kill $Package | Out-Null
Start-Sleep -Seconds 3
$pid0 = (adb shell pidof $Package) -replace '\s',''
if ($pid0) {
    Write-Output "    WARNING: still running as pid $pid0 - am kill only reaps background processes."
    Write-Output "    Background the app (KEYCODE_HOME) and retry, or the trace starts mid-process."
}

$out = Join-Path $OutDir "notification-1.nettrace"
Write-Output "=== collecting for $Duration -> $out"
$collect = Start-Process -PassThru -NoNewWindow -FilePath "dotnet-trace" -ArgumentList @(
    "collect", "--dsrouter", "android", "--duration", $Duration,
    "--buffersize", $BufferSizeMB, "--providers", $providers, "-o", $out)

Start-Sleep -Seconds 4
Write-Output ""
Write-Output "  >>> SEND THE PUSH NOW (post a message to this device's account on dev)."
Write-Output "  >>> Tap the notification too if you also want the MainActivity path."
Write-Output ""

$collect | Wait-Process -Timeout 600
$pid1 = (adb shell pidof $Package) -replace '\s',''
if (Test-Path $out) {
    Write-Output ("    captured: {0} MB, app pid after: {1}" -f `
        [Math]::Round((Get-Item $out).Length / 1MB, 1), $(if ($pid1) { $pid1 } else { "<not running>" }))
    if (-not $pid1) {
        Write-Output "    The app never started - the push did not arrive. Check that it was sent to"
        Write-Output "    the account signed in on this device, on the same environment as the build."
    }
}
else {
    Write-Output "    FAILED: no file produced"
}
