# server-loop.ps1
# Edit-run-restart loop for the Voxt server.
#
# Runs three steps in sequence:
#   1) npm-build     - npm run build:Debug
#   2) dotnet-build  - dotnet build -c $Configuration App.Server.csproj
#   3) server-run    - dotnet run   -c $Configuration App.Server.csproj -- -kb
#
# Per-step output goes to tmp/server-loop-<step>.log. Stage transitions are
# appended to tmp/server-loop.log. On any failure, a final marker line
# "Last step failed, remove this file to restart the loop." is appended to
# tmp/server-loop.log, and the script waits for either a keypress (in the
# loop terminal) or removal of tmp/server-loop.log to restart. A clean
# server exit (stop-style termination) also restarts the loop.
#
# All loop log files (tmp/server-loop.log + tmp/server-loop-<step>.log + the
# DevLog) are wiped at the start of every iteration.

[CmdletBinding()]
param(
    [Alias('c')]
    [string]$Configuration = "Release",
    # Anything after the named params is forwarded to `dotnet run` AFTER
    # the `--` separator — i.e. it goes to App.Server.exe as command-line
    # arguments. Example:
    #   pwsh -NoProfile -File server-loop.ps1 -c Debug --foo --bar=baz
    # → `dotnet run -c Debug ... -- --foo --bar=baz`
    [Parameter(ValueFromRemainingArguments = $true)]
    [string[]]$ServerArgs = @()
)

$ErrorActionPreference = "Continue"
$ScriptDir = $PSScriptRoot
Set-Location $ScriptDir

$tmpDir = Join-Path $ScriptDir "tmp"
if (-not (Test-Path $tmpDir)) {
    New-Item -ItemType Directory -Path $tmpDir | Out-Null
}

$loopLog         = Join-Path $tmpDir "server-loop.log"
$npmBuildLog     = Join-Path $tmpDir "server-loop-npm-build.log"
$dotnetBuildLog  = Join-Path $tmpDir "server-loop-dotnet-build.log"
# Server-run produces three sibling files — one per stream, distinguished by
# extension. The DevLog (.log) is the richest source: it carries the full
# ActualChat structured diagnostics across all categories, not just whatever
# made it to stdout.
$serverRunBase   = Join-Path $tmpDir "server-loop-server-run"
$serverRunOutLog = "$serverRunBase.out"  # dotnet run stdout
$serverRunErrLog = "$serverRunBase.err"  # dotnet run stderr
$devLog          = "$serverRunBase.log"  # ActualChat_DevLog
$projectCsproj   = "src/dotnet/App.Server/App.Server.csproj"

$allLoopLogs = @($loopLog, $npmBuildLog, $dotnetBuildLog, $serverRunOutLog, $serverRunErrLog, $devLog)

function Write-LoopLog([string]$Text) {
    $ts = Get-Date -Format "HH:mm:ss"
    $line = "[$ts] $Text"
    Write-Host $line
    Add-Content -Path $loopLog -Value $line -Encoding UTF8
}

function Reset-LoopLogs {
    foreach ($f in $allLoopLogs) {
        Remove-Item $f -Force -ErrorAction SilentlyContinue
    }
}

function Wait-ForRestart {
    Write-LoopLog "Last step failed, remove this file to restart the loop."
    Write-Host "Press any key in this terminal OR delete '$loopLog' to restart..." -ForegroundColor Yellow
    while (Test-Path $loopLog) {
        try {
            if ([System.Console]::KeyAvailable) {
                [void][System.Console]::ReadKey($true)
                break
            }
        } catch {
            # No interactive console (e.g. running detached); only file removal will break the wait.
        }
        Start-Sleep -Milliseconds 333
    }
}

# Returns the base URL for the running server. Reads .env when present so
# worktrees with a custom HostSettings__BaseUri (e.g. https://wt1.local.voxt.ai)
# stop the right instance.
function Get-BaseUri {
    $baseUri = "https://local.voxt.ai"
    $envFile = Join-Path $ScriptDir ".env"
    if (Test-Path $envFile) {
        Get-Content $envFile | ForEach-Object {
            if ($_ -match '^HostSettings__BaseUri=(.+)$') {
                $baseUri = $Matches[1].Trim().TrimEnd('/')
            }
        }
    }
    return $baseUri
}

