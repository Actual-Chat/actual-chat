# Plan: Run E2E & Unit TS Tests from CI and Locally

**Issue:** #3756
**Branch:** `feat/3756-run-e2e-test-from-ci`

## Current State

- **3 E2E tests** in `tests/ts/e2e/`: `signin-and-message`, `mention-search`, `svg-avatar-upload`
- **7 unit tests** in `tests/ts/unit/`: pure logic, no browser/server needed
- **Framework:** Vitest + Playwright
- **Browser:** All E2E tests connect to host Chrome via CDP (`localhost:9222`). Only `svg-avatar-upload` has a fallback to launch headless Chromium; the other two hard-require CDP.
- **Server:** All E2E tests assume the server is already running at `BASE_URL` (from `.env` or default `https://local.voxt.ai`)
- **Massive duplication:** Each E2E test file copy-pastes ~80 lines of identical helpers (`loadBaseUrl`, `dismissCookieConsent`, `skipOnboarding`, `isSignedIn`, `signIn`, `screenshot`, config constants)
- **CI:** GitHub Actions runs .NET unit/integration tests only. No TS tests run in CI at all.

## Execution Environments

Tests must work across these environments:

| # | Environment | How to run | Server | Browser | BaseURL |
|---|-------------|-----------|--------|---------|---------|
| 1 | **Windows host** | `npm run test:e2e` | Already running via `run-watch.cmd` or direct | Host Chrome (`c chrome`) on `localhost:9222` | From `.env` — `https://{instance}.local.voxt.ai` via nginx |
| 2 | **macOS host** | `npm run test:e2e` | Already running via `run-watch.cmd` or direct | Host Chrome (`c chrome`) on `localhost:9222` | From `.env` — `https://{instance}.local.voxt.ai` via nginx |
| 3 | **Docker container** (`c fwt`) | `npm run test:e2e` | Running on host (watch mode), accessible via `--network host` | Host Chrome on `localhost:9222` or `host.docker.internal:9222` (macOS IPv6 quirk), or headless fallback | From `.env` — same as host, nginx accessible via localhost |
| 4 | **GitHub Actions CI** | `npm run test:e2e` | **Managed** — started by test harness | Headless Chromium (no display) | `http://localhost:{port}` — direct to Kestrel, no nginx |

### Key platform differences

- **Docker `--network host`**: `localhost` inside the container IS the host. Nginx on port 443, Kestrel on port from `.env` (e.g., 7120), Chrome debug on 9222 — all reachable via localhost.
- **macOS Docker quirk**: `localhost` can resolve to `::1` (IPv6) while Chrome binds IPv4 only. Current code uses `192.168.65.254` (`host.docker.internal` IP). The helper should try localhost first, then fall back to `host.docker.internal` resolved IP.
- **Worktree subdomains**: Each worktree gets its own subdomain (`3756-run-e2e-test-fr.local.voxt.ai`) and port (7120). The `.env` file in each worktree has the correct `HostSettings__BaseUri`. Tests must read this, not hardcode `local.voxt.ai`.
- **CI has no nginx**: Kestrel runs directly on HTTP. No SSL certs, no domain routing.
- **Self-signed certs**: Local dev uses self-signed certs via nginx. Headless Playwright needs `ignoreHTTPSErrors: true`.

### Health endpoints (already exist)

- `/healthz/live` — liveness (always 200 if process is up)
- `/healthz/ready` — readiness (checks CPU load)

## Goals

1. Run TS unit tests locally (all platforms) and in CI
2. Run TS E2E tests in CI (headless Chromium, managed server)
3. Run TS E2E tests locally against host Chrome (`c chrome`) OR headless
4. Run TS E2E locally with server assumed running OR auto-started
5. Eliminate the copy-paste duplication across E2E tests

## Design Decisions

### D1: Browser mode — env var `AC_E2E_BROWSER`

| Value | Behavior |
|-------|----------|
| _(unset)_ / `auto` | Try CDP connect (with platform-aware host resolution), fall back to headless |
| `cdp` | Force CDP — fail if Chrome isn't running |
| `headless` | Force headless Chromium — never tries CDP |

**CDP host resolution order** (in `auto`/`cdp` mode):
1. `localhost:9222`
2. If in Docker and localhost fails: resolve `host.docker.internal` IP → try that
3. If both fail and mode is `auto`: launch headless

This handles Windows host, macOS host, Linux Docker, and macOS Docker (IPv6 quirk) uniformly.

### D2: Server lifecycle — env var `AC_E2E_SERVER`

