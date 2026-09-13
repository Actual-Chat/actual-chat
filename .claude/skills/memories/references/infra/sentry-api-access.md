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