# Sends a clean stop request to the running server. The 's'/'x' keys in the
# child's keyboard watcher don't reach it because we redirect stdout/stderr to
# capture logs (which detaches the inherited console on Windows), so we forward
# keypresses from the loop terminal via this HTTP path instead.
#
# 502 / 503 / connection-refused responses mean the upstream is already
# down (server has stopped or is mid-restart). That's the desired
# end-state, so we treat them as success and keep the log line quiet.
# Watchdog helpers: external liveness check used during Step 3 in case the
# in-process /health/stop hard-exit watchdog can never arm because the
# listener itself is unresponsive (NGINX 502, direct port refused).
#
# The .NET process publishes its bound port via a single log line:
#   "[ActualChat.Mesh.MeshWatcher] [+] Endpoint lock acquired: localhost:<port>"
# The line is stable across one dotnet run; the .log file is wiped at the
# start of every loop iteration, so the next process writes a fresh line.
function Get-ServerPortFromDevLog {
    if (-not (Test-Path $devLog)) { return $null }
    try {
        $line = Select-String -Path $devLog `
            -Pattern '\[ActualChat\.Mesh\.MeshWatcher\] \[\+\] Endpoint lock acquired: localhost:(\d+)' `
            -ErrorAction SilentlyContinue |
            Select-Object -Last 1
        if ($line -and $line.Matches.Count -gt 0) {
            return [int]$line.Matches[0].Groups[1].Value
        }
    } catch {
        # Log is currently being written; transient I/O errors are fine.
    }
    return $null
}

function Test-ServerProbe([int]$Port) {
    $url = "http://localhost:$Port/healthz/live"
    try {
        $resp = Invoke-WebRequest -Uri $url -TimeoutSec 5 -UseBasicParsing -ErrorAction Stop
        return ([int]$resp.StatusCode -ge 200) -and ([int]$resp.StatusCode -lt 400)
    } catch {
        return $false
    }
}

