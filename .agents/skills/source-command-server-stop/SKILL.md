---
name: "source-command-server-stop"
description: "Stop the ActualChat server"
---

# source-command-server-stop

Use this skill when the user asks to run the migrated source command `server-stop`.

## Command Template

# Stop Server

Stop the ActualChat App.Server for current worktree.

## Bash Implementation

```bash
#!/bin/bash
PROJECT_PATH="${AC_ProjectPath:-$(pwd)}"

pwsh -NoProfile -c "
    . '$PROJECT_PATH/scripts/Common.ps1'

    \$envFile = Join-Path '$PROJECT_PATH' '.env'
    \$instance = 'dev'; \$port = 7080
    if (Test-Path \$envFile) {
        Get-Content \$envFile | ForEach-Object {
            if (\$_ -match '^urls=.*?(\d+)$') { \$port = [int]\$Matches[1] }
            if (\$_ -match '^CoreSettings__Instance=(.+)$') { \$instance = \$Matches[1] }
        }
    }

    \$pidFile = Join-Path '$PROJECT_PATH' 'tmp' \"server-\$instance.pid\"
    \$proc = \$null

    # Try PID file first
    if (Test-Path \$pidFile) {
        try {
            \$savedPid = [int](Get-Content \$pidFile -Raw).Trim()
            \$proc = [System.Diagnostics.Process]::GetProcessById(\$savedPid)
            if (\$proc.HasExited) { \$proc = \$null }
        } catch { \$proc = \$null }
    }

    # Fallback: find process listening on port
    if (-not \$proc) {
        \$lsof = bash -c \"lsof -i :\$port 2>/dev/null | grep LISTEN | head -1\" 2>\$null
        if (\$lsof) {
            try {
                \$foundPid = [int]((\$lsof -split '\s+')[1])
                \$proc = [System.Diagnostics.Process]::GetProcessById(\$foundPid)
                if (\$proc.HasExited) { \$proc = \$null }
            } catch { \$proc = \$null }
        }
    }

    if (-not \$proc) {
        Write-Host 'No running server found'
        exit 0
    }

    \$procId = \$proc.Id
    Write-Host \"Stopping server \$instance (PID: \$procId)...\"

    # Kill process tree
    function Kill-Tree([int]\$pid) {
        \$children = bash -c \"pgrep -P \$pid 2>/dev/null\" 2>\$null
        if (\$children) {
            foreach (\$c in (\$children -split \"\`n\" | Where-Object { \$_ })) {
                Kill-Tree([int]\$c)
            }
        }
        try {
            \$p = [System.Diagnostics.Process]::GetProcessById(\$pid)
            if (-not \$p.HasExited) { \$p.Kill(); \$p.WaitForExit(3000) | Out-Null }
        } catch {}
    }
    Kill-Tree \$procId
    Remove-Item \$pidFile -ErrorAction SilentlyContinue
    Write-Host \"Stopped (was PID: \$procId)\"
"
```
