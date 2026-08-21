---
name: soniox-sweep
description: |
  Inspect and manually purge the Soniox artifacts — transcriptions and uploaded
  audio files — that offline transcription leaves behind, on dev or prod. Use
  when offline transcription silently stops working, when Soniox answers 429
  `limit_exceeded`, when `SonioxTranscriberTest.OfflineTranscribeWorks` and
  `CleanerDeletesEnqueuedArtifacts` fail together, or when the user says
  "sweep Soniox", "soniox/sonoix sweep", "sonoix swipe", "purge Soniox",
  "check Soniox files", or "is Soniox at the cap".
allowed-tools:
  - Bash
  - Read
---

# Sweeping Soniox artifacts

Every offline transcription leaves two artifacts on Soniox: the uploaded
**file** and the **transcription**. Both count against caps enforced **per
organization**, not per key or per project — so dev and prod share one budget,
and a backlog on either starves both.

`SonioxCleaner` deletes both as each transcription ends, and `SonioxSweeper`
sweeps up whatever it dropped every ~4h. This skill is the manual fallback for
when they have already fallen behind — enough hosts died mid-transcription, or
the deployed server predates the fix.

## Two things to know before touching anything

**`GET /v1/files` under-reports.** A transcription holds its file, so an account
at the cap can list **0 files and 2000 transcriptions**. Always look at both,
and always delete transcriptions first — deleting one cascades to its file.

**The API allows ~500 requests/minute per organization, and live transcription
spends from the same budget.** A purge that runs flat out both fails with `429
limit_exceeded` and breaks transcription for real users while it runs. The
script below paces itself at 200/min, serialized. **Do not raise the rate or add
concurrency.** A full 2000-artifact purge takes ~10 minutes; that is the cost.

## Keys

| Environment | Key |
|---|---|
| dev | `$CoreSettings__SonioxKey` — already in the host/container env |
| prod | a key file the user names; **default `tmp/sonoix-prod.key`** |

The prod key is **not** in the environment and must be passed explicitly. If a
prod sweep is requested without a path, use `tmp/sonoix-prod.key` and say so. If
that file is missing, ask for the path — never fall back to the dev key for a
prod request, and never the reverse.

## Step 1 — always summarize first, never delete first

```bash
node .claude/skills/soniox-sweep/sweep.mjs                                  # dev
node .claude/skills/soniox-sweep/sweep.mjs --key-file tmp/sonoix-prod.key   # prod
```

Without `--purge` the script only reports. Report back to the user:

- totals for both kinds, and how many are older than the cutoff
- oldest and newest `created_at` — **a newest that is hours or days old means
  the leak is historical, not live**
- the per-day histogram — bursts on a few days point at restarts/deploys losing
  `SonioxCleaner`'s in-memory queue; a steady trickle points at a code path
  dropping ids
- status counts (`completed` / `error` / `processing`)

Exactly **2000 transcriptions with nothing recent** is the signature of a full
account: uploads are already 429ing, so nothing new can be created.

## Step 2 — ask before purging

**Never purge without explicit confirmation.** State the environment, the count,
the age range, and the ~10 min/2000 artifacts it will take, then wait for a yes.
Deletion is irreversible, and on prod it shares the rate limit with live users.

Anything still `processing` is left alone — Soniox answers its delete with 409,
and it may be a live transcription running on another host.

## Step 3 — purge

```bash
node .claude/skills/soniox-sweep/sweep.mjs --key-file tmp/sonoix-prod.key --purge --older-than 1h
```

`--older-than` defaults to `1h` and accepts `15m`-style values. Keep it above
the longest in-flight offline transcription so a live one isn't deleted
mid-flight: `1h` is safe, and `15m` — matching
`SonioxSweeper.Options.Retention` — is the practical floor.

The script re-lists both kinds afterwards; confirm it ends at 0 left, or explain
what didn't go.

## If the account keeps filling up

A purge buys time, it isn't the fix. Check, in order:

1. **Is the deployed server new enough?** `SonioxSweeper` only learned to sweep
   transcriptions in `11bbc663de` (2026-08-21). An older pod sweeps files only,
   sees the empty file list, and reports nothing — while transcriptions pile up
   invisibly. This is exactly how prod reached the cap on 2026-08-21.
2. **Server logs.** `SonioxCleaner` logs `Enqueue failed, leaving transcription
   {TranscriptionId} and file {FileId} on Soniox` at Warning; `SonioxSweeper`
   logs `Sweep: deleted N orphaned ...` at Information and `Sweep: rate-limited,
   retrying ...` at Warning. Search for `Soniox` via `/gcloud-prod-logs` or
   `/gcloud-dev-logs`.
3. **Restart churn.** The cleaner's queue lives in memory, so every host that
   dies mid-transcription drops the ids it holds. The sweeper is the backstop —
   if the backlog outruns one sweep per 4h, lower
   `SonioxSweeper.Options.Period`.
