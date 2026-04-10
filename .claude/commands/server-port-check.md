---
allowed-tools: Bash
description: Find orphan processes holding the server port
---

# Port Check

Diagnose port conflicts by finding what process holds the server port.
Works inside Docker (detects host-side orphans too) and on the host directly.

## Options

| Option | Description |
|--------|-------------|
| (none) | Check the port from .env |
| `<port>` | Check a specific port number |
| `--kill` | Kill the process holding the port (if found and visible) |
| `--all` | Check all registered worktree ports |

## Bash Implementation

```bash
#!/bin/bash
PROJECT_PATH="${AC_ProjectPath:-$(pwd)}"

ARGS=(-ProjectPath "$PROJECT_PATH")
for arg in "$@"; do
    case "$arg" in
        --kill) ARGS+=(-Kill) ;;
        --all) ARGS+=(-CheckAll) ;;
        *[0-9]*) ARGS+=(-CustomPort "$arg") ;;
    esac
done

pwsh -NoProfile -File "$PROJECT_PATH/scripts/Server-PortCheck.ps1" "${ARGS[@]}"
```
