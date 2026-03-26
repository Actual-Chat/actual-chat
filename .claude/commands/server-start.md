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

## Configuration

Uses `AppServerFactory.Connect()` from `scripts/Common.ps1` to auto-detect whether to control the server locally or via the watch-agent (macOS/Windows Docker).

## Bash Implementation

```bash
#!/bin/bash
set -e

PROJECT_PATH="${AC_ProjectPath:-$(pwd)}"
WATCH="false"
[ "${1:-}" = "--watch" ] && WATCH="true"

pwsh -NoProfile -c "
    . '$PROJECT_PATH/scripts/Common.ps1'
    \$server = [AppServerFactory]::Create('$PROJECT_PATH')
    \$r = \$server.Start(\$$WATCH)
    if (\$r.started) {
        Write-Host \"Started (PID: \$(\$r.pid)), port: \$(\$r.port)\"
        if (\$r.watch) { Write-Host 'Mode: dotnet watch (auto-reload)' }
        if (\$r.ready) { Write-Host \"Server ready! (\$(\$r.readyTime)s)\" }
        else { Write-Host 'Warning: Server may not be ready yet' }
    } else {
        Write-Host \$r.message
    }
    \$s = \$server.GetStatus()
    \$host_ = if (\$s.instance -eq 'dev') { 'local.voxt.ai' } else { \"\$(\$s.instance).local.voxt.ai\" }
    Write-Host \"Browser: https://\$host_\"
"
```
