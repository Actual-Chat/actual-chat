---
allowed-tools: Bash
description: Stop the ActualChat server
---

# Stop Server

Stop the ActualChat App.Server for current worktree.

## Bash Implementation

```bash
#!/bin/bash
PROJECT_PATH="${AC_ProjectPath:-$(pwd)}"

pwsh -NoProfile -c "
    . '$PROJECT_PATH/scripts/Common.ps1'
    \$server = [AppServerFactory]::Create('$PROJECT_PATH')
    \$r = \$server.Stop()
    if (\$r.stopped) { Write-Host \"Stopped (was PID: \$(\$r.pid))\" }
    else { Write-Host \$r.message }
"
```
