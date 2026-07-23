---
name: gcloud-prod-logs
version: 1.0.0
description: |
  Search Google Cloud (GKE) server logs of the PROD environment. Use when troubleshooting
  a production incident that needs the server side of the story — stream stalls, RPC peer
  changes / reconnects, watchdog closures, mesh/node churn, pod restarts — or when the
  user says "check PROD logs", "check production server logs", or gives a production
  timeline from a client console log.
allowed-tools:
  - Bash
---

# Searching PROD server logs in Google Cloud Logging

Same query shapes, search terms, and method as the DEV skill — read
`.claude/skills/gcloud-dev-logs/SKILL.md` for the search-term table and workflow.
PROD differences:

- Project: `actual-chat-app-prod`. The active `gcloud config` project is usually the
  DEV one, so passing `--project=actual-chat-app-prod` explicitly is mandatory.
- App container filter is the same: `resource.labels.container_name="actual-chat-app"`.
- PROD is read-only territory: only `gcloud logging read` — never mutate anything
  (no config changes, no restarts) in this project.

## Query template

```bash
gcloud logging read \
  'resource.labels.container_name="actual-chat-app"
   AND timestamp>="2026-07-22T05:12:00Z" AND timestamp<="2026-07-22T05:17:00Z"
   AND textPayload:"<term>"' \
  --project=actual-chat-app-prod \
  --format='value(timestamp, textPayload)' \
  --order=asc | head -80
```

Always bound `timestamp` on both ends (PROD volume is higher than DEV — unbounded
queries are slow and get truncated); start with a ±3-5 min window and widen only if
needed. Add `resource.labels.pod_name` to `--format='value(...)'` when multiple pods
matter.