| Value | Behavior |
|-------|----------|
| _(unset)_ / `external` | Assume server is already running at `BASE_URL` |
| `managed` | Start the server before tests, stop after |

When `AC_E2E_SERVER=managed`:
- Start: `dotnet run --project src/dotnet/App.Server` as a background process with env vars:
  - `ASPNETCORE_URLS=http://+:{port}` (HTTP only, no cert needed)
  - `ASPNETCORE_ENVIRONMENT=Development`
  - `HostSettings__BaseUri=http://localhost:{port}`
- Wait: poll `/healthz/live` until 200 or timeout (60s)
- Stop: kill the process tree in `globalTeardown`

When `AC_E2E_SERVER=external`:
- Verify server is reachable at `BASE_URL` (single fetch to `/healthz/live`)
- Warn (don't fail) if unreachable — test failures will speak for themselves

**CI default**: `AC_E2E_SERVER=managed` set in workflow env.
**Local default**: `external` (developer runs their own server).

### D3: Vitest config split

| Config | Include | Purpose |
|--------|---------|---------|
| `vitest.config.ts` | `tests/ts/unit/**/*.test.ts` | Unit tests only (fast, no deps) |
| `vitest.config.e2e.ts` | `tests/ts/e2e/**/*.test.ts` + `globalSetup` | E2E tests (needs browser + server) |

**npm scripts:**
```json
{
  "test": "vitest run",
  "test:unit": "vitest run --config vitest.config.ts",
  "test:e2e": "vitest run --config vitest.config.e2e.ts",
  "test:all": "vitest run --config vitest.config.ts && vitest run --config vitest.config.e2e.ts"
}
```

`npm run test` (bare) keeps using the default `vitest.config.ts` = unit tests only. Safe to run anywhere without setup.

### D4: Extract shared E2E helpers

Create `tests/ts/e2e/helpers.ts`:
- **Config**: `loadBaseUrl()`, `TEST_EMAIL`, `TEST_OTP`
- **Browser**: `connectBrowser()` — platform-aware CDP/headless (respects `AC_E2E_BROWSER`, `AC_OS`)
- **Page helpers**: `dismissCookieConsent()`, `skipOnboarding()`, `isSignedIn()`, `signIn()`, `screenshot()`

Each test file shrinks from ~200+ lines to ~100 lines of actual test logic.

### D5: Self-signed cert handling

- **CDP mode** (connecting to host Chrome): Chrome already trusts the cert (user installed it). No action needed.
- **Headless mode locally**: Playwright launches with `ignoreHTTPSErrors: true` in the browser context. Works for `https://*.local.voxt.ai`.
- **Headless mode in CI**: Server runs on HTTP directly. No cert issues at all.

## Implementation Plan

### Step 1: Extract shared helpers

Create `tests/ts/e2e/helpers.ts` with all shared code. Refactor each E2E test to import from it. **No behavior change yet** — just dedup. Keep the same CDP-first behavior.

### Step 2: Generalize browser connection

Update `connectBrowser()` in helpers:
- Read `AC_E2E_BROWSER` env var
- Implement platform-aware CDP host detection (localhost → host.docker.internal fallback)
- `auto` mode: try CDP with 3s timeout, fall back to headless
- Headless mode: launch with `ignoreHTTPSErrors: true`, `--no-sandbox` (for CI)

### Step 3: Create vitest E2E config + global setup

**`vitest.config.e2e.ts`**: E2E-specific config pointing to `tests/ts/e2e/global-setup.ts`.

**`tests/ts/e2e/global-setup.ts`**:
```typescript
// globalSetup runs once before all test files
export async function setup() {
    if (process.env.AC_E2E_SERVER === 'managed') {
        // spawn dotnet run, poll /healthz/live, store PID
    } else {
        // optional: verify BASE_URL is reachable, warn if not
    }
}
export async function teardown() {
    // kill managed server if started
}
```

### Step 4: Update `vitest.config.ts` + `package.json`

- Narrow `vitest.config.ts` include to unit tests only
- Add new npm scripts

### Step 5: Add CI workflow jobs

Add to `build-test-deploy-dev.yml`:

**TS Unit Tests** — lightweight, no services:
```yaml
ts-unit-tests:
  name: TS Unit Tests
  runs-on: ubuntu-latest
  needs: [check-tested]
  if: needs.check-tested.outputs.skip-tests != 'true'
  steps:
    - uses: actions/checkout@v4
    - uses: actions/setup-node@v4
      with: { node-version: 20 }
    - run: npm ci
    - run: npm run test:unit
```

**TS E2E Tests** — needs services, managed server, headless browser:
```yaml
ts-e2e-tests:
  name: TS E2E Tests
  runs-on: ubuntu-latest
  needs: [check-tested]
  if: needs.check-tested.outputs.skip-tests != 'true'
  services:
    redis: { ... }      # same as _run-tests.yml
    postgres: { ... }
    nats: { ... }
  env:
    AC_E2E_BROWSER: headless
    AC_E2E_SERVER: managed
  steps:
    - uses: actions/checkout@v4
      with: { fetch-depth: 0, lfs: true }
    - uses: actions/setup-node@v4
      with: { node-version: 20 }
    - uses: ./.github/actions/setup-dotnet
    - run: npm ci
    - run: npx playwright install chromium --with-deps
    - run: npm run test:e2e
    - uses: actions/upload-artifact@v4
      if: always()
      with:
        name: e2e-screenshots
        path: tmp/*.png
        if-no-files-found: ignore
```

The managed server global-setup handles building + starting the server with:
- `ASPNETCORE_URLS=http://+:5000`
- `HostSettings__BaseUri=http://localhost:5000`
- Direct HTTP to Kestrel — no nginx, no certs

## File Changes Summary

| File | Action |
|------|--------|
| `tests/ts/e2e/helpers.ts` | **New** — shared helpers extracted from all 3 test files |
| `tests/ts/e2e/global-setup.ts` | **New** — managed server lifecycle for vitest globalSetup |
| `tests/ts/e2e/signin-and-message.test.ts` | **Edit** — import helpers, remove duplicated code |
| `tests/ts/e2e/mention-search.test.ts` | **Edit** — import helpers, remove duplicated code |
| `tests/ts/e2e/svg-avatar-upload.test.ts` | **Edit** — import helpers, remove duplicated code |
| `vitest.config.ts` | **Edit** — unit tests only |
| `vitest.config.e2e.ts` | **New** — E2E config with globalSetup |
| `package.json` | **Edit** — add `test:unit`, `test:e2e`, `test:all` scripts |
| `.github/workflows/build-test-deploy-dev.yml` | **Edit** — add `ts-unit-tests` and `ts-e2e-tests` jobs |

## Environment Variables Reference

| Variable | Values | Default | Set by |
|----------|--------|---------|--------|
| `AC_E2E_BROWSER` | `auto`, `cdp`, `headless` | `auto` | Developer or CI workflow |
| `AC_E2E_SERVER` | `external`, `managed` | `external` | Developer or CI workflow |
| `AC_OS` | `Linux in Docker`, etc. | _(unset if not via launcher)_ | `c.ps1` launcher |
| `HostSettings__BaseUri` | URL | from `.env` or `https://local.voxt.ai` | `.env` / CI env |

## Running — by environment

### Windows/macOS host (server + Chrome already running)
```bash
npm run test:unit                    # unit tests
npm run test:e2e                     # e2e → auto-detects CDP, uses server from .env
```

### Docker container via `c fwt` (server on host via watch mode)
```bash
npm run test:unit                    # unit tests
npm run test:e2e                     # e2e → auto-detects CDP to host Chrome, uses server from .env
AC_E2E_BROWSER=headless npm run test:e2e  # skip host Chrome, use headless
```

### Locally with auto-started server (any platform)
```bash
AC_E2E_SERVER=managed npm run test:e2e               # headless auto-detected (no Chrome)
AC_E2E_SERVER=managed AC_E2E_BROWSER=headless npm run test:e2e  # explicit headless
```

### GitHub Actions CI (automatic)
```bash
# Set by workflow env:
# AC_E2E_BROWSER=headless  AC_E2E_SERVER=managed
npm run test:e2e
```

### Single test file
```bash
npx vitest run tests/ts/e2e/signin-and-message.test.ts --config vitest.config.e2e.ts
```

## Open Questions

1. **Test database seeding**: E2E tests depend on specific accounts and chats (`test-claude-agent@actual.chat`, `/chat/the-actual-one`). In CI with a fresh DB, these won't exist. Does the sign-in flow auto-register when account isn't found (the code handles "Account not found" → "Register a new account")? Does OTP `111111` work as a test bypass? If so, sign-in is handled. But the chat `/chat/the-actual-one` needs to exist — do we need a DB seed step?

2. **Parallelism**: Should E2E test files run sequentially or in parallel? Currently within a `describe` block tests are sequential and share a page. Across files they could run in parallel with separate browser contexts. Sequential is safer to start with.

3. **Managed server build time**: In CI, building `App.Server` may take significant time. Should the E2E job depend on a prior build job and reuse artifacts, or build independently? Building independently is simpler but slower.
