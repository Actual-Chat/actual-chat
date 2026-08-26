---
allowed-tools: Bash, Read
description: Inspect a running server-loop.cmd/.ps1 — show current stage, last step output, server reachability
---

# /server-loop

Use this when the user has started `server-loop.cmd` (or `server-loop.ps1`)
in another terminal and wants to know where it is, why it stalled, or
whether the server is reachable.

For browser-side interaction (sign-in flows, debugUI helpers, multi-Chrome
setup), see `/debug-ui` — `server-loop` and the chrome-devtools MCP rig
are typically running together.

**Do NOT use `/server-start`, `/server-restart`, or `/server-stop` while
`server-loop` is running** — the loop owns the dotnet process and will
fight you. To restart the running .NET server, use one of:

- `s` or `x` keypress in the loop terminal — in fact *any* key except
  `j`, which is reserved for the rebundle below. The loop forwards it to
  `/health/stop`.
- `curl https://local.voxt.ai/health/stop` (it's a **GET**, not POST —
  POST returns 400 from antiforgery).
- `debugUI.stopServer()` from the browser console.

A clean stop makes the loop rebuild and relaunch automatically. A
**failed start** (port in use, DB unreachable, etc.) parks the loop —
the marker line `Last step failed, remove this file to restart the loop.`
is the last line of `tmp/server-loop.log`. To unstick, delete that file
or press a key in the loop terminal.

To rebuild **only the TS/CSS bundle** without stopping anything, press `j`
in the loop terminal or `touch tmp/server-loop-rebundle` — see "Rebundle
without restarting the server" below for what it does and does not get you.

### The loop rebuild is the PREFERRED way to check everything

When the loop is running, validate TS / shared-code changes by triggering a
loop rebuild and watching its logs — **not** by running `tsc`/`eslint`/
`npm run build:Verify` yourself. The loop runs **natively on the host**, while
Claude runs in Docker, and the difference is large:

- `tsc --noEmit` alone: **~1 min in Docker** (where Claude runs).
- The same check via the loop / natively: **~5–10s**.

So: edit code → trigger a restart → watch `tmp/server-loop-npm-build.log` (TS
type/build errors) and `tmp/server-loop.log` (stage transitions). Don't burn a
minute on an in-Docker `tsc`.

### DON'T manually run `npm run build:Debug` while the loop is running

