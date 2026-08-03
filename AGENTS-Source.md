# Project-specific Rules for Voxt (ActualChat)

**YOU MUST READ [docs/CODING_STYLE.md](docs/CODING_STYLE.md) before writing or
modifying any C# or TypeScript code.** It's not optional. This project
**deviates from standard .NET conventions** on several points (notably:
no `Async` suffix on async methods; no XML docs on members; mixed brace
style). Default instincts from elsewhere will produce code that gets
rejected. If you haven't opened that file yet in this session, stop and
read it now.

**You MUST NOT write a single comment, docstring, or XML doc** — in C#, in
TypeScript, anywhere — without first reading
[docs/CODING_STYLE.md → "Regular comments, docstrings, XML documentation
comments"](docs/CODING_STYLE.md#regular-comments-docstrings-xml-documentation-comments).
You have a strong tendency to over-comment and to restate what the code
already says; that section explains exactly when a comment is justified and
when it isn't. Re-read it any time you're tempted to add a `//`, `///`, or
JSDoc block.

A style hook checks every `.cs`/`.ts`/`.razor`/`.css` edit against
[docs/CODING_STYLE.md](docs/CODING_STYLE.md). Everything it reports is fixed by
default, including violations outside the lines you changed. If the user
explicitly decides to keep offending code as-is, record it in
[.claude/style-bypasses.md](.claude/style-bypasses.md), in the format described
there. That file is the only thing the hook skips; without an entry there the
same violation is reported again on the next edit to that file.

# Type Catalog — Reuse Existing Abstractions (CRITICAL)

This codebase is large and mature. **Reusing what already exists is far more
important than writing something new.** A new helper that duplicates an
existing one is a defect, not a feature: it splinters the codebase, drifts
out of sync, and makes future changes harder. **Always look first.**

**Indexes** (read these before writing or planning new code):
- [`docs/api-index.md`](docs/api-index.md) — condensed, curated overview of
  the most important .NET types, organized by project.
- [`docs/api-index-full.md`](docs/api-index-full.md) — complete .NET type list.
- [`docs/api-index-ts.md`](docs/api-index-ts.md) — TypeScript exports across
  `src/nodejs/` and `src/dotnet/UI.Blazor*/`.

## Planning rule (mandatory)

**Every implementation plan MUST include a "Reuse" section** with two parts:

1. **Existing abstractions to reuse.** Research first. List the concrete
   types/functions you intend to call from the indexes above (or from the
   sibling `ActualLab.Fusion` project). If you cannot find a fit, say so
   explicitly — silence is not acceptable.

2. **Reusability of new components.** For every new component the plan
   introduces, ask: *is this likely useful elsewhere?* If yes, the plan
   **must list an option to put it in a shared project** instead of the
   feature-specific one:
   - **C#**: `ActualChat.Core` (no server/UI deps) or
     `ActualChat.Core.Server` (server-side, no UI deps).
   - **TypeScript**: `src/nodejs/src/` (under `actuallab-core`,
     `actuallab-rpc`, or a shared subfolder), not buried inside a single
     component's folder.

   The plan should compare the local-vs-shared placement and recommend
   one. Default to shared when in doubt — promoting later is harder than
   placing correctly the first time.

If the work is small enough that you skip a written plan, you still owe
yourself the "look first" step: search the indexes for keywords related
to what you're about to write.

## Architecture Docs

Consult `docs/live-video/` for the live-video pipeline (capture, encoding,
simulcast, RPC fan-out, playback, quality control, A/V sync) and
`docs/live-audio/` for the live-audio pipeline (mic capture, VAD, Opus,
publish/persist/transcribe, fan-out, replay, playback). Both are written
from current source.

## TypeScript Validation

When modifying TypeScript files under `src/nodejs/` or `src/dotnet/UI.Blazor.App/`, always validate changes by running:

```bash
npm run build:Verify
```

This runs `tsc --noEmit`, `eslint`, and the debug build. It catches unused variables, type errors, and lint violations that `tsc --noEmit` alone may miss.

The only exception is when `/server-loop` is running - in this case you should trigger a rebuild there and watch for the build errors. This option is significantly faster than `npm run build:Verify`, because it runs on OS, not in Docker, and moreover, `/server-loop` use implies you'll anyway end up triggering it after successful `npm run build`.

## Infrastructure services and the running server

**Infrastructure services**: When running in Docker, assume that all services defined in `docker-compose.yml` (PostgreSQL, Redis, NATS, nginx, etc.) are already running on the host. Do not attempt to start them yourself - they are managed externally and accessible from the container.

**Running integration tests**: Tests detect Claude's Docker environment via `AC_OS="Linux in Docker"` and use regular localhost-based configuration (not `testsettings.docker.json`). This works because `--network host` makes localhost = host.

**Running the server (Docker watch mode)**: The host runs `./run-watch.cmd` — it auto-rebuilds and restarts the server when you change files. After editing code, poll `tmp/watch-dotnet.log` until you see `Now listening on:` (ready) or `error` (fix and wait again). Do not use `/server-start` or `/server-restart` — the watch process owns the server. Frontend build output: `tmp/watch-web.log`.

**Running the server (direct)**: Use `/server-start`, `/server-restart`, `/server-stop`. Use `--watch` flag for auto-reload.
