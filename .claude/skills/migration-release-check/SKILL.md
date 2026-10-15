---
name: migration-release-check
description: |
  Review the EF Core migrations a release (or a PR) carries against how Voxt is
  deployed: a rolling update, with the migration pod and the app pods starting
  at the same time. Use before cutting a release branch, before merging a PR
  that adds a migration, or when the user says "check the migrations", "are the
  migrations safe to ship", "expand/contract check", "/migration-release-check".
  Reports risks; never blocks, edits or runs anything on prod.
allowed-tools:
  - Bash
  - Read
  - Grep
  - Glob
  - mcp__voxt-robokitty-dev__run_sql_query
---

# /migration-release-check — what the migrations of a release can break on rollout

A report, not a gate. For every migration in the range it answers four
questions and says what would make the answer "safe". Nothing is changed:
no code, no migrations, no database. Prod numbers come from SQL the skill
**writes for the user** — nobody on the team has prod SQL access from here.

Background and the incident behind it: `tmp/s/2026-10-15-expand-contract-migrations.md`
(if it is not in your slot, the facts below are enough), issues #5313, #5327, #5331, #5350.

## Why each check exists

Deploy facts the checks rely on — verify them on every run (step 5), they drift:

- The **migration pod** (`actual-chat-migration`, `Dockerfile` target `migrations-app`)
  and the **app pods** are created together; the Deployment does not wait for the
  migration. App pods also migrate on start (`DbInitializer.MigrateDb`), serialized
  by EF's migration lock, so whichever takes the lock first runs the migration.
- A migration is **one transaction**. `DropColumn`, `AlterColumn`, `CreateIndex`,
  `DropIndex` hold `ACCESS EXCLUSIVE` on the table until it commits: every read
  and write of that table waits, and queued waiters can exhaust the DB's pool.
- Command timeouts: **30 s** on the app path (`DbInitializer.CommandTimeout`),
  **300 s** in the migration pod (`Command Timeout=300` in `entrypoint.sh`).
- The rollout mixes versions. **Old code meets the new schema** from the moment
  the migration commits until the last old pod is gone (dev 2026-10-15: ~2 min,
  324 `column ... does not exist`). **New code meets the old schema** if the
  migration is slow or failed and the app starts anyway (#5331; dev 2026-10-14:
  3 h of `null value in column "duration"`).
- EF selects every mapped column in every query, so a column the old model maps
  breaks *all* reads of that entity once it is dropped — not just the code that
  used the property.

The safe shape is **expand/contract** (parallel change): release N only adds or
relaxes (nullable / default) and stops using the old thing; release N+1 drops it.
With migrations generated from the model (CLAUDE.md → EF Core migrations), that
means the property stays in the model, nullable, for one release before it is
removed. Not a team rule — the report says where a migration departs from it and
what that costs.

## Arguments

- none — the range is `merge-base(latest origin/release/v*, origin/dev)..origin/dev`,
  i.e. what the next release will carry.
- `#5339` / a PR number — the migrations that PR adds (`gh pr diff <n> --name-only`).
- `<from>..<to>` — an explicit git range.

## Steps

### 1. Range and the previous release

```bash
git fetch -q origin
REL=$(git branch -r --list 'origin/release/v*' --sort=-version:refname | head -1 | tr -d ' ')
BASE=$(git merge-base "$REL" origin/dev)
```

`$REL` is the code that will still be running on the old pods during the rollout.
For a PR, the old code is `origin/dev` at the PR's base, but say in the report
that prod runs `$REL`, which may be older still.

### 2. The migrations

```bash
git diff --name-only --diff-filter=A "$BASE" origin/dev -- 'src/dotnet/*/Migrations/2*.cs' \
  | grep -v '\.Designer\.cs$'
```

A migration that was **modified** rather than added (`--diff-filter=M`) is a
finding of its own: if it already ran anywhere — dev applies `dev` on every
merge, prod applies release branches — the edit never runs there. Check with
`git branch -r --contains <the commit that added it> | grep release`.

For each file, list the operations of `Up()`:

```bash
git show origin/dev:<file> | awk '/override void Up/,/override void Down/' \
  | grep -oE 'migrationBuilder\.[A-Za-z]+' | sort | uniq -c
```

Then read `Up()` itself for every migration that has anything beyond
`CreateTable`, nullable `AddColumn` or `CreateIndex` on a new table.

### 3. Classify every operation

