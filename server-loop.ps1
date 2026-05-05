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

$loopLog        = Join-Path $tmpDir "server-loop.log"
$npmBuildLog    = Join-Path $tmpDir "server-loop-npm-build.log"
$dotnetBuildLog = Join-Path $tmpDir "server-loop-dotnet-build.log"
$serverRunLog   = Join-Path $tmpDir "server-loop-server-run.log"
$devLog         = Join-Path $tmpDir "server-run.log"  # ActualChat_DevLog
$projectCsproj  = "src/dotnet/App.Server/App.Server.csproj"

$allLoopLogs = @($loopLog, $npmBuildLog, $dotnetBuildLog, $serverRunLog, $devLog)

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

while ($true) {
    Reset-LoopLogs
    $failureMessage = $null

    # Step 1: npm-build
    Write-LoopLog "Step 1/3 (npm-build): npm run build:Debug -> $npmBuildLog"
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
        $failureMessage = "Step 1 (npm-build) failed with exit code $LASTEXITCODE. See '$npmBuildLog'."
    }

    # Step 2: dotnet-build
    if (-not $failureMessage) {
        Write-LoopLog "Step 2/3 (dotnet-build): dotnet build -c $Configuration -> $dotnetBuildLog"
        & dotnet build -c $Configuration $projectCsproj *> $dotnetBuildLog
        if ($LASTEXITCODE -ne 0) {
            $failureMessage = "Step 2 (dotnet-build) failed with exit code $LASTEXITCODE. See '$dotnetBuildLog'."
        }
    }

    # Step 3: server-run
    if (-not $failureMessage) {
        Write-LoopLog "Step 3/3 (server-run): dotnet run -c $Configuration -- -kb  (stdout -> $serverRunLog, DevLog -> $devLog)"
        $env:ActualChat_DevLog = $devLog
        $env:ASPNETCORE_ENVIRONMENT = "Development"
        # Predefined TOTPs for phone sign-in (test numbers +1 555-555-5550..5555).
        # Lookup key is digits-only normalized phone; value is the 6-digit code.
        $env:UsersSettings__PredefinedTotps__15555555550 = "000000"
        $env:UsersSettings__PredefinedTotps__15555555551 = "000000"
        $env:UsersSettings__PredefinedTotps__15555555552 = "000000"
        $env:UsersSettings__PredefinedTotps__15555555553 = "000000"
        $env:UsersSettings__PredefinedTotps__15555555554 = "000000"
        $env:UsersSettings__PredefinedTotps__15555555555 = "000000"
        & dotnet run -c $Configuration --no-launch-profile --project $projectCsproj -- -kb *> $serverRunLog
        $exitCode = $LASTEXITCODE
        if ($exitCode -ne 0) {
            # Server exits non-zero only on start failure. Stop-style termination is exit 0.
            $failureMessage = "Step 3 (server-run) failed to start, exit code $exitCode. See '$serverRunLog'."
        } else {
            Write-LoopLog "Server stopped. Restarting from step 1..."
        }
    }

    if ($failureMessage) {
        Write-LoopLog $failureMessage
        Wait-ForRestart
    }
}
