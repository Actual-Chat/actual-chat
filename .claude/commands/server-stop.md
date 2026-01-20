---
allowed-tools: Bash
description: Stop the ActualChat server
---

# Stop Server

Stop the ActualChat App.Server that was started with /server-start.

## Environment Detection

Check AC_OS environment variable to determine the environment:
- **Docker** (`AC_OS` = "Linux in Docker"): PID file at /tmp/server.pid, use standard kill commands
- **Host OS** (Windows, WSL, Linux, macOS): Same approach works, but on Windows native you may need different process handling

## Steps

1. Check if /tmp/server.pid exists
2. If it exists, read the PID and attempt graceful shutdown (SIGTERM)
3. Wait briefly, then verify the process stopped
4. If still running, force kill (SIGKILL)
5. Remove the PID file
6. If PID file doesn't exist, try to find and kill any App.Server processes by name
7. Verify the server has stopped

## Commands

### Linux/Docker/WSL
```bash
# Try graceful shutdown first
if [ -f /tmp/server.pid ]; then
    pid=$(cat /tmp/server.pid)
    kill "$pid" 2>/dev/null
    sleep 2
    kill -9 "$pid" 2>/dev/null
    rm -f /tmp/server.pid
fi

# If that fails or no PID file, force kill by process name
pkill -9 -f "App.Server" 2>/dev/null

# Verify stopped
pgrep -f "App.Server" || echo "Server stopped"
```

### Windows (if running from PowerShell directly)
```powershell
# Find and stop the App.Server process
Get-Process | Where-Object { $_.ProcessName -like "*App.Server*" } | Stop-Process -Force
```

## Notes

- The server may take a few seconds to fully shut down
- Log file remains at /tmp/server.log for debugging
