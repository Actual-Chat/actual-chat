#!/usr/bin/env pwsh
# Records N cold-start CPU-sampling profiles of the Android Release build.
# The app suspends at launch until the tracer attaches (DOTNET_DiagnosticPorts,
# baked in by android-tracing-env.txt when IsTracingEnabled=true), so collect
# must be listening before `am start`.

param(
    [int] $Runs = 3,
    [string] $Duration = "00:00:05",
    [string] $OutDir = "tmp/profiles",
    [string] $Package = "chat.actual.dev.app"
)

$ErrorActionPreference = "Continue"
New-Item -ItemType Directory -Force -Path $OutDir | Out-Null

for ($i = 1; $i -le $Runs; $i++) {
    $out = Join-Path $OutDir "startup-$i.nettrace"
    Write-Output "=== run $i/$Runs -> $out"

    adb shell am force-stop $Package | Out-Null
    adb reverse tcp:9000 tcp:9001 | Out-Null

    $collect = Start-Process -PassThru -NoNewWindow -FilePath "dotnet-trace" -ArgumentList @(
        "collect", "--dsrouter", "android",
        "--duration", $Duration,
        "--profile", "dotnet-sampled-thread-time",
        # Loader (0x8) + JIT (0x10) at Verbose: makes MethodLoadVerbose fire, and the rundown at
        # session stop emit MethodDCStop for R2R methods - without these most frames don't symbolicate.
        "--providers", "Microsoft-Windows-DotNETRuntime:0x1F000080018:5",
        "-o", $out
    )

    # Give dsrouter time to bind 9001 before the app tries to connect
    Start-Sleep -Seconds 4
    adb shell am start -S -W -n "$Package/.MainActivity" | Select-String "TotalTime|Status"

    $collect | Wait-Process -Timeout 120
    if (Test-Path $out) {
        $sizeMb = [Math]::Round((Get-Item $out).Length / 1MB, 1)
        Write-Output "    captured: $sizeMb MB"
    }
    else {
        Write-Output "    FAILED: no file produced"
    }

    adb shell am force-stop $Package | Out-Null
    Start-Sleep -Seconds 2
}

Write-Output "=== done"
Get-ChildItem $OutDir -Filter *.nettrace | ForEach-Object {
    Write-Output ("{0}  {1} MB" -f $_.Name, [Math]::Round($_.Length / 1MB, 1))
}
