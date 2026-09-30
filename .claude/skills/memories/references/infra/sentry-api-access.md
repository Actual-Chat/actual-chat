Sentry holds **client-side** (MAUI app) errors only; server-side errors live in GCP Cloud
Logging (see [[gcp-prod-and-play-store-access]]). Token is in the `SENTRY_ACCESS_TOKEN` env
var (also wired as a stdio MCP server in `.mcp.json`, but the `mcp__sentry__*` tools are
often not connected — REST works regardless).

Org is `actual-chat`, and there is exactly **one** project: `actual-chat-ui` (platform
`dotnet-maui`).

What works and what doesn't:

- `GET /api/0/projects/` — works; the way to discover org + project slugs.
- `GET /api/0/organizations/` and `/api/0/users/me/` — **403**, the token lacks org scope.
- `GET /api/0/organizations/actual-chat/events/` (discover API) — **403**, same reason.
- `GET /api/0/projects/actual-chat/actual-chat-ui/issues/?statsPeriod=...` — works.
  `statsPeriod` accepts **only** `''`, `24h`, `14d`. Anything else (`90d`, `1h`) errors out.
- `GET /api/0/issues/{id}/events/?start=...&end=...` — works, and is the only way to get
  counts for a window narrower than 24h.

Gotcha: on the issues endpoint, `start`/`end` do **not** filter — `count`/`userCount` stay
period-wide totals. To scope to a short window, list issues by `sort=date`, keep those whose
`lastSeen` is inside it, then count each one's events via the per-issue events endpoint.

No `jq` or `python3` in the Docker container — use `pwsh` + `Invoke-RestMethod` to parse JSON.

## Is Sentry receiving anything at all?

`GET /api/0/projects/actual-chat/actual-chat-ui/stats/?stat=received&resolution=1d&since=<unix>&until=<unix>`
and the same with `stat=rejected`. **`rejected == received` means the org quota is exhausted** and
every event is being dropped at ingestion — the issue list then simply stops at the cut-off
date, which reads as "no new errors" unless you look at `lastSeen`. This happened 2026-09-18 →
month end (#4956): clients kept sending 1.4–4.6 K events/day, all rejected. The DSN key has no
rate limit of its own (`…/keys/` → `rateLimit: null`), and the token cannot read org billing.

Since #4956 only `Error`+ log events are sent (`MauiDiagnostics.ConfigureSentrySerilog`);
warnings are breadcrumbs on the error that follows them. Before that, warnings were 92% of the
volume, so a Sentry search for a warning-level diagnostic line finds nothing newer than 2.21.
