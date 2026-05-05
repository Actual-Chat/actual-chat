---
allowed-tools: Bash, Read
description: Inspect a running server-loop.cmd/.ps1 — show current stage, last step output, server reachability
---

# /server-loop

Use this when the user has started `server-loop.cmd` (or `server-loop.ps1`)
in another terminal and wants to know where it is, why it stalled, or
whether the server is reachable.

**Do NOT use `/server-start`, `/server-restart`, or `/server-stop` while
`server-loop` is running** — the loop owns the dotnet process and will
fight you. To restart the running .NET server, use one of:

- `s` keypress in the loop terminal (only works on the host running the loop)
- `curl -X POST https://local.voxt.ai/health/stop`
- `debugUI.stopServer()` from the browser console

A clean stop makes the loop rebuild and relaunch automatically. A
**failed start** (e.g. port in use, DB unreachable) parks the loop —
the marker line `Last step failed, remove this file to restart the loop.`
is the last line of `tmp/server-loop.log`. To unstick, delete that file
or press a key in the loop terminal.

## Cross-environment caveat

You may be running in Docker/WSL while `server-loop` runs on the host
OS. In that case you cannot send a keypress to the loop terminal.
Observation through log files still works (`tmp/` is shared) — and the
HTTP `/health/stop` and `debugUI.stopServer()` paths are reachable from
any environment that can hit `https://local.voxt.ai`.

## Sign-in for tests (phone + TOTP)

On a local-dev server with no Twilio/SMSTo configured, phone sign-in
uses `LogOnlyTextMessageSender`: instead of an SMS, the TOTP is
written to the server log as a warning like
`!!! Text message to +15550001234: Your code is 482917`.

**Two ways in:**

