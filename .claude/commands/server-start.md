---
allowed-tools: Bash
description: Start the ActualChat server in background
---

# Start Server

Start the ActualChat App.Server in the background with multi-worktree support.

## Options

| Option | Description |
|--------|-------------|
| (none) | Start with `dotnet run` |
| `--watch` | Start with `dotnet watch run` for auto-reload on C# changes |

## Bash Implementation

```bash
#!/bin/bash
set -e

PROJECT_PATH="${AC_ProjectPath:-$(pwd)}"
WATCH="false"
[ "${1:-}" = "--watch" ] && WATCH="true"

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

    # Check if already running
    \$lsof = bash -c \"lsof -i :\$port 2>/dev/null | grep LISTEN | head -1\" 2>\$null
    if (\$lsof) {
        \$pid = [int]((\$lsof -split '\s+')[1])
        Write-Host \"Already running (PID: \$pid, port: \$port)\"
        Write-Host \"Browser: \$baseUri\"
        exit 0
    }

    \$env:ActualChat_CaptchaBypassEnabled = 'true'
    \$env:ASPNETCORE_ENVIRONMENT = 'Development'

    \$serverProject = Join-Path '$PROJECT_PATH' 'src' 'dotnet' 'App.Server' 'App.Server.csproj'
    \$tmpDir = Join-Path '$PROJECT_PATH' 'tmp'
    if (-not (Test-Path \$tmpDir)) { New-Item -ItemType Directory -Path \$tmpDir -Force | Out-Null }
    \$logFile = Join-Path \$tmpDir \"server-\$instance.log\"
    \$errFile = \"\$logFile.err\"
    \$pidFile = Join-Path \$tmpDir \"server-\$instance.pid\"

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
    \$proc = [System.Diagnostics.Process]::Start(\$psi)
    Set-Content \$pidFile \$proc.Id

    \$logFs = [System.IO.FileStream]::new(\$logFile, [System.IO.FileMode]::Create, [System.IO.FileAccess]::Write, [System.IO.FileShare]::Read)
    \$errFs = [System.IO.FileStream]::new(\$errFile, [System.IO.FileMode]::Create, [System.IO.FileAccess]::Write, [System.IO.FileShare]::Read)
    \$null = \$proc.StandardOutput.BaseStream.CopyToAsync(\$logFs).ContinueWith(
        [System.Action[System.Threading.Tasks.Task]]{ param(\$t) \$logFs.Dispose() })
    \$null = \$proc.StandardError.BaseStream.CopyToAsync(\$errFs).ContinueWith(
        [System.Action[System.Threading.Tasks.Task]]{ param(\$t) \$errFs.Dispose() })

    # Health check
    \$ready = \$false
    for (\$i = 1; \$i -le 90; \$i++) {
        Start-Sleep -Seconds 1
        if (\$proc.HasExited) { Write-Host 'Server process exited unexpectedly'; break }
        try {
            \$null = Invoke-WebRequest -Uri \"http://localhost:\$port\" -TimeoutSec 2 -ErrorAction Stop
            \$ready = \$true
            Write-Host \"Server ready! (\${i}s)\"
            break
        } catch {}
    }

    Write-Host \"Started (PID: \$(\$proc.Id)), port: \$port\"
    if (\$watch) { Write-Host 'Mode: dotnet watch (auto-reload)' }
    if (-not \$ready) { Write-Host 'Warning: Server may not be ready yet' }
    Write-Host \"Browser: \$baseUri\"
"
```