function Start-ServerProcess {
    param(
        [Parameter(Mandatory)] [string]   $Configuration,
        [Parameter(Mandatory)] [string]   $ProjectCsproj,
        [Parameter(Mandatory)] [AllowEmptyCollection()] [string[]] $ServerArgs,
        [Parameter(Mandatory)] [string]   $StdoutPath,
        [Parameter(Mandatory)] [string]   $StderrPath
    )
    if ($IsWindows) {
        $runArgs = @('run', '-c', $Configuration, '--no-launch-profile', '--project', $ProjectCsproj, '--') + $ServerArgs
        return Start-Process -FilePath dotnet `
            -ArgumentList $runArgs `
            -RedirectStandardOutput $StdoutPath -RedirectStandardError $StderrPath `
            -NoNewWindow -PassThru
    }
    # POSIX: Start-Process with -RedirectStandardOutput/-RedirectStandardError
    # attaches an AsyncStreamReader to the child's stdio. On force-kill
    # (Stop-Process -Force / watchdog kill) the StreamWriter backing the
    # redirect file is disposed while the reader still has buffered lines;
    # the next FlushMessageQueue throws ObjectDisposedException on a
    # threadpool thread, escapes $ErrorActionPreference, and aborts the
    # host (the "Cannot write to a closed TextWriter" / "Abort trap: 6"
    # symptom). Workaround: launch via System.Diagnostics.Process directly
    # with no PowerShell-owned readers, and have /bin/sh do the redirection
    # then `exec` dotnet so we still get the dotnet PID. (Start-Process
    # -NoNewWindow without -Redirect* on macOS turned out to detach the
    # child early; the direct API does not.)
    $quotedArgs = ($ServerArgs | ForEach-Object { "'$(($_ -replace "'", "'\''"))'" }) -join ' '
    $shellLine = "exec dotnet run -c '$Configuration' --no-launch-profile --project '$ProjectCsproj' -- $quotedArgs > '$StdoutPath' 2> '$StderrPath'"
    $psi = [System.Diagnostics.ProcessStartInfo]::new()
    $psi.FileName = '/bin/sh'
    $psi.ArgumentList.Add('-c')
    $psi.ArgumentList.Add($shellLine)
    $psi.UseShellExecute = $false
    $psi.RedirectStandardInput = $false
    $psi.RedirectStandardOutput = $false
    $psi.RedirectStandardError = $false
    return [System.Diagnostics.Process]::Start($psi)
}

function Send-StopSignal {
    $url = "$(Get-BaseUri)/health/stop"
    Write-LoopLog "Forwarding stop request to $url..."
    try {
        Invoke-WebRequest -Uri $url -Method Get -TimeoutSec 5 -SkipCertificateCheck -UseBasicParsing | Out-Null
    } catch {
        $resp = $_.Exception.Response
        $status = $null
        if ($resp -is [System.Net.Http.HttpResponseMessage]) { $status = [int]$resp.StatusCode }
        elseif ($resp -is [System.Net.HttpWebResponse]) { $status = [int]$resp.StatusCode }
        # 502/503 from nginx + raw socket errors all mean "upstream gone" —
        # which is what stop is trying to achieve. Don't yell about it.
        $upstreamGone = ($status -in 502, 503, 504) -or ($_.Exception -is [System.Net.WebException] -and $resp -eq $null)
        if ($upstreamGone) {
            Write-LoopLog "Stop request: upstream already down (treating as success)."
        } else {
            Write-LoopLog "Stop request failed: $_"
        }
    }
}

# One-time banner: listing log paths here keeps per-iteration output terse.
Write-Host "server-loop log files (wiped at the start of every iteration):"
Write-Host "  loop          $loopLog"
Write-Host "  npm-build     $npmBuildLog"
Write-Host "  dotnet-build  $dotnetBuildLog"
Write-Host "  server-run    $serverRunOutLog (stdout)"
Write-Host "                $serverRunErrLog (stderr)"
Write-Host "                $devLog (DevLog)"
Write-Host ""

while ($true) {
    Reset-LoopLogs
    $failureMessage = $null

    # Step 1: npm-build
    Write-LoopLog "Step 1/3 (npm-build)"
    # On Windows `npm` is a `.cmd` wrapper. PowerShell's `&` + `*>` does not
    # reliably propagate exit codes or capture all output from .cmd files, so
    # tsc errors silently dropped the loop into a "passed" state. Run via
    # `cmd /c` and redirect inside cmd to capture both streams faithfully.
    if ($IsWindows) {
        & cmd /c "npm run build:Debug > `"$npmBuildLog`" 2>&1"
    } else {
        & npm run build:Debug *> $npmBuildLog
    }
    if ($LASTEXITCODE -ne 0) {
        $failureMessage = "Step 1 (npm-build) failed with exit code $LASTEXITCODE."
    }

    # Step 2: dotnet-build
    if (-not $failureMessage) {
        Write-LoopLog "Step 2/3 (dotnet-build)"
        & dotnet build -c $Configuration $projectCsproj *> $dotnetBuildLog
        if ($LASTEXITCODE -ne 0) {
            $failureMessage = "Step 2 (dotnet-build) failed with exit code $LASTEXITCODE."
        }
    }

    # Step 3: server-run
    if (-not $failureMessage) {
        Write-LoopLog "Step 3/3 (server-run)"
        $env:ActualChat_DevLog = $devLog
        $env:ASPNETCORE_ENVIRONMENT = "Development"
        # Predefined TOTPs for phone sign-in (test numbers +1 555-555-5550..5555).
        # Lookup key is digits-only normalized phone; value is the 6-digit code.
        $env:UsersSettings__PredefinedTotps__15555555550 = "111111"
        $env:UsersSettings__PredefinedTotps__15555555551 = "111111"
        $env:UsersSettings__PredefinedTotps__15555555552 = "111111"
        $env:UsersSettings__PredefinedTotps__15555555553 = "111111"
        $env:UsersSettings__PredefinedTotps__15555555554 = "111111"
        $env:UsersSettings__PredefinedTotps__15555555555 = "111111"
        # Run dotnet asynchronously so this PowerShell can poll its own stdin
        # while the server is up. Otherwise `*>` redirection makes 's'/'x' keys
        # invisible to the child's Console.ReadKey watcher (the child no longer
        # has a TTY for stdin on Windows). On 's'/'x' from the loop terminal we
        # forward a /health/stop instead — which triggers the same clean
        # IHostApplicationLifetime.StopApplication path the child would have.
        # Stdout and stderr go to separate sibling files so output streams stay
        # uninterleaved — the .err file is empty on a healthy run.
        # Args after `--` are forwarded to App.Server.exe. The loop owns
        # keypress handling itself (forwards 's'/'x' / any-key to
        # /health/stop), so the App.Server-side `-kb` keyboard watcher
        # would have nothing to read — its stdin is captured by the
        # redirect anyway. We don't pass `-kb`. `$ServerArgs` appends
        # whatever the operator passed to server-loop.ps1.
        $proc = Start-ServerProcess `
            -Configuration  $Configuration `
            -ProjectCsproj  $projectCsproj `
            -ServerArgs     $ServerArgs `
            -StdoutPath     $serverRunOutLog `
            -StderrPath     $serverRunErrLog
        Write-Host "Keyboard: any key = stop the server (forwarded to /health/stop; force-kill after 30s)." -ForegroundColor Cyan

        # Watchdog state: probe /healthz/live once we've learned the port
        # from the MeshWatcher log line. Two consecutive misses
        # -> Stop-Process -Force on $proc so the loop can recycle into a
        # fresh build. First probe lands 10s after process start (the
        # MeshWatcher line should have been written by then); subsequent
        # probes every 15s. If the port isn't in the log when a probe is
        # due, that itself counts as a missed probe.
        $watchdogPort = $null
        $watchdogMissCount = 0
        $watchdogAnnounced = $false
        $watchdogNextProbeAt = (Get-Date).AddSeconds(10)
        # Keyboard-stop force-kill deadline: set when the user presses a
        # key in the loop terminal. If $proc hasn't exited by then we
        # bypass /health/stop entirely with Stop-Process -Force.
        $keyStopForceKillAt = $null
        # Set whenever the loop knows it caused the exit (stop signal sent
        # or watchdog killed the process). On exit we treat non-zero codes
        # as "stop-style termination" instead of a start failure, since
        # FailFast / Stop-Process -Force never exit 0.
        $stopRequested = $false

        while (-not $proc.HasExited) {
            try {
                if ([System.Console]::KeyAvailable) {
                    [void][System.Console]::ReadKey($true)
                    if (-not $keyStopForceKillAt) {
                        Send-StopSignal
                        $stopRequested = $true
                        $keyStopForceKillAt = (Get-Date).AddSeconds(30)
                        Write-LoopLog "Stop signal sent; will force-kill PID $($proc.Id) if it doesn't exit within 30s."
                    }
                    # Subsequent keypresses while we wait are no-ops; the
                    # 30s deadline already covers the "stop didn't take" case.
                }
            } catch {
                # Detached / no console — fall back to passive wait.
            }

            if ($keyStopForceKillAt -and (Get-Date) -ge $keyStopForceKillAt) {
                Write-LoopLog "Keyboard-stop: graceful shutdown didn't complete within 30s; force-killing PID $($proc.Id)."
                try { Stop-Process -Id $proc.Id -Force -ErrorAction Stop }
                catch { Write-LoopLog "Keyboard-stop: Stop-Process failed: $_" }
                $stopRequested = $true
                # Clear the deadline so we don't retry on the next tick.
                # $proc.HasExited will flip after Stop-Process.
                $keyStopForceKillAt = $null
            }

            if ((Get-Date) -ge $watchdogNextProbeAt) {
                if (-not $watchdogPort) {
                    $watchdogPort = Get-ServerPortFromDevLog
                }
                $probeOk = $false
                if ($watchdogPort) {
                    if (-not $watchdogAnnounced) {
                        Write-LoopLog "Watchdog: started — probing http://localhost:$watchdogPort/healthz/live every 15s, kill after 2 misses."
                        $watchdogAnnounced = $true
                    }
                    $probeOk = Test-ServerProbe $watchdogPort
                }

                if ($probeOk) {
                    $watchdogMissCount = 0
                } else {
                    $watchdogMissCount++
                    if ($watchdogPort) {
                        Write-LoopLog "Watchdog: miss $watchdogMissCount/2 on port $watchdogPort (PID $($proc.Id))."
                    } else {
                        Write-LoopLog "Watchdog: miss $watchdogMissCount/2 — MeshWatcher port not yet in $devLog."
                    }
                    if ($watchdogMissCount -ge 2) {
                        Write-LoopLog "Watchdog: two consecutive misses; force-killing PID $($proc.Id)."
                        try { Stop-Process -Id $proc.Id -Force -ErrorAction Stop }
                        catch { Write-LoopLog "Watchdog: Stop-Process failed: $_" }
                        $stopRequested = $true
                        # $proc.HasExited will flip on the next tick.
                    }
                }
                $watchdogNextProbeAt = (Get-Date).AddSeconds(15)
            }

            Start-Sleep -Milliseconds 200
        }
        $exitCode = $proc.ExitCode
        # Did the server reach "I bound a port" before exiting? If so any
        # exit is a post-start termination (stop signal in any of its
        # forms — keyboard, /health/stop, DebugUI, watchdog, in-process
        # HardExit) and we restart. If the MeshWatcher line never
        # appeared, the process never finished startup — that's a real
        # failure and the loop should park.
        $reachedSteadyState = $stopRequested -or ($null -ne (Get-ServerPortFromDevLog))
        if ($reachedSteadyState) {
            # FailFast / Stop-Process -Force don't yield exit 0; the
            # original "exit 0 only" rule would falsely park the loop.
            Write-LoopLog "Server stopped (exit code $exitCode). Restarting from step 1..."
        } elseif ($exitCode -ne 0) {
            $failureMessage = "Step 3 (server-run) failed to start, exit code $exitCode."
        } else {
            Write-LoopLog "Server stopped. Restarting from step 1..."
        }
    }

    if ($failureMessage) {
        Write-LoopLog $failureMessage
        Wait-ForRestart
    }
}
