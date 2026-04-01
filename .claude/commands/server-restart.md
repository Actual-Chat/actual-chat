---
allowed-tools: Bash
description: Restart the ActualChat server (stop, build, start, health check)
---

# Restart Server

Stop the current server, rebuild, start it again, and wait until it's ready.

## Options

| Option | Description |
|--------|-------------|
| (none) | Restart with `dotnet run` |
| `--watch` | Restart with `dotnet watch run` for auto-reload on C# changes |
| `--no-build` | Skip the build step (just stop and start) |

## Bash Implementation

```bash
#!/bin/bash
set -e

PROJECT_PATH="${AC_ProjectPath:-$(pwd)}"
WATCH="false"
NO_BUILD="false"
for arg in "$@"; do
    case "$arg" in
        --watch) WATCH="true" ;;
        --no-build) NO_BUILD="true" ;;
    esac
done

pwsh -NoProfile -c "
    . '$PROJECT_PATH/scripts/Common.ps1'

    \$envFile = Join-Path '$PROJECT_PATH' '.env'
    \$instance = 'dev'; \$port = 7080; \$baseUri = 'https://local.voxt.ai'
    if (Test-Path \$envFile) {
        Get-Content \$envFile | ForEach-Object {
            if (\$_ -match '^urls=.*?(\d+)$') { \$port = [int]\$Matches[1] }
            if (\$_ -match '^CoreSettings__Instance=(.+)$') { \$instance = \$Matches[1] }
            if (\$_ -match '^HostSettings__BaseUri=(.+)$') { \$baseUri = \$Matches[1] }
        }
    }

    \$serverProject = Join-Path '$PROJECT_PATH' 'src' 'dotnet' 'App.Server' 'App.Server.csproj'
    \$tmpDir = Join-Path '$PROJECT_PATH' 'tmp'
    if (-not (Test-Path \$tmpDir)) { New-Item -ItemType Directory -Path \$tmpDir -Force | Out-Null }
    \$pidFile = Join-Path \$tmpDir \"server-\$instance.pid\"
    \$logFile = Join-Path \$tmpDir \"server-\$instance.log\"
    \$errFile = \"\$logFile.err\"

    # --- Stop ---
    function Kill-Tree([int]\$pid) {
        \$children = bash -c \"pgrep -P \$pid 2>/dev/null\" 2>\$null
        if (\$children) {
            foreach (\$c in (\$children -split \"\`n\" | Where-Object { \$_ })) { Kill-Tree([int]\$c) }
        }
        try {
            \$p = [System.Diagnostics.Process]::GetProcessById(\$pid)
            if (-not \$p.HasExited) { \$p.Kill(); \$p.WaitForExit(3000) | Out-Null }
        } catch {}
    }

    \$proc = \$null
    if (Test-Path \$pidFile) {
        try {
            \$savedPid = [int](Get-Content \$pidFile -Raw).Trim()
            \$proc = [System.Diagnostics.Process]::GetProcessById(\$savedPid)
            if (\$proc.HasExited) { \$proc = \$null }
        } catch { \$proc = \$null }
    }
    if (-not \$proc) {
        \$lsof = bash -c \"lsof -i :\$port 2>/dev/null | grep LISTEN | head -1\" 2>\$null
        if (\$lsof) {
            try { \$proc = [System.Diagnostics.Process]::GetProcessById([int]((\$lsof -split '\s+')[1])) } catch {}
        }
    }
    if (\$proc -and -not \$proc.HasExited) {
        Write-Host \"Stopping server \$instance (PID: \$(\$proc.Id))...\"
        Kill-Tree \$proc.Id
        Remove-Item \$pidFile -ErrorAction SilentlyContinue
        # Wait for port release
        for (\$i = 0; \$i -lt 20; \$i++) {
            \$check = bash -c \"lsof -i :\$port 2>/dev/null | grep LISTEN\" 2>\$null
            if (-not \$check) { break }
            Start-Sleep -Milliseconds 500
        }
        Write-Host \"Stopped (was PID: \$(\$proc.Id))\"
    } else {
        Write-Host 'No running server found'
    }

    # --- Build ---
    if (-not \$$NO_BUILD) {
        Write-Host 'Building server...'
        \$output = & dotnet build \$serverProject --verbosity quiet 2>&1
        if (\$LASTEXITCODE -ne 0) {
            Write-Host 'Build failed:'
            Write-Host (\$output | Out-String)
            exit 1
        }
        Write-Host 'Server build complete'
    }

    # --- Start ---
    \$env:ActualChat_CaptchaBypassEnabled = 'true'
    \$env:ASPNETCORE_ENVIRONMENT = 'Development'

    \$watch = \$$WATCH
    \$mode = if (\$watch) { 'watch mode' } else { 'run' }
    Write-Host \"Starting server: \$instance on port \$port (\$mode)\"

    \$dotnetArgs = if (\$watch) {
        @('watch', 'run', '--project', \$serverProject, '--no-launch-profile')
    } else {
        @('run', '--project', \$serverProject, '--no-launch-profile')
    }

    \$psi = [System.Diagnostics.ProcessStartInfo]::new('dotnet', (\$dotnetArgs -join ' '))
    \$psi.WorkingDirectory = '$PROJECT_PATH'
    \$psi.UseShellExecute = \$false
    \$psi.RedirectStandardOutput = \$true
    \$psi.RedirectStandardError = \$true
    \$newProc = [System.Diagnostics.Process]::Start(\$psi)
    Set-Content \$pidFile \$newProc.Id

    \$logFs = [System.IO.FileStream]::new(\$logFile, [System.IO.FileMode]::Create, [System.IO.FileAccess]::Write, [System.IO.FileShare]::Read)
    \$errFs = [System.IO.FileStream]::new(\$errFile, [System.IO.FileMode]::Create, [System.IO.FileAccess]::Write, [System.IO.FileShare]::Read)
    \$null = \$newProc.StandardOutput.BaseStream.CopyToAsync(\$logFs).ContinueWith(
        [System.Action[System.Threading.Tasks.Task]]{ param(\$t) \$logFs.Dispose() })
    \$null = \$newProc.StandardError.BaseStream.CopyToAsync(\$errFs).ContinueWith(
        [System.Action[System.Threading.Tasks.Task]]{ param(\$t) \$errFs.Dispose() })

    # Health check
    \$ready = \$false
    for (\$i = 1; \$i -le 90; \$i++) {
        Start-Sleep -Seconds 1
        if (\$newProc.HasExited) { Write-Host 'Server process exited unexpectedly'; break }
        try {
            \$null = Invoke-WebRequest -Uri \"http://localhost:\$port\" -TimeoutSec 2 -ErrorAction Stop
            \$ready = \$true
            Write-Host \"Server ready! (\${i}s)\"
            break
        } catch {}
    }

    Write-Host \"Started (PID: \$(\$newProc.Id)), port: \$port\"
    if (-not \$ready) { Write-Host 'Warning: Server may not be ready yet' }
    Write-Host \"Browser: \$baseUri\"
"
```
