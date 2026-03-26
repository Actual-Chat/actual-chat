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
    \$server = [AppServerFactory]::Create('$PROJECT_PATH')
    \$r = \$server.GetStatus()
    Write-Host \"Instance: \$(\$r.instance)\"
    Write-Host \"Port:     \$(\$r.port)\"
    Write-Host \"Status:   \$(\$r.status)\"
    if (\$r.pid) { Write-Host \"PID:      \$(\$r.pid)\" }
"
```
