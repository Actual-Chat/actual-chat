#!/usr/bin/env pwsh
# Records N cold-start profiles of the Android Release build.
# The app suspends at launch until the tracer attaches (DOTNET_DiagnosticPorts,
# baked in by android-tracing-env.txt when IsTracingEnabled=true), so collect
# must be listening before `am start`.
#
# Mode Sampling - thread-time stacks, for "where is time going".
# Mode Methods  - JIT + R2R method events, for _Profiling/CreateMibc-AR.ps1.
#   The sampler is what makes a capture 200+ MB; dropping it keeps the EventPipe
#   buffer from wrapping, and a wrap silently discards the *earliest* events -
#   exactly the startup methods a .mibc is being recorded for.
# Mode Jit      - JIT events only, no R2R. Run it through CreateMibc-AR.ps1 and the
#   resulting method list *is* the set of methods startup had to JIT, which is how
#   R2R coverage of a build gets measured (fewer = more was precompiled).
# Mode Attach   - Methods, minus the force-stop at the end, so the app is left in the
#   state it was driven into. A tracing build cannot be driven by hand at all until
#   something attaches, so this is how you sign in - and a hand-driven session reaches
#   UI the cold starts never do, which is coverage worth keeping rather than discarding.
#   Do not be tempted to cheapen the provider mask here: below 0x4000080018 the runtime
#   emits no MethodDetails and dotnet-pgo rejects the whole trace.

param(
    [int] $Runs = 3,
    # Startup is ~2s and the *ServiceStarter work is done by ~6s; past 10s a run only
    # adds trace volume. Cold starts barely vary, so 3 runs is plenty.
    [string] $Duration = "00:00:10",
    [string] $OutDir = "tmp/profiles",
    [string] $Package = "chat.actual.dev.app",
    [ValidateSet("Sampling", "Methods", "Jit", "Attach")]
    [string] $Mode = "Sampling",
    [int] $BufferSizeMB = 0,
    # Publishes and installs the tracing APK first. Without IsTracingEnabled the app has no
    # diagnostic port at all and dotnet-trace simply never finds it.
    [switch] $Build
)

$ErrorActionPreference = "Continue"
New-Item -ItemType Directory -Force -Path $OutDir | Out-Null

if ($Build) {
    $repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
    $proj = Join-Path $repoRoot "src/dotnet/App.Maui/App.Maui.csproj"
    dotnet publish $proj -f:net11.0-android -p:IsTracingEnabled=true -p:EmbedAssembliesIntoApk=true -c:Release
    if ($LASTEXITCODE -ne 0) { throw "publish failed ($LASTEXITCODE)" }
    $apk = Join-Path $repoRoot "artifacts/publish/App.Maui/release_net11.0-android/chat.actual.dev.app-Signed.apk"
    adb install -r $apk
    if ($LASTEXITCODE -ne 0) { throw "adb install failed ($LASTEXITCODE)" }
}

if ($BufferSizeMB -le 0) {
    $BufferSizeMB = if ($Mode -eq "Sampling") { 256 } else { 1024 }
}

# Loader (0x8) + Jit (0x10) + Type (0x80000), then the JIT/R2R block. dotnet-pgo documents
# 0x1E000080018 as "both JIT and R2R events" and 0x1C000080018 as "only JIT events"; the
# R2R half is what captures precompiled methods, which never raise a JIT event.
$mask = if ($Mode -eq "Jit") { "0x1C000080018" } else { "0x1F000080018" }
$providers = "Microsoft-Windows-DotNETRuntime:${mask}:5"

for ($i = 1; $i -le $Runs; $i++) {
    $out = Join-Path $OutDir "startup-$i.nettrace"
    Write-Output "=== run $i/$Runs [$Mode] -> $out"

    adb shell am force-stop $Package | Out-Null
    adb reverse tcp:9000 tcp:9001 | Out-Null

    $collectArgs = @(
        "collect", "--dsrouter", "android",
        "--duration", $Duration,
        "--buffersize", $BufferSizeMB,
        "--providers", $providers,
        "-o", $out
    )
    if ($Mode -eq "Sampling") {
        $collectArgs += @("--profile", "dotnet-sampled-thread-time")
    }

    $collect = Start-Process -PassThru -NoNewWindow -FilePath "dotnet-trace" -ArgumentList $collectArgs

    # Give dsrouter time to bind 9001 before the app tries to connect
    Start-Sleep -Seconds 4
    adb shell am start -S -W -n "$Package/.MainActivity" | Select-String "TotalTime|Status|Error"

    if ($Mode -eq "Attach") {
        Write-Output "    app is running - drive it by hand until this window closes"
    }

    $collect | Wait-Process -Timeout 3600
    if (Test-Path $out) {
        $sizeMb = [Math]::Round((Get-Item $out).Length / 1MB, 1)
        Write-Output "    captured: $sizeMb MB"
    }
    else {
        Write-Output "    FAILED: no file produced"
    }

    # Leaving the app up after an Attach would defeat the point: whatever state it was
    # driven into is what the next cold start should begin from.
    if ($Mode -ne "Attach") {
        adb shell am force-stop $Package | Out-Null
    }
    Start-Sleep -Seconds 2
}

Write-Output "=== done"
Get-ChildItem $OutDir -Filter *.nettrace | ForEach-Object {
    Write-Output ("{0}  {1} MB" -f $_.Name, [Math]::Round($_.Length / 1MB, 1))
}
