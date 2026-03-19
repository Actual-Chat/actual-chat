---
allowed-tools: Bash
description: Show status of all running ActualChat servers
---

# Server Status

Show status of all worktree servers by scanning `.env` files.

## Bash Implementation

```bash
#!/bin/bash

PROJECT_PATH="${AC_ProjectPath:-$(pwd)}"
PARENT_DIR="$(dirname "$PROJECT_PATH")"

# Determine if using shared /tmp (Docker) or per-worktree tmp
if [ "$AC_OS" = "Linux in Docker" ]; then
    USE_SHARED_TMP=true
else
    USE_SHARED_TMP=false
fi

echo ""
printf "%-20s %-20s %-6s %-8s %s\n" "Worktree" "Instance" "Port" "PID" "Status"
printf '%.0s-' {1..65}
echo ""

for env_file in "$PARENT_DIR"/ActualChat*/.env; do
    [ -f "$env_file" ] || continue

    wt_path="$(dirname "$env_file")"
    wt_name="$(basename "$wt_path")"

    # Extract instance and port from .env
    inst=$(grep -E '^CoreSettings__Instance=' "$env_file" | cut -d= -f2)
    port=$(grep -E '^urls=' "$env_file" | grep -oE '[0-9]+$')

    [ -z "$inst" ] && continue

    # Determine tmp directory for this worktree
    if [ "$USE_SHARED_TMP" = true ]; then
        tmp_dir="/tmp"
    else
        tmp_dir="$wt_path/tmp"
    fi

    pid_file="$tmp_dir/server-$inst.pid"

    # Display worktree name
    if [ "$wt_name" = "ActualChat" ]; then
        display_wt="(main)"
    else
        display_wt="${wt_name#ActualChat-}"
    fi

    pid="-"
    status="Stopped"
    if [ -f "$pid_file" ]; then
        pid=$(cat "$pid_file" | tr -d '[:space:]')
        if kill -0 "$pid" 2>/dev/null; then
            status="Running"
        else
            status="Stale"
        fi
    fi

    printf "%-20s %-20s %-6s %-8s %s\n" "$display_wt" "$inst" "$port" "$pid" "$status"
done
echo ""
```