Step 1 of the loop already runs `npm run build:Debug` on every iteration.
If you `npm run build:Debug` first AND THEN trigger a restart, you build
twice — and the second one (the loop's) is the one whose output the .NET
host actually picks up via the bundle-fingerprint emitted at startup.

Workflow when iterating on TS / shared code with the loop running:
1. Edit code.
2. Trigger a restart — `debugUI.stopServer()`, `curl /health/stop`, OR
   `rm tmp/server-loop.log` if the loop parked at "Last step failed".
3. Wait for `Step 3/3 (server-run)` + the watchdog's
   `Watchdog: started — probing http://localhost:7080/healthz/live …`
   line in `tmp/server-loop.log` and a 200 from
   `https://local.voxt.ai/`.
4. Reload Chrome (hard-reload if WASM mode — see `/debug-ui`).

That's it. ~30 seconds and the bundle the page loads is fresh. Manual
`npm run build:Debug` is appropriate ONLY when the loop isn't running
(or when you're hunting a build error in isolation).

### Hard restart — when the browser is stuck on stale WASM

**The failure this exists for:** the browser holds cached assemblies that no
longer match the ones the server serves. The page reload-loops on an assembly
mismatch, the console shows mismatch/load failures, and **nothing you can do
from inside that tab recovers it** — reloading is what's already looping. An
agent driving the page is simply stuck, and the run is over.

Two triggers, same as the rebundle pair:

- **Press `h` in the loop terminal.**
- **`touch tmp/server-loop-hard-restart`** — for anyone who can't reach that
  terminal (Claude in Docker, another shell). `tmp/` is shared across host, WSL
  and Docker.

The loop stops the server, then — once the process has released the files —
purges `artifacts/{obj,bin}/App.Wasm` and drops both build stamps, so Steps 1
and 2 both run instead of being skipped as up to date. Watch for:

```
[hh:mm:ss] Hard restart requested: stopping the server, then purging the WASM build outputs.
[hh:mm:ss] Hard restart: purged …/artifacts/obj/App.Wasm.
[hh:mm:ss] Hard restart: stamps dropped, so npm-build and dotnet-build both run below.
```

Dropping the stamps alone is not enough — MSBuild considers the stale outputs
up to date and skips them, which is exactly how the mismatch survives an
ordinary restart. The purge is scoped to the WASM app deliberately: it is the
only output the browser can be poisoned by, and wiping all of `artifacts/`
turns a ~1 minute recovery into a very long one.

::: tip Prefer render mode `'s'` and you will rarely need this
The stale-assembly trap only exists in WASM. `await debugUI.setRenderMode('s')`
once at the start of a session makes reloads cheap (no runtime download, no
service-worker dance) **and** removes this whole failure class. Switch to `'w'`
at the end to confirm the WASM build, not while iterating — see `/debug-ui`.
:::

### Rebundle without restarting the server

Two triggers, same code path (`Invoke-Rebundle` in `server-loop.ps1`):

- **Press `j` in the loop terminal.** For the developer, who has it
  focused. It is the one key the loop does *not* treat as "stop the
  server".
- **`touch tmp/server-loop-rebundle`** (or, in pwsh,
  `New-Item -ItemType File tmp/server-loop-rebundle -Force`). For everyone
  who can't reach that terminal — Claude in Docker, another shell, a
  script. `tmp/` is shared across host / WSL / Docker.

Either way, while the server is up: the loop's 200ms poll picks the request
up, runs `npm run build:Debug` inline, and **keeps the same server process**
— no stop, no `dotnet build`, no startup wait. Watch `tmp/server-loop.log`
for:

```
[hh:mm:ss] Rebundle: npm run build:Debug (server keeps running)...
[hh:mm:ss] Rebundle: done in 8.2s.
[hh:mm:ss] Rebundle: the server serves the new bundle — reload the page with caching disabled …
```

TS/lint errors land in `tmp/server-loop-npm-build.log` and produce a
`Rebundle: FAILED (exit code N) …` line. A failed rebundle does **not** park
the loop and does **not** touch the server — fix the TS and request another
one. Requesting a rebundle while one is running is fine; the sentinel is
deleted before the build starts, so the second request queues another pass.
A `j` pressed while a keyboard stop is already in flight is declined with
`Rebundle: ignored — a server stop is already in flight.`

**This is a TYPE-CHECK fast path first and a hot-swap second.** The server
keeps running, and it does serve the new bundle — but the *browser* still has
to be told to re-fetch it:

- The page URL is `/dist/bundle.<hash>.js`, fingerprinted at `dotnet build`
  time and served `Cache-Control: immutable`. It does not change on a
  rebundle, so a plain reload always serves the cached copy — you need a
  cache-bypassing reload (see `/debug-ui`).
- If the .NET static-asset manifest and the new bundle disagree on size, the
  loop logs `Rebundle: WARNING — the server still advertises N bytes …`.
  That means the manifest predates the bundle; restart for a full rebuild.
- `Rebundle: WARNING — the server still serves a precompressed /dist copy …`
  means `-p:DisableBuildCompression=true` didn't take (see below) — most
  likely the running server was launched by an older `server-loop.ps1`.
  Restart the loop.

Either way this is still the cheapest way to type-check TS from Docker
(~8s vs ~1min for an in-Docker `tsc --noEmit`).

### Why the loop builds with `-p:DisableBuildCompression=true`

Both Step 2 (`dotnet build`) and Step 3 (`dotnet run`) get
`-p:DisableBuildCompression=true`, in every configuration.

Without it, build-time static-asset compression writes `.gz` copies under
`artifacts/obj/App.Wasm/<config>/compressed/` and MapStaticAssets hands those
to any browser sending `Accept-Encoding: gzip`. Only `dotnet build`
regenerates them, so a rebundle — which runs npm and nothing else — would be
**invisible** to the browser while looking like it worked.
`Directory.Build.props` sets `CompressionEnabled=false` globally, but
`App.Wasm.csproj` re-enables it for `Release` (the loop's default `-c`); a
command-line global property overrides both.

Two notes for anyone touching this:

- It must be on **both** steps. Global properties are part of a project's
  build identity, so `dotnet build` with it and `dotnet run` without it are
  two different builds — MSBuild would redo the whole thing in Step 3.
- It disables *build* compression only. `dotnet publish` still precompresses,
  which is what real deployments want, so this doesn't affect release builds.

### Steps 1 and 2 are skipped when nothing changed

The loop stamps `tmp/server-loop-npm-build.stamp` and
`tmp/server-loop-dotnet-build.<config>.stamp` before each step succeeds and
skips the step when nothing it consumes is newer:

- **npm-build** consumes `src/nodejs`, `src/dotnet` (minus `.cs`/`.csproj`/
  MSBuild files — but `.razor`/`.cshtml` count, Tailwind scans them),
  `resources/sounds/converted`, and the root build config. So a **C#-only**
  change skips it.
- **dotnet-build** consumes all of `src` + `lib` + the root MSBuild files —
  including `App.Wasm/wwwroot/dist`, because the bundle is what
  MapStaticAssets fingerprints. So it is skipped only when *nothing* changed.
  This costs nothing in safety: Step 3's `dotnet run` builds anyway; skipping
  Step 2 only avoids doing the same build twice.

Log lines read `Step 1/3 (npm-build) — skipped, no bundle input changed …`.
If you ever suspect a stale skip, `rm tmp/server-loop-*.stamp` forces a full
cycle. Detection is mtime-based, so a tool that *preserves* mtimes when
writing (archive extraction, a restored backup) can hide a change — normal
edits from any editor, git, Claude, or Docker bind mounts bump mtimes fine.

## Cross-environment caveat

You may be running in Docker/WSL while `server-loop` runs on the host
OS. In that case you cannot send a keypress to the loop terminal.
Observation through log files still works (`tmp/` is shared) — and the
HTTP `/health/stop` and `debugUI.stopServer()` paths are reachable from
any environment that can hit `https://local.voxt.ai`.

## Sign-in for tests

Two paths, in increasing convenience:

1. **`debugUI.signIn(phoneOrEmail)` from the browser console** — the
   fastest. Local-dev-only; auto-uses the dev-bypass TOTP `111111`. See
   `/debug-ui` for the full details and option object.

2. **The modal flow with predefined phones** — `+1 555 555 5550..5555`
   accept TOTP `111111`. `server-loop.ps1` exports
   `UsersSettings__PredefinedTotps__<digits>` env vars; no SMS is sent.
   The predefined codes are wired up only when `server-loop` is the one
   launching the server — `/server-start` and friends don't set them.

3. **Any other test number** (e.g. `+1 555 000 1234`) — request the
   code, then grep the server log for the digits and type them into the
   UI:

   ```bash
   grep -E "Text message to" tmp/server-loop-server-run.log | tail -5
   ```

   The TOTP is also written to the DevLog (`tmp/server-loop-server-run.log`,
   yes the *same* base name; see file table below).

## Chrome on the host

When the user is running `server-loop`, they're almost certainly also
running Chrome with remote debugging — most likely launched via
`ai chrome` (one instance, default profile, port 9222) or `ai chrome*2`
(two anonymous-profile instances, ports 9222/9223 — what you want for
multi-user tests).

**Prefer the `chrome-devtools` MCP for any browser-side work.** Two MCP
services are wired up in `docker-compose.yml`
(`chrome-devtools-mcp-1` → `localhost:8765` → Chrome :9222 and
`chrome-devtools-mcp-2` → `localhost:8766` → Chrome :9223). Tools land
in Claude as `mcp__chrome1__*` / `mcp__chrome2__*` (the older
`mcp__chrome-devtools-{1,2}__*` names still resolve too).
Setup details and usage live in `/debug-ui`.

## Where to look

| File | Purpose |
|------|---------|
| `tmp/server-loop.log` | Stage transitions + failure marker (the "what's happening" view) |
| `tmp/server-loop-npm-build.log` | Step 1 stdout/stderr (npm run build:Debug) |
| `tmp/server-loop-dotnet-build.log` | Step 2 stdout/stderr (dotnet build) |
| `tmp/server-loop-server-run.out` | Step 3 — `dotnet run` stdout |
| `tmp/server-loop-server-run.err` | Step 3 — `dotnet run` stderr (empty on a healthy run) |
| `tmp/server-loop-server-run.log` | Server's `ActualChat_DevLog` — the structured app diagnostics, richer than stdout |
| `tmp/server-loop-rebundle` | Write it to request an in-place rebundle (same as pressing `j` in the loop terminal); the loop deletes it when it starts building |
| `tmp/server-loop-hard-restart` | Write it to request a hard restart — stop, purge `artifacts/{obj,bin}/App.Wasm`, drop both stamps, full rebuild (same as pressing `h`). For when the browser is reload-looping on stale WASM assemblies |
| `tmp/server-loop-npm-build.stamp` | "npm-build last succeeded at" (UTC ticks) — drives the skip check |
| `tmp/server-loop-dotnet-build.<config>.stamp` | Same for dotnet-build, per `-c` configuration |

All six log files are wiped at the start of each loop iteration; the
`.stamp` files deliberately survive it. The loop
banner-prints these paths once at startup and never again, so per-step
log lines stay terse — `[hh:mm:ss] Step N/3 (name)`.

The `.log` (DevLog) is usually the one you want; the `.out` is mostly
ASP.NET startup banner + bootstrap warnings, and `.err` is empty unless
the process actually crashed.

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
port `7081`. Pick those up before probing. The loop reads the same
`.env` to address the right server when forwarding keypresses to
`/health/stop`.

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
        'npm-build'      = Join-Path \$tmp 'server-loop-npm-build.log'
        'dotnet-build'   = Join-Path \$tmp 'server-loop-dotnet-build.log'
        'server-run.out' = Join-Path \$tmp 'server-loop-server-run.out'
        'server-run.err' = Join-Path \$tmp 'server-loop-server-run.err'
        'server-run.log' = Join-Path \$tmp 'server-loop-server-run.log'
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

    \$rebundleFlag = Join-Path \$tmp 'server-loop-rebundle'
    if (Test-Path \$rebundleFlag) {
        Write-Host ''
        Write-Host 'Rebundle requested, not yet picked up (tmp/server-loop-rebundle still exists).'
        Write-Host '  -> the loop deletes it when it starts the npm build; if it lingers, the loop is not on step 3.'
    }

    Write-Host ''
    Write-Host '=== step log sizes ==='
    foreach (\$k in 'npm-build','dotnet-build','server-run.out','server-run.err','server-run.log') {
        \$f = \$logs[\$k]
        if (Test-Path \$f) {
            \$len = (Get-Item \$f).Length
            Write-Host (\"  {0,-15} {1,10} bytes  {2}\" -f \$k, \$len, \$f)
        } else {
            Write-Host (\"  {0,-15}   (absent)\" -f \$k)
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

- **Loop log shows `Step 3/3 (server-run)` as the latest line and both
  URLs are up:** healthy run state. Edit code, then trigger a stop (any
  of the three methods above) — the loop rebuilds.
- **Loop log ends with `Last step failed, remove this file to restart`:**
  open the corresponding `server-loop-<step>.log` (or `.out`/`.err`) for
  the actual error. Fix the code, then unstick the loop (delete the
  loop log file).
- **NGINX URL down but backend up:** check that NGINX is running on the
  host (it lives outside the loop, in `docker-compose.yml`). The
  `/health/stop` and `debugUI.stopServer()` paths require NGINX.
- **Both URLs down while loop is on step 3:** the server crashed between
  startup and now. `server-loop-server-run.err` and
  `server-loop-server-run.log` (DevLog) have the details.
- **No loop log at all:** the user didn't actually start `server-loop`,
  or they started it from a different working directory (worktrees!).
  Ask before guessing.

## Picking up script edits

`server-loop.ps1` is loaded once when the loop starts — PowerShell does
not hot-reload. After editing the script (renaming log files, changing
env vars, etc.), the host needs to **restart the loop** for the changes
to take effect. C# / TypeScript changes are picked up by the next
rebuild and don't need a loop restart.

## After a server restart, hard-reload the browser when WASM is in play

A plain reload is enough when render mode is `'s'` (Server). When the
page is in `'w'` (WASM) or `'a'` (Auto, which upgrades to WASM), you
need a hard reload with caches cleared — otherwise the SW keeps
serving the stale hashed `bundle.<hash>.js` and the cached
`_framework` payload, and your fix won't appear. See the **"Hard-reload
after WASM-affecting changes"** section in `/debug-ui` for the
one-liner and the recommended workflow ("`'s'` while iterating, switch
to `'w'` at the end to confirm").
