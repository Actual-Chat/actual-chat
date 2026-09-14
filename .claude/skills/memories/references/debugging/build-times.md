Typical local timings for the server-loop steps:

- **npm build (`build:Debug`)**: ~5s
- **dotnet build (`App.Server.csproj`)**: 15s – 1.5min (depends on what changed; cold/large changes hit the upper end)
- **server startup (warm `dotnet run`)**: ~5s

**Why:** User-confirmed measurements; sized for the local dev box.

**How to apply:** When awaiting a `/health/stop` → rebuild → restart cycle, a 30–60s timeout is usually enough; allow up to ~2min if a substantial dotnet change is in flight. Avoid 5-minute "just in case" timeouts — they slow iteration.

**Validate via the loop, NOT in-Docker tsc/eslint (user-insisted, recurring).** Claude runs in Docker where `tsc --noEmit` alone takes ~1 min; the loop runs natively and does the same check in ~5–10s. So when the loop is running, check TS/shared changes by triggering a rebuild and watching `tmp/server-loop-npm-build.log` + `tmp/server-loop.log` — don't run `npx tsc`/`eslint`/`build:Verify` yourself. (Documented in `.claude/commands/server-loop.md` too.)
