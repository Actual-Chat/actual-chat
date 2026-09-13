Soniox meters two things **per organization**, not per key or per project — dev
and prod share both budgets.

**Stored artifacts.** When full, `POST /v1/files` returns `429
{"error_type":"limit_exceeded", ...}` and all offline transcription silently
degrades — `SonioxOfflineTranscriber` catches, logs, returns null. CI surfaces it
as `SonioxTranscriberTest.OfflineTranscribeWorks` + `CleanerDeletesEnqueuedArtifacts`
failing together. `GET /v1/files` **under-reports**: a transcription holds its
file, so a full account can list **0 files and 2000 transcriptions**. Purge both,
transcriptions first (deleting one cascades to its file).

**Requests per minute — ~500, shared with live transcription.** A flat-out purge
dies at ~500 requests *and* starves real users while it runs (message differs:
"Requests per minute limit for async transcription has been exceeded"). **Pace
bulk work at ≤200/min, serialized.** 2000 artifacts ≈ 10 min.

Use the `/soniox-sweep` skill (`.claude/skills/soniox-sweep/`, added
2026-08-21 in PR #4227) — it reports first, confirms, then purges at 200/min.
Prod key is not in the env; default path `tmp/sonoix-prod.key`. Dev key is
`$CoreSettings__SonioxKey`.

History: purged 342 artifacts 2026-08-13, then **2000 transcriptions on
2026-08-21** (prod, dating 08-07..08-19). The second backlog was invisible
because `SonioxSweeper` swept files only until `11bbc663de` (2026-08-21) — and
prod still ran older code. See [[server-loop-iteration-gotchas]] for the general
"deployed code is older than dev" trap.
