---
allowed-tools: Bash
description: Stop the ActualChat server
---

# Stop Server

Stop the ActualChat App.Server for current worktree or all worktrees.

## Options

| Option | Description |
|--------|-------------|
| (none) | Stop current worktree's server |
| `--all` | Stop all servers |

## Bash Implementation

```bash
#!/bin/bash

PROJECT_PATH="${AC_ProjectPath:-$(pwd)}"
STOP_ALL="${1:-}"

# Load configuration from .env file
if [ -f "$PROJECT_PATH/.env" ]; then
    set -a
    source "$PROJECT_PATH/.env"
    set +a
fi

INSTANCE="${CoreSettings__Instance:-dev}"

# Determine tmp directory
if [ "$AC_OS" = "Linux in Docker" ]; then
    TMP_DIR="/tmp"
else
    TMP_DIR="$PROJECT_PATH/tmp"
fi

stop_server() {
    local inst="$1"
    local tmp_dir="$2"
    local pid_file="$tmp_dir/server-$inst.pid"

    if [ -f "$pid_file" ]; then
        local pid=$(cat "$pid_file" | tr -d '[:space:]')
        echo "Stopping $inst (PID: $pid)..."
        kill "$pid" 2>/dev/null || true
        sleep 2
        kill -9 "$pid" 2>/dev/null || true
        rm -f "$pid_file"
        echo "Stopped"
    else
        echo "No PID file for $inst"
    fi
}

if [ "$STOP_ALL" = "--all" ]; then
    # Stop all servers by scanning .env files
    PARENT_DIR="$(dirname "$PROJECT_PATH")"

    for env_file in "$PARENT_DIR"/ActualChat*/.env; do
        [ -f "$env_file" ] || continue

        wt_path="$(dirname "$env_file")"
        inst=$(grep -E '^CoreSettings__Instance=' "$env_file" | cut -d= -f2)

        [ -z "$inst" ] && continue

        # Determine tmp directory for this worktree
        if [ "$AC_OS" = "Linux in Docker" ]; then
            tmp_dir="/tmp"
        else
            tmp_dir="$wt_path/tmp"
        fi

        stop_server "$inst" "$tmp_dir"
    done
    pkill -9 -f "App.Server" 2>/dev/null || true
    echo "All servers stopped"
else
    stop_server "$INSTANCE" "$TMP_DIR"
fi
```
