---
name: merge-release-to-dev
description: |
  Merge the current release branch forward into `dev` and push `dev`, so fixes
  committed straight to `release/vX.Y` reach `dev` and the issues their
  `Closes #N` lines name get closed. Use when the user says "merge release into
  dev", "merge the release forward", "bring my release fixes to dev", "are my
  release fixes on dev?", "/merge-release-to-dev", or right after a release fix
  lands on `release/*`. `--dry-run` only reports what is missing.
allowed-tools:
  - Bash
  - AskUserQuestion
---

# /merge-release-to-dev — Merge the release branch forward

Nothing merges `release/*` into `dev` automatically. The team does it by hand,
pushing a `Merge release/vX.Y into dev` commit straight to `dev` — no pull
request. `/cherry-pick-to-release` does it as its last step, but only when it
picked something, so a fix committed directly to the release branch waits for
whoever merges next. Until then it is not on `dev`, and a `Closes #N` in its
message closes nothing: GitHub acts on closing keywords only when the commit
reaches the default branch, `dev`.

## Arguments

- none — merge and push.
- `--dry-run` — steps 1–2 only: report what `dev` is missing and stop.

## Invoking this skill is the permission to push `dev`

The standing rule is never to push unless the user asked in that message.
Running this skill is that ask: merge and push without re-confirming. It never
pushes the release branch (that triggers a release build) and never force-pushes.

## Reuse

- **Release branch discovery** is `/cherry-pick-to-release` step 2
  (`.claude/commands/cherry-pick-to-release.md`): the highest
  `origin/release/v*`, sanity-checked against `version.json`.
- **The `version.json` conflict rule** is the one `/cherry-pick-to-release`
  step 7 and `/prepare-release` use: keep `dev`'s side.

## Steps

### 1. Find the release branch

```bash
git fetch origin --prune
git branch -r --format='%(refname:short)' \
  | grep -E '^origin/release/v[0-9]+(\.[0-9]+)?$' \
  | sed 's#^origin/release/v##' | sort -V | tail -1
```

That `X.Y` gives `release/vX.Y`. Work from `origin/release/vX.Y` and
`origin/dev`, never from the local branches: the local `dev` may be stale or
checked out in another worktree.

If a local `release/vX.Y` exists and is ahead of origin, those commits would be
left out:

```bash
git rev-parse -q --verify refs/heads/release/vX.Y \
  && git rev-list --count origin/release/vX.Y..release/vX.Y
```

A count above 0 means unpushed commits. Show them and ask via
`AskUserQuestion`: `Merge origin only` or `Stop` (the user pushes them first —
pushing the release branch is not this skill's call).

### 2. List what `dev` is missing

```bash
git log --no-merges --reverse --format='%h%x09%an%x09%ad%x09%s' --date=format:'%m-%d %H:%M' \
  origin/dev..origin/release/vX.Y
```

Empty → say `dev already has everything on release/vX.Y` and stop.

Otherwise print a table, oldest first, with a **Kind** column:

- `copy` — the body has `(cherry picked from commit <sha>)`: the original is
  already on `dev`, and the merge only records it.
- `new` — no trailer: committed straight to the release branch. These are the
  ones that reach `dev` for the first time.

Then list the issues the merge will close:

```bash
git log --format=%B origin/dev..origin/release/vX.Y \
  | grep -oiE "^(close[sd]?|fix(e[sd])?|resolve[sd]?) #[0-9]+" | grep -oE '[0-9]+' | sort -u
```

Commits by other people go too — a merge forward takes the whole branch. Say
whose they are; that is expected, not a reason to stop.

`--dry-run` stops here.

### 3. Merge in a temporary worktree

The merge happens in a throwaway worktree on a detached `origin/dev`, so the
current checkout — its branch, its uncommitted changes — is never touched, and
nothing needs a clean tree or a stash. Put it in the scratchpad (or `tmp/`):

```bash
W=<scratchpad>/merge-release
git worktree add --detach "$W" origin/dev
git -C "$W" merge --no-ff origin/release/vX.Y -m "Merge release/vX.Y into dev"
```

A `version.json` conflict (release has `X.Y`, `dev` the next `-alpha`) — keep
`dev`'s side:

```bash
git -C "$W" checkout --ours version.json && git -C "$W" add version.json
git -C "$W" commit --no-edit
```

Any other conflict: capture `git -C "$W" diff --name-only --diff-filter=U`,
then `git -C "$W" merge --abort`, remove the worktree (step 5), show the files
and ask. Don't resolve content conflicts on a guess.

### 4. Push

```bash
git -C "$W" push origin HEAD:dev
```

Rejected as non-fast-forward → someone pushed `dev` meanwhile. Redo from a
fresh base once — the merge commit lives only on the worktree's detached HEAD,
so nothing is lost:

```bash
git -C "$W" fetch origin dev
git -C "$W" switch --detach origin/dev
git -C "$W" merge --no-ff origin/release/vX.Y -m "Merge release/vX.Y into dev"
git -C "$W" push origin HEAD:dev
```

Rejected again → stop and report. Never `--force`.

### 5. Clean up

```bash
git worktree remove "$W"
git fetch origin dev:dev 2>/dev/null || true
```

The second line fast-forwards a local `dev` when it can; it refuses, harmlessly,
when `dev` is the current branch, is checked out elsewhere or has local commits.

### 6. Report


```
Merged release/v2.22 into dev (a1b2c3d4e5, clean) and pushed dev:

  new   f173c72546  Frol  fix(ios): turn CallKit off on the China storefront
  new   d327a910a9  Frol  fix(ios-share): move the share extension to the app's RPC endpoint

Issues closed by the merge: #5520 (closed).
```

For each issue from step 2, check `gh issue view <N> --json state --jq .state`.
GitHub closes it within seconds of the push. Still `OPEN` → say so, and
note that it may need a manual close if the keyword was malformed. If you
stopped early, say where and what state the repo is in.
