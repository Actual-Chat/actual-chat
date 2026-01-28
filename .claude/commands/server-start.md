---
allowed-tools: Bash, Read
description: Start the ActualChat server in background
---

# Start Server

Start the ActualChat App.Server in the background and verify it starts correctly.

## Environment Detection

Check AC_OS environment variable to determine the environment:
- **Docker** (`AC_OS` = "Linux in Docker"): Use `--no-launch-profile` to bypass launchSettings.json (which binds to localhost), allowing the server to bind to 0.0.0.0:7080 via ASPNETCORE_URLS env var.
- **Host OS** (Windows, WSL, Linux, macOS): Use normal `dotnet run` which uses launchSettings.json (binds to localhost:7080).

## Steps

1. Check if server is already running (check /tmp/server.pid or platform equivalent)
2. If running, report that and skip startup
3. Detect environment (Docker vs Host)
4. Start server with appropriate options:
   - Docker: `ASPNETCORE_ENVIRONMENT=Development dotnet run --project ... --no-launch-profile`
   - Host: `dotnet run --project ...`
5. Store PID in /tmp/server.pid
6. Wait for startup (may take 30-60 seconds for compilation on first run)
7. Verify with `curl -s -o /dev/null -w "%{http_code}" http://localhost:7080/` (should return 200)
8. Report success or any errors

## Verification Notes

- Use `curl http://localhost:7080/` to verify - it returns HTTP 200 when ready
- The `/health` endpoint does NOT exist (returns 404)
- Port checking tools (`ss`, `netstat`, `lsof`) may not be available in Docker
- Check `/tmp/server.log` for startup progress and errors

## Commands

### Docker (AC_OS = "Linux in Docker")
```bash
ASPNETCORE_ENVIRONMENT=Development ActualChat_CaptchaBypassEnabled=true dotnet run --project src/dotnet/App.Server/App.Server.csproj --no-launch-profile > /tmp/server.log 2>&1 &
echo $! > /tmp/server.pid
```

### Host OS (Windows, WSL, Linux, macOS)
```bash
ActualChat_CaptchaBypassEnabled=true dotnet run --project src/dotnet/App.Server/App.Server.csproj > /tmp/server.log 2>&1 &
echo $! > /tmp/server.pid
```

## Endpoints

- **Docker**: http://0.0.0.0:7080 (accessible from host via port mapping, nginx at https://local.voxt.ai)
- **Host**: http://localhost:7080 (nginx at https://local.voxt.ai)
