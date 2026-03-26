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
    \$server = [AppServerFactory]::Create('$PROJECT_PATH')
    \$r = \$server.Restart(\$$WATCH, \$$NO_BUILD)
    if (\$r.error) {
        Write-Host \"Error: \$(\$r.error)\"
        exit 1
    }
    if (\$r.stop.stopped) { Write-Host \"Stopped (was PID: \$(\$r.stop.pid))\" }
    if (\$r.build) { Write-Host \"Build: exit code \$(\$r.build.exitCode)\" }
    \$start = \$r.start
    if (\$start.started) {
        Write-Host \"Started (PID: \$(\$start.pid)), port: \$(\$start.port)\"
        if (\$start.ready) { Write-Host \"Server ready! (\$(\$start.readyTime)s)\" }
    } else {
        Write-Host \$start.message
    }
    \$s = \$server.GetStatus()
    \$host_ = if (\$s.instance -eq 'dev') { 'local.voxt.ai' } else { \"\$(\$s.instance).local.voxt.ai\" }
    Write-Host \"Browser: https://\$host_\"
"
```
