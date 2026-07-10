---
name: "source-command-server-port-check"
description: "Find orphan processes holding the server port"
---

# source-command-server-port-check

Use this skill when the user asks to run the migrated source command `server-port-check`.

## Command Template

# Port Check

Diagnose port conflicts by finding what process holds the server port.
Automatically kills any process found on the port.
Works inside Docker (detects host-side orphans too) and on the host directly.

## Options

| Option | Description |
|--------|-------------|
| (none) | Check the port from .env |
| `<port>` | Check a specific port number |
| `--all` | Check all registered worktree ports |

## Bash Implementation

```bash
#!/bin/bash
PROJECT_PATH="${AC_ProjectPath:-$(pwd)}"

ARGS=(-ProjectPath "$PROJECT_PATH")
for arg in "$@"; do
    case "$arg" in
        --all) ARGS+=(-CheckAll) ;;
        *[0-9]*) ARGS+=(-CustomPort "$arg") ;;
    esac
done

pwsh -NoProfile -File "$PROJECT_PATH/scripts/ServerPorts.ps1" "${ARGS[@]}"
```
