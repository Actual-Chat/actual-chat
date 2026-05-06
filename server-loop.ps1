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
    [string]$Configuration = "Release"
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
function Send-StopSignal {
    $url = "$(Get-BaseUri)/health/stop"
    Write-LoopLog "Forwarding stop request to $url..."
    try {
        Invoke-WebRequest -Uri $url -Method Get -TimeoutSec 5 -SkipCertificateCheck -UseBasicParsing | Out-Null
    } catch {
        Write-LoopLog "Stop request failed: $_"
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
        $proc = Start-Process -FilePath dotnet `
            -ArgumentList @('run', '-c', $Configuration, '--no-launch-profile', '--project', $projectCsproj, '--', '-kb') `
            -RedirectStandardOutput $serverRunOutLog -RedirectStandardError $serverRunErrLog `
            -NoNewWindow -PassThru
        Write-Host "Keyboard: 's' or 'x' = stop the server (forwarded to /health/stop)." -ForegroundColor Cyan

        while (-not $proc.HasExited) {
            try {
                if ([System.Console]::KeyAvailable) {
                    $ch = [System.Console]::ReadKey($true).KeyChar
                    if ($ch -in 's','S','x','X') { Send-StopSignal }
                }
            } catch {
                # Detached / no console — fall back to passive wait.
            }
            Start-Sleep -Milliseconds 200
        }
        $exitCode = $proc.ExitCode
        if ($exitCode -ne 0) {
            # Server exits non-zero only on start failure. Stop-style termination is exit 0.
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