| Operation | Old code on new schema | New code on old schema | Lock |
|---|---|---|---|
| `CreateTable`, nullable `AddColumn` | safe | safe (new code must tolerate the table/column missing only if the migration can fail — see #5331) | short |
| `AddColumn` NOT NULL **with** default | safe | new code writing it fails until migrated | rewrite on old PG, metadata-only on PG ≥ 11 |
| `AddColumn` NOT NULL **without** default | **old inserts fail** | — | — |
| `DropColumn` / `DropTable` | **every query of the entity fails** unless the old model no longer maps it | safe if the new code has no property; **inserts fail** if the column is still NOT NULL in the old schema | `ACCESS EXCLUSIVE`, fast itself |
| `RenameColumn` / `RenameTable` | **fails** | **fails** | — |
| `AlterColumn` → nullable | safe | safe | short |
| `AlterColumn` → NOT NULL, type change | old writes of NULL / old type fail | — | may rewrite the table |
| `CreateIndex` on an existing table | safe | safe | **holds the lock while the index builds**; `unique: true` fails on duplicates |
| `DropIndex` | safe | safe | short; check the queries that used it |
| `DropPrimaryKey` / `AddPrimaryKey` | safe if columns unchanged | — | builds an index under the lock; fails on duplicates |
| `Sql(...)` | read it | read it | `DELETE` / `UPDATE` on a big table under the same transaction |

How to settle the two **old-code** questions instead of guessing:

- **Does the old model map the column?** Look at the model snapshot on `$REL`:
  `git show "$REL":src/dotnet/<Svc>.Service.Migration/Migrations/<Ctx>ModelSnapshot.cs | grep -n '"<column>"'`.
  Mapped → old pods break on every query of that entity after the drop.
- **Is it NOT NULL in the old schema?** In the same snapshot, the property's
  `.IsRequired()` or a non-nullable CLR type. NOT NULL + the new code no longer
  writing it = the 2026-10-14 incident shape.

Expand/contract verdict per dropped column: **follows** when the snapshot on
`$REL` already has it nullable *and* `git grep -n '<Property>' "$REL" -- 'src/dotnet/' ':!*/Migrations/*' ':!*/Db/Db*.cs'`
finds no use; otherwise **departs**, and the report names the window it opens.

Framework-owned tables (`_operations`, `_events`, `__EFMigrationsHistory`) count
too: old and new framework versions both write them on every command.

### 4. Data and size

For every table touched by a lock-holding operation or a `Sql(...)`:

- **dev** — run it yourself via `mcp__voxt-robokitty-dev__run_sql_query`
  (`database`: `chat`, `users`, `media`, `contacts`, `invite`, `notification`,
  `flows`, `mlsearch`, `oauth` — the `ac_dev_` prefix is implied). If the dev
  schema already has the migration applied, say so: the numbers are post-migration.
- **prod** — write the queries for the user to run on `ac_prod_<db>`; never claim
  prod numbers you did not get back from them.

```sql
-- size
SELECT count(*), pg_size_pretty(pg_total_relation_size('<table>')) FROM <table>;
-- a new unique index: duplicates that would fail it (after any DELETE the migration does)
SELECT <cols>, count(*) FROM <table> [WHERE <rows the migration keeps>]
GROUP BY <cols> HAVING count(*) > 1 LIMIT 10;
-- a Sql DELETE/UPDATE: how many rows, and whether anything still references them
SELECT count(*) FROM <table> WHERE <its predicate>;
```

Lock estimate: dev time for the same migration, if dev applied it, scaled by the
size ratio — and say it is an estimate. For a measurement on prod without a copy
and without a lock: `CREATE UNIQUE INDEX CONCURRENTLY tmp_measure ... ;` then
`DROP INDEX CONCURRENTLY tmp_measure;` (autocommit, outside a transaction). Over
**30 s** → the app path can time out and roll back.

Dev migration timing, when dev applied it:

```bash
gcloud logging read 'resource.labels.container_name="actual-chat-migration" AND textPayload:"<MigrationName>"' \
  --project=actual-chat-app-dev --freshness=30d --format='value(timestamp,textPayload)' --order=asc | head
```

That gives the start. For the end, read the same pod's log from that timestamp
(`resource.labels.pod_name="<pod>" AND timestamp>="<start>"`, add
`resource.labels.pod_name` to `--format`) — its next `Done.` line. Several
`Applying` lines for one migration mean earlier attempts failed and rolled back.

### 5. Deploy guards

Report each as OK / not OK; these decide whether a slow or failed migration
stays a stopped deploy or becomes an outage.

```bash
gh issue view 5331 --repo Actual-Chat/actual-chat --json state --jq .state   # server starts after a failed migration
grep -n 'set -e' Dockerfile                                                   # entrypoint stops on the first failure
grep -n 'CommandTimeout' src/dotnet/Db/DbInitializer.cs
# every entrypoint call has a bundle and vice versa; any output line is a mismatch
comm -3 \
  <(grep -oE 'bundle (--context [A-Za-z]+ )?--output \./artifacts/[A-Za-z.]+' Dockerfile | sed 's|.*artifacts/|./|' | sort) \
  <(grep -oE '^\./[A-Za-z.]+\.Migration\.exe' Dockerfile | sort)
```

### 6. Report

One table, then the prod SQL, then the guards:

```
Range    release/v2.21 (merge-base 4f3c…) .. origin/dev 380cf9fd4c — 24 migrations, 3 need attention

| Migration | Operation | Old code / new schema | New code / old schema | Lock | Verdict |
|---|---|---|---|---|---|
| Chat 20261014091148_RemoveUnusedChatEntryColumns | DropColumn kind, duration, audio_entry_id… | every chat_entries query fails (mapped on v2.21) | inserts fail: duration NOT NULL | 9 DropIndex + 8 CreateIndex + DELETE, one tx | departs |

Prod SQL (run on ac_prod_chat): …
Guards   #5331 OPEN · entrypoint without set -e · Flows.Service.Migration.exe called, not bundled · app timeout 30 s
```

Then, per departing migration, the cheapest way to make it safe — usually
"keep the property nullable for one release, drop it in the next", or "do the
`DELETE` ahead of the release in batches". Say what it costs (a release cycle,
splitting an already merged PR, re-aligning dev). The team decides.

## Common mistakes

| Mistake | Fix |
|---|---|
| Judging a drop by `git grep` for the property alone | EF selects every mapped column; check the `$REL` snapshot |
| Quoting dev numbers as prod | Prod comes only from SQL the user ran |
| Treating a migration dev already applied as freely editable | Dev will never rerun it; say what dev needs |
| Skipping framework tables | `_operations` / `_events` are written by every command on every pod |
| Calling the report a verdict to block the release | It lists risks and costs; the team decides |
