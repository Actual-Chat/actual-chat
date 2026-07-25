---
allowed-tools: Bash, Read, Grep, AskUserQuestion
description: Cherry-pick commits from `dev` into the current release branch (yours by default, `--all` for everyone's), then merge the release branch back into `dev` and push both.
argument-hint: [--all] [time frame]
---

# /cherry-pick-to-release

Port commits that landed on `dev` into the **current release branch**, skipping
anything already there, then merge the release branch back into `dev` and push
both branches.

This is a **mutating, outward-facing** command: it rewrites two shared branches
and pushes them. The one place it stops for the user is the commit-selection
question (and any cherry-pick conflict). Everything else runs straight through —
don't ask permission between mechanical steps.

## Arguments

`$ARGUMENTS` is free-form. Parse out two independent things, in any order:

**Scope:**

- (none) — only commits authored by the **current user** (default).
- `--all` / `all` / `everyone` — commits by **every** author.

**Time frame:**

- (none) — **every** commit on `dev` that isn't on the release branch.
- `today`, `last 2 days`, `last week`, `last 8 hours` — relative window.
- `since 2026-07-20`, `since yesterday` — absolute or named lower bound.
- `2026-07-20..2026-07-24` — explicit date range.

Anything else in `$ARGUMENTS` that reads like a content filter ("only the UI
ones", "video stuff") — keep it and apply it as the default selection when you
reach step 5, instead of asking.

## Reuse

- **`git log --cherry-pick --right-only A...B`** is the whole "which commits
  aren't there yet" engine — patch-id equivalence, built in. Don't hand-roll
  subject matching.
- **`git cherry-pick -x`** stamps `(cherry picked from commit <sha>)` into each
  message; step 4 reads those trailers back. Every run makes the next one
  smarter — never drop `-x`.
- **Identity resolution** matches `/my-changes` (`.claude/commands/my-changes.md`
  → *Whose commits*): `gh api user --jq .login`, `git config user.name`,
  `git config user.email`.
- **Release branch naming** comes from `version.json` → `release.branchName`
  = `release/v{version}`, same as `/prepare-release`.
- **Merge-back conflict handling** reuses `/prepare-release` step 7 verbatim.

## Steps

### 1. Preconditions

```bash
git status --porcelain
git branch --show-current
```

- Working tree must be **clean**. If it isn't, stop and tell the user — don't
  stash on their behalf.
- If a cherry-pick / merge / rebase is already in progress
  (`.git/CHERRY_PICK_HEAD`, `.git/MERGE_HEAD`, `.git/rebase-merge`), stop and
  say so.
- Remember the starting branch so you can restore it if you bail out early.

The **source** branch is always `dev`. If HEAD is something else, say so in the
opening report and use `dev` anyway.

### 2. Find and sync the release branch

```bash
git fetch origin --prune
git branch -r --format='%(refname:short)' \
  | grep -E '^origin/release/v[0-9]+(\.[0-9]+)?$' \
  | sed 's#^origin/release/v##' | sort -V | tail -1
```

That last `X.Y` is the **current release branch**, `release/vX.Y`. Sanity-check
it against `version.json`: `dev`'s version is the *next* minor with an `-alpha`
tag (e.g. `2.14-alpha` → release branch `release/v2.13`). If the two disagree,
report both and go with the highest `origin/release/v*` — but say that you did.

Sync both branches, fast-forward only:

```bash
git switch dev && git pull --ff-only
git switch release/vX.Y 2>/dev/null || git switch -c release/vX.Y --track origin/release/vX.Y
git pull --ff-only
git switch dev
```

If either `--ff-only` pull fails, the local branch has diverged from origin —
**stop**, show `git log --oneline --left-right origin/release/vX.Y...release/vX.Y`,
and let the user sort it out. Never force anything.

Report the release branch and its sync state before going further, e.g.:

> Release branch: **release/v2.13** (was 3 commits behind origin, now up to date)
> Source: **dev** (up to date)

### 3. Collect candidates

Run this **once**, unfiltered — the full symmetric difference:

```bash
git log --no-merges --cherry-pick --right-only \
  --format='%H%x09%an%x09%ae%x09%aI%x09%s' \
  origin/release/vX.Y...dev
```

**Do not add `--author` or `--since` to this command.** Both prune the traversal
on the *left* side too, which breaks `--cherry-pick`'s patch-id matching and
resurrects commits that are already on the release branch. Filter the output
afterwards instead.

### 4. Filter

Apply, in order, to the rows from step 3:

1. **Already cherry-picked** — collect the SHAs the release branch already
   credits and drop rows matching them (catches picks whose diff drifted, so
   patch-id no longer matches):

   ```bash
   git log --format='%b' origin/release/vX.Y \
     | grep -oiE 'cherry picked from commit [0-9a-f]{7,40}' \
     | grep -oE '[0-9a-f]{7,40}'
   ```

   Match by prefix — the trailers may hold short or full SHAs.

2. **Author** — unless `--all`, keep only rows whose author name or email
   matches the current user (see *Reuse*). A commit with a
   `Co-Authored-By: Claude …` trailer still belongs to its human author.

3. **Time frame** — if one was given, keep only rows inside it (the `%aI`
   column is ISO-8601, so string comparison works).

4. **Release housekeeping** — always drop `Set version to '…'` (nbgv) and
   `Merge branch …` subjects. They're branch bookkeeping and must not travel.

If nothing survives, say so plainly (`Nothing to cherry-pick — release/v2.13 is
already caught up with your commits on dev.`) and stop. No merge, no push.

### 5. Present and ask

**Always print the table first**, oldest-first (the order they'd be applied):

| # | Commit | Date | Author | Subject |
|---|--------|------|--------|---------|
| 1 | `c07a70d` | 07-25 | Alex Yakunin | fix(chat): gate "Start call" button behind IncompleteUI |

Drop the Author column when not in `--all` mode.

Then ask with `AskUserQuestion`. `AskUserQuestion` caps out at 4 questions ×
4 options and can't pre-check anything, so:

**≤ 16 commits** — one gating question, header `Selection`:

- `All N commits (Recommended)` — the default path
- `Pick individually` — leads to the checkbox follow-up below
- `Cancel`

…and tell the user in the message above it that they can hit **Other** and just
describe what they want ("only the UI ones", "1-5 and 9", "everything except the
docs commits").

If they choose `Pick individually`, follow up with up to 4 **multiSelect**
questions of up to 4 options each — one option per commit, labelled
`<short-sha> <subject, truncated to ~55 chars>`, headers `Commits 1-4`,
`Commits 5-8`, … The union of the checked boxes is the selection.

**> 16 commits** — skip the checkbox path entirely (it doesn't fit). Ask a
single question with `All N commits (Recommended)` / `Only mine` (only when
`--all` is in play) / `Cancel`, and lean on **Other** for free-form answers —
index ranges (`1-12, 15, 20-24`), short SHAs, or a description.

Resolve any free-form answer into concrete commits yourself, then **echo the
resolved list back in one line** and keep going. Don't ask a second time.

### 6. Cherry-pick

On the release branch, **oldest first**, one at a time so a failure is
attributable:

```bash
git switch release/vX.Y
git cherry-pick -x <sha>          # repeat, in chronological order
```

`-x` is mandatory — it's what step 4 reads on the next run.

**On conflict**, stop immediately and show:

```bash
git status --short --branch
git diff --name-only --diff-filter=U
```

Report which commit conflicted, which files, and how many picks already
succeeded (those are **already committed on the release branch** — aborting the
current pick does not undo them). Then ask how to proceed:

- `Resolve it` — fix the files, `git add`, `git cherry-pick --continue`
- `Skip this commit` — `git cherry-pick --skip`, carry on with the rest
- `Stop here` — `git cherry-pick --abort`, then jump to step 9 and report the
  partial state, leaving the successful picks in place (unpushed)

An **empty** cherry-pick (`The previous cherry-pick is now empty`) means the
change was already there in another form — `git cherry-pick --skip` it and note
it in the summary; that's not an error.

### 7. Merge the release branch back into dev

Only if at least one commit was picked.

```bash
git switch dev
git merge --no-ff release/vX.Y -m "Merge release/vX.Y into dev"
```

This usually merges cleanly — the picked commits' originals are already on
`dev`. If `version.json` conflicts (release has `X.Y`, dev has the next
`-alpha`), keep **dev's** side:

```bash
git checkout --ours version.json && git add version.json
git commit --no-edit
```

Any *other* conflict: stop, show it, and ask. Don't guess at content merges.

### 8. Push both

```bash
git push origin release/vX.Y
git push origin dev
```

Release branch first — pushing it is what triggers the release build; `dev`
follows. If a push is rejected as non-fast-forward, someone pushed in the
meantime: re-run `git pull --ff-only` on that branch and retry once. If it fails
again, stop and report. Never `--force`.

### 9. Report

A short summary, no ceremony:

```
Cherry-picked 6 of 8 commits into release/v2.13:

  c07a70d → 9f2a11b  fix(chat): gate "Start call" button behind IncompleteUI
  …

Skipped:
  e46a2b9  chore: minor edit          (not selected)
  12d50b5  feat(theme): Ctrl+Shift+L  (conflict — skipped)

Merged release/v2.13 into dev (clean). Pushed release/v2.13 and dev.
```

If you stopped early, say exactly where and what state the repo is in — which
branch is checked out, what's committed but unpushed, and the command to resume.