1. **Any test number, e.g. `+1 555 000 1234`** — request the code, then
   grep the server log for the digits and type them into the UI.
   Two log files have it (the writes are duplicated by the logging
   pipeline):
   - `tmp/server-run.log` (server's `ActualChat_DevLog`)
   - `tmp/server-loop-server-run.log` (loop's stdout capture)

   ```bash
   grep -E "Text message to" tmp/server-run.log | tail -5
   ```

2. **Predefined numbers `+1 555 555 5550..5555`** — `server-loop.ps1`
   exports `UsersSettings__PredefinedTotps__<digits>` env vars. No SMS
   is sent (and no log line is produced). All six numbers accept
   TOTP `000000`.

The predefined codes are wired up only when `server-loop` is the one
launching the server — `/server-start` and friends don't set them.

## Chrome on the host (likely already running)

When the user is running `server-loop`, they're almost certainly also
running `c.ps1 chrome` (or `c chrome`) on the host — that opens Chrome
with remote debugging on port `9222`, pointing at the local site.

**Prefer the `chrome-devtools` MCP for any browser-side inspection or
control** (console logs, network, screenshots, DOM snapshots, script
evaluation). Use it to:

- Read console errors after editing — much faster than reproducing them
  in a new Playwright session.
- Trigger a server stop without leaving Claude: evaluate
  `debugUI.stopServer()` in the page context. The loop will rebuild
  and relaunch automatically.
- Verify a fix landed: take a screenshot or read the DOM after the
  loop reports `Step 3/3 (server-run): ...`.

If `chrome-devtools` MCP isn't available, fall back to Playwright with
`chromium.connectOverCDP('http://localhost:9222')` — same Chrome, same
session, the user sees what you do.

## Where to look

| File | Purpose |
|------|---------|
| `tmp/server-loop.log` | Stage transitions + failure marker (the "what's happening" view) |
| `tmp/server-loop-npm-build.log` | Step 1 stdout/stderr (npm run build:Debug) |
| `tmp/server-loop-dotnet-build.log` | Step 2 stdout/stderr (dotnet build) |
| `tmp/server-loop-server-run.log` | Step 3 stdout/stderr (dotnet run -- -kb) |
| `tmp/server-run.log` | Server's `ActualChat_DevLog` (structured app diagnostics) |

All five files are wiped at the start of each loop iteration. Their
absence means either (a) `server-loop` is not running, or (b) it's
between iterations and hasn't reached step 1 yet (rare/brief).

## Reachability — two URLs to know

- **`https://local.voxt.ai`** — the URL you (and the user) should hit.
  This is NGINX, fronting the .NET server with TLS + the proper Host
  header. All app behavior that depends on `BaseUrlKind.Local` (e.g.
  `/health/stop`, `debugUI.stopServer()`) requires this hostname.
- **`http://localhost:7080`** — the .NET backend that NGINX proxies to.
  Useful as a low-level liveness probe, but the app will reject some
  endpoints when reached this way because the Host header isn't local.

If `.env` exists in the project root (worktrees normally have one), it
overrides the defaults:

| `.env` key | What it sets | Default |
|-----------|--------------|---------|
| `urls=...:<port>` *or* `ASPNETCORE_URLS=...:<port>` | Backend port | `7080` |
| `HostSettings__BaseUri=https://<prefix>.local.voxt.ai` | Browser URL (`<prefix>` is e.g. a worktree name like `wt1`) | `https://local.voxt.ai` |
| `CoreSettings__Instance=<name>` | Instance name (used elsewhere) | `dev` |

So a worktree's `.env` might point to `https://wt1.local.voxt.ai` on
port `7081`. Pick those up before probing.

## Bash implementation

```bash
#!/bin/bash
PROJECT_PATH="${AC_ProjectPath:-$(pwd)}"

pwsh -NoProfile -c "
    \$envFile = Join-Path '$PROJECT_PATH' '.env'
    \$port = 7080
    \$baseUri = 'https://local.voxt.ai'
    if (Test-Path \$envFile) {
        Get-Content \$envFile | ForEach-Object {
            if (\$_ -match '^(?:urls|ASPNETCORE_URLS)=.*?:(\d+)\s*$') { \$port = [int]\$Matches[1] }
            if (\$_ -match '^HostSettings__BaseUri=(.+)$') { \$baseUri = \$Matches[1].Trim() }
        }
    }

    \$tmp = Join-Path '$PROJECT_PATH' 'tmp'
    \$loopLog = Join-Path \$tmp 'server-loop.log'
    \$logs = @{
        'npm-build'    = Join-Path \$tmp 'server-loop-npm-build.log'
        'dotnet-build' = Join-Path \$tmp 'server-loop-dotnet-build.log'
        'server-run'   = Join-Path \$tmp 'server-loop-server-run.log'
    }

    Write-Host '=== server-loop state ==='
    if (-not (Test-Path \$loopLog)) {
        Write-Host \"tmp/server-loop.log: NOT FOUND.\"
        Write-Host '  -> server-loop is either not running, or between iterations.'
    } else {
        Write-Host \"--- tail \$loopLog ---\"
        Get-Content \$loopLog -Tail 20 | ForEach-Object { Write-Host \"  \$_\" }
        \$last = (Get-Content \$loopLog -Tail 1)
        if (\$last -match 'remove this file to restart the loop') {
            Write-Host ''
            Write-Host 'PAUSED: server-loop is waiting for restart.' -ForegroundColor Yellow
            Write-Host '  -> delete tmp/server-loop.log, or press a key in the loop terminal.'
        }
    }

    Write-Host ''
    Write-Host '=== step log sizes ==='
    foreach (\$k in 'npm-build','dotnet-build','server-run') {
        \$f = \$logs[\$k]
        if (Test-Path \$f) {
            \$len = (Get-Item \$f).Length
            Write-Host (\"  {0,-13} {1,10} bytes  {2}\" -f \$k, \$len, \$f)
        } else {
            Write-Host (\"  {0,-13}   (absent)\" -f \$k)
        }
    }

    Write-Host ''
    Write-Host '=== reachability ==='
    foreach (\$entry in @(@{label='backend  '; url=\"http://localhost:\$port\"}, @{label='nginx    '; url=\$baseUri})) {
        try {
            \$resp = Invoke-WebRequest -Uri \$entry.url -TimeoutSec 3 -UseBasicParsing -ErrorAction Stop
            Write-Host (\"  {0} {1}  [HTTP {2}]\" -f \$entry.label, \$entry.url, \$resp.StatusCode)
        } catch {
            \$msg = \$_.Exception.Message
            if (\$msg.Length -gt 80) { \$msg = \$msg.Substring(0, 77) + '...' }
            Write-Host (\"  {0} {1}  DOWN ({2})\" -f \$entry.label, \$entry.url, \$msg) -ForegroundColor Red
        }
    }
"
```

## Interpreting the result

- **Loop log shows `Step 3/3 (server-run): ...` as the latest line and
  both URLs are up:** healthy run state. Edit code, then trigger a
  stop (one of the three methods above) — the loop rebuilds.
- **Loop log ends with `Last step failed, remove this file to restart`:**
  open the corresponding `server-loop-<step>.log` for the actual error.
  Fix the code, then unstick the loop (delete the loop log file).
- **NGINX URL down but backend up:** check that NGINX is running on the
  host (it lives outside the loop). The `/health/stop` and
  `debugUI.stopServer()` paths require NGINX.
- **Both URLs down while loop is on step 3:** the server crashed between
  startup and now. `server-loop-server-run.log` and `tmp/server-run.log`
  have the details.
- **No loop log at all:** the user didn't actually start `server-loop`,
  or they started it from a different working directory. Ask before
  guessing.
