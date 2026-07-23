---
name: gcloud-dev-logs
version: 1.0.0
description: |
  Search Google Cloud (GKE) server logs of the DEV environment (dev.voxt.ai). Use when
  troubleshooting anything that needs the server side of the story — stream stalls, RPC
  peer changes / reconnects, watchdog closures, mesh/node churn, pod restarts — or when
  the user says "check GCP logs", "check server logs on dev", or gives a dev.voxt.ai
  timeline from a client console log.
allowed-tools:
  - Bash
---

# Searching DEV server logs in Google Cloud Logging

## Setup facts

- Project: `actual-chat-app-dev` (usually already the active `gcloud config` project).
- App container filter: `resource.labels.container_name="actual-chat-app"`.
- Auth: `gcloud auth list` must show an active account. If not, ask the user to run
  `! gcloud auth login` — never attempt it yourself.
- All log timestamps are UTC. Client console timestamps (dev.voxt.ai) are also UTC —
  they align 1:1.

## Query template

```bash
gcloud logging read \
  'resource.labels.container_name="actual-chat-app"
   AND timestamp>="2026-07-22T05:12:00Z" AND timestamp<="2026-07-22T05:17:00Z"
   AND textPayload:"<term>"' \
  --project=actual-chat-app-dev \
  --format='value(timestamp, textPayload)' \
  --order=asc | head -80
```

- Always bound `timestamp` on both ends; unbounded queries are slow and get truncated.
  Start with a ±3-5 min window around the incident and widen only if needed.
- `textPayload:"x"` is a substring match; combine with `AND`/`OR` in parentheses.
- `--order=asc` + `head` gives a readable timeline; default order is desc.
- Add `resource.labels.pod_name` to `--format='value(...)'` when multiple pods matter.

## Useful search terms

| Looking for | Term |
|---|---|
| A specific media stream | short stream-id fragment, e.g. `"XDWKB"` (ids look like `<nodeId>-01KY43XDWKB0…`; the prefix before `-01…` is the serving NODE id, not the author) |
| Publish start | `"PushStream: VideoRecord"` / `"PushStream: AudioRecord"` |
| Server killed a silent stream | `"Stale stream watchdog"` |
| Stream registration / viewers | `"RegisterActiveStream"`, `"GetStream: first frame"`, `"GetVideoRaw"` |
| Demand fan-out | `"SubscribeToDemand"`, `"DemandInfo"` |
| Mesh / pod churn (client peer-change trigger) | `"UpdateOnlineNodes"`, `"ComputeState"`, `"Rerouting"`, `"Shards @"` |
| Keyframe requests | `"RequestKeyFrame"` |

## Method

1. Take the incident time from the client console log (UTC) and query a bounded window.
2. Search by the most specific token first (stream id fragment), then widen to
   generic terms (`PushStream`, `watchdog`, `UpdateOnlineNodes`) in the same window.
3. Correlate: a burst of `UpdateOnlineNodes`/`Rerouting` = node churn — expect
   client-side `peerChanged=true` reconnects at the same moment.
