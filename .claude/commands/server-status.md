---
allowed-tools: Bash
description: Show status of all running ActualChat servers
---

# Server Status

Show server status for current worktree.

## Bash Implementation

```bash
#!/bin/bash
PROJECT_PATH="${AC_ProjectPath:-$(pwd)}"

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

    \$lsof = bash -c \"lsof -i :\$port 2>/dev/null | grep LISTEN | head -1\" 2>\$null
    \$pid = \$null
    \$status = 'stopped'
    if (\$lsof) {
        try { \$pid = [int]((\$lsof -split '\s+')[1]); \$status = 'running' } catch {}
    }

    Write-Host \"Instance: \$instance\"
    Write-Host \"Port:     \$port\"
    Write-Host \"Status:   \$status\"
    if (\$pid) { Write-Host \"PID:      \$pid\" }
    Write-Host \"Browser:  \$baseUri\"
"
```
